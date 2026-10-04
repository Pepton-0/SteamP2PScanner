using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Xml.Linq;

namespace SpsLogic
{
    public enum StartupTaskState
    {
        NotRegistered,
        /// <summary>Registered for the given SpsLauncher.exe.</summary>
        Registered,
        /// <summary>Registered for SpsLauncher.exe in another directory (e.g. the Sps directory was moved).</summary>
        RegisteredForOtherPath,
    }

    /// <summary>
    /// Registers SpsLauncher as a logon task of Task Scheduler.
    /// A task with "run with highest privileges" is the only way to auto start an elevated app without a UAC prompt.
    /// </summary>
    /// <remarks>
    /// The task does not start SpsLauncher.exe directly. It runs a headless cmd that starts SpsLauncher.exe
    /// if it still exists, and otherwise deletes the task itself. So deleting the Sps directory also
    /// removes the logon task on the next logon, without placing any file outside the Sps directory.
    /// The Task Scheduler COM API is used instead of schtasks.exe output, which is encoded with the console code page.
    /// </remarks>
    public static class StartupTask
    {
        public const string LauncherFileName = "SpsLauncher.exe";

        /// <summary>
        /// Named event that SpsLauncher waits on. Setting it asks SpsLauncher to exit gracefully.
        /// </summary>
        public const string LauncherExitEventName = "SpsLauncher_Exit";

        public const int LauncherExitTimeoutMilliseconds = 3000;
        private const string TaskName = "SteamP2PScanner SpsLauncher";
        private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        private const int TASK_CREATE_OR_UPDATE = 6;
        private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;

        /// <summary>
        /// Gets SpsLauncher.exe next to the running exe.
        /// </summary>
        public static string DefaultLauncherPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LauncherFileName); }
        }

        /// <summary>
        /// Checks the logon task. Never throws; returns <see cref="StartupTaskState.NotRegistered"/> on failure.
        /// </summary>
        /// <param name="launcherPath">Full path to SpsLauncher.exe to compare with the registered one.</param>
        public static StartupTaskState GetState(string launcherPath)
        {
            try
            {
                dynamic task = GetRootFolder().GetTask(TaskName);
                XDocument definition = XDocument.Parse((string)task.Xml);
                XNamespace ns = TaskNamespace;
                bool isSamePath = definition.Descendants(ns + "Arguments").Any(arguments =>
                    arguments.Value.IndexOf("\"" + launcherPath + "\"", StringComparison.OrdinalIgnoreCase) >= 0);
                return isSamePath ? StartupTaskState.Registered : StartupTaskState.RegisteredForOtherPath;
            }
            catch (FileNotFoundException)
            {
                return StartupTaskState.NotRegistered;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to query the logon task: " + ex.GetType().Name + ": " + ex.Message, true);
                return StartupTaskState.NotRegistered;
            }
        }

        /// <summary>
        /// Creates or overwrites the logon task for the current user. Requires administrator privileges.
        /// </summary>
        /// <param name="launcherPath">Full path to SpsLauncher.exe.</param>
        /// <param name="runNow">Start the task right after registration, as it runs at logon.</param>
        public static bool Register(string launcherPath, bool runNow)
        {
            if (!File.Exists(launcherPath))
            {
                Logger.Log("Cannot register the logon task because SpsLauncher is missing: " + launcherPath, true);
                return false;
            }

            if (launcherPath.IndexOf('"') >= 0 || launcherPath.IndexOf('%') >= 0)
            {
                // These characters cannot be passed through cmd safely.
                Logger.Log("Cannot register the logon task because the path has '\"' or '%': " + launcherPath, true);
                return false;
            }

            try
            {
                dynamic task = GetRootFolder().RegisterTask(
                    TaskName, CreateTaskXml(launcherPath), TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
                Logger.Log("Registered the logon task: " + launcherPath, true);
                if (runNow)
                {
                    task.Run(null);
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to register the logon task: " + ex.GetType().Name + ": " + ex.Message, true);
                return false;
            }
        }

        /// <summary>
        /// Deletes the logon task. Requires administrator privileges. Succeeds when the task does not exist.
        /// </summary>
        public static bool Unregister()
        {
            try
            {
                GetRootFolder().DeleteTask(TaskName, 0);
                Logger.Log("Unregistered the logon task.", true);
                return true;
            }
            catch (FileNotFoundException)
            {
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to unregister the logon task: " + ex.GetType().Name + ": " + ex.Message, true);
                return false;
            }
        }

        /// <summary>
        /// Checks whether SpsLauncher at the path is running.
        /// </summary>
        public static bool IsLauncherRunning(string launcherPath)
        {
            return FindLauncherProcessIds(path => string.Equals(path, launcherPath, StringComparison.OrdinalIgnoreCase)).Length > 0;
        }

        /// <summary>
        /// Starts SpsLauncher through the logon task so that it runs exactly as it does at logon.
        /// Falls back to starting the exe directly when the task cannot be run.
        /// </summary>
        public static bool StartLauncher(string launcherPath)
        {
            try
            {
                GetRootFolder().GetTask(TaskName).Run(null);
                Logger.Log("Started SpsLauncher through the logon task.", true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to run the logon task, start SpsLauncher directly: " + ex.GetType().Name + ": " + ex.Message, true);
            }

            try
            {
                Process.Start(new ProcessStartInfo(launcherPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(launcherPath),
                }).Dispose();
                Logger.Log("Started SpsLauncher directly: " + launcherPath, true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to start SpsLauncher: " + ex.GetType().Name + ": " + ex.Message, true);
                return false;
            }
        }

        /// <summary>
        /// Asks every running SpsLauncher (in any directory) to exit, and kills ones that do not exit in time.
        /// Blocks the caller for up to <see cref="LauncherExitTimeoutMilliseconds"/>.
        /// </summary>
        public static void StopLaunchers()
        {
            TimeSpan step = Logger.GetTimestamp();
            int[] processIds = FindLauncherProcessIds(path => true);
            Logger.Log("StopLaunchers found " + processIds.Length + " process(es): " + Logger.GetElapsedMillsec(step) + " ms", true);
            if (processIds.Length == 0)
            {
                return;
            }

            // Graceful exit removes the tray icon. Killing leaves a ghost icon until the mouse hovers it.
            step = Logger.GetTimestamp();
            EventWaitHandle exitEvent;
            bool signaled = false;
            try
            {
                if (EventWaitHandle.TryOpenExisting(LauncherExitEventName, out exitEvent))
                {
                    using (exitEvent)
                    {
                        exitEvent.Set();
                        signaled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // e.g. UnauthorizedAccessException when the event was created with a different integrity level.
                Logger.Log("Failed to open the SpsLauncher exit event: " + ex.GetType().Name + ": " + ex.Message, true);
            }

            // Not signaled means SpsLauncher (e.g. an old build) does not listen. Each process then waits for the full timeout.
            Logger.Log("StopLaunchers exit event signaled=" + signaled + ": " + Logger.GetElapsedMillsec(step) + " ms", true);

            foreach (int processId in processIds)
            {
                step = Logger.GetTimestamp();
                try
                {
                    using (Process process = Process.GetProcessById(processId))
                    {
                        if (!process.WaitForExit(LauncherExitTimeoutMilliseconds))
                        {
                            process.Kill();
                            Logger.Log("Killed SpsLauncher because it did not exit: pid=" + processId + ", " + Logger.GetElapsedMillsec(step) + " ms", true);
                        }
                        else
                        {
                            Logger.Log("Stopped SpsLauncher: pid=" + processId + ", " + Logger.GetElapsedMillsec(step) + " ms", true);
                        }
                    }
                }
                catch (ArgumentException)
                {
                    // Already exited.
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to stop SpsLauncher: pid=" + processId + ", " + ex.GetType().Name + ": " + ex.Message, true);
                }
            }
        }

        private static int[] FindLauncherProcessIds(Func<string, bool> isTarget)
        {
            var processIds = new List<int>();
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(LauncherFileName)))
            {
                using (process)
                {
                    string path = WinApi.TryGetProcessPath(process.Id);
                    if (path != null && isTarget(path))
                    {
                        processIds.Add(process.Id);
                    }
                }
            }

            return processIds.ToArray();
        }

        private static dynamic GetRootFolder()
        {
            Type serviceType = Type.GetTypeFromProgID("Schedule.Service", true);
            dynamic service = Activator.CreateInstance(serviceType);
            service.Connect();
            return service.GetFolder("\\");
        }

        private static string CreateTaskXml(string launcherPath)
        {
            string directory = Path.GetDirectoryName(launcherPath);
            string systemDirectory = Environment.SystemDirectory;
            string userId = WindowsIdentity.GetCurrent().Name;

            // if exist "<exe>" ( start "" /d "<dir>" "<exe>" ) else ( schtasks /delete /tn "<task>" /f )
            // conhost --headless splits and re-quotes the arguments for cmd, so every quoted part must be
            // a separate token. Keep spaces around the parentheses.
            string script =
                "if exist \"" + launcherPath + "\" " +
                "( start \"\" /d \"" + directory + "\" \"" + launcherPath + "\" ) " +
                "else ( \"" + Path.Combine(systemDirectory, "schtasks.exe") + "\" /delete /tn \"" + TaskName + "\" /f )";

            XNamespace ns = TaskNamespace;
            var task = new XElement(ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description",
                        "Starts SpsLauncher at logon. Deletes itself when " + launcherPath + " no longer exists.")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger",
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "UserId", userId))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", userId),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    // The default limit is 72 hours. Never stop the resident app.
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Enabled", "true")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        // conhost --headless runs cmd without flashing a console window at logon.
                        new XElement(ns + "Command", Path.Combine(systemDirectory, "conhost.exe")),
                        new XElement(ns + "Arguments",
                            "--headless \"" + Path.Combine(systemDirectory, "cmd.exe") + "\" /d /c " + script))));

            return task.ToString();
        }
    }
}
