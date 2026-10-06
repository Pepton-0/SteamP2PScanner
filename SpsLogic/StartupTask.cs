using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace SpsLogic
{
    /// <summary>Installs SpsLauncher and WinDivert into Program Files so that the Sps directory can be deleted as a whole,
    /// and manages the logon task that starts SpsLauncher or uninstalls it once SpsGui.exe is gone.</summary>
    public static class StartupTask
    {
        public const string LauncherFileName = "SpsLauncher.exe";
        public const string SpsGuiExeName = "SpsGui.exe";

        /// <summary>Named event that SpsLauncher waits on. Setting it asks SpsLauncher to exit gracefully.</summary>
        public const string LauncherExitEventName = "SpsLauncher_Exit";

        /// <summary>Argument that tells SpsLauncher which Sps directory to watch.</summary>
        public const string SpsDirArgument = "--sps-dir";

        public const int LauncherExitTimeoutMilliseconds = 3000;

        private const string WinDivertDllName = "WinDivert.dll";
        private const string WinDivertSysName = "WinDivert64.sys";
        private const string TaskName = "SteamP2PScanner SpsLauncher";
        private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        private const int TASK_CREATE_OR_UPDATE = 6;
        private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;

        // Files SpsLauncher needs to run on its own, copied from the Sps directory.
        private static readonly string[] LauncherFiles =
        {
            LauncherFileName,
            LauncherFileName + ".config",
            "SpsLogic.exe",
            "Newtonsoft.Json.dll",
        };

        /// <summary>Directory where SpsLauncher and its copy of WinDivert are installed.</summary>
        public static string InstallDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SpsLauncher"); }
        }

        /// <summary>Full path to the installed SpsLauncher.exe.</summary>
        public static string InstalledLauncherPath
        {
            get { return Path.Combine(InstallDirectory, LauncherFileName); }
        }

        /// <summary>Copies SpsLauncher and WinDivert from the Sps directory unless the installed version equals
        /// <paramref name="version"/>. Requires administrator privileges.</summary>
        /// <returns>True when the launcher files are in place after the call.</returns>
        public static bool Install(string spsDirectory, string version)
        {
            try
            {
                if (ReadInstalledVersion() == version && File.Exists(InstalledLauncherPath))
                {
                    Logger.Log("SpsLauncher " + version + " is already installed.", true);
                    return true;
                }

                string installDir = InstallDirectory;
                Directory.CreateDirectory(installDir);

                // The launcher exe cannot be overwritten while it is running.
                StopLaunchers();

                foreach (string fileName in LauncherFiles)
                {
                    CopyIfExists(Path.Combine(spsDirectory, fileName), Path.Combine(installDir, fileName), required: true);
                }

                // Without the version file, the next startup retries the WinDivert copy.
                if (CopyWinDivert(spsDirectory, installDir))
                {
                    File.WriteAllText(VersionFilePath, version);
                }

                Logger.Log("Installed SpsLauncher " + version + " into " + installDir, true);
                return File.Exists(InstalledLauncherPath);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to install SpsLauncher, fall back to the Sps directory: " + ex.GetType().Name + ": " + ex.Message, true);
                return false;
            }
        }

        private static string VersionFilePath
        {
            get { return Path.Combine(InstallDirectory, "version"); }
        }

        /// <summary>Returns null when SpsLauncher is not installed or the version file cannot be read.</summary>
        private static string ReadInstalledVersion()
        {
            try
            {
                return File.Exists(VersionFilePath) ? File.ReadAllText(VersionFilePath).Trim() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <returns>False when the installed WinDivert was kept because it could not be replaced.</returns>
        private static bool CopyWinDivert(string spsDirectory, string installDir)
        {
            string destSys = Path.Combine(installDir, WinDivertSysName);
            try
            {
                CopyIfExists(Path.Combine(spsDirectory, WinDivertDllName), Path.Combine(installDir, WinDivertDllName), required: false);
                CopyIfExists(Path.Combine(spsDirectory, WinDivertSysName), destSys, required: false);
                return true;
            }
            catch (IOException)
            {
                // The driver is loaded from this file. Keep the installed version until the driver is unloaded.
                Logger.Log("WinDivert is in use in the install directory. Keeping the current copy.", true);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                Logger.Log("WinDivert in the install directory could not be replaced. Keeping the current copy.", true);
                return false;
            }
        }

        private static void CopyIfExists(string source, string destination, bool required)
        {
            if (!File.Exists(source))
            {
                if (required)
                {
                    throw new FileNotFoundException("Required file is missing.", source);
                }

                return;
            }

            File.Copy(source, destination, overwrite: true);
        }

        /// <summary>Creates or updates the logon task. Requires administrator privileges.</summary>
        public static bool SyncTask(string spsDirectory, bool autoRun)
        {
            try
            {
                GetRootFolder().RegisterTask(
                    TaskName, CreateTaskXml(spsDirectory, autoRun), TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
                Logger.Log("Synced the logon task (autoRun=" + autoRun + ", spsDir=" + spsDirectory + ").", true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to sync the logon task: " + ex.GetType().Name + ": " + ex.Message, true);
                return false;
            }
        }

        /// <summary>Checks whether the installed SpsLauncher is running.</summary>
        public static bool IsLauncherRunning()
        {
            string installed = InstalledLauncherPath;
            return FindLauncherProcessIds(path => string.Equals(path, installed, StringComparison.OrdinalIgnoreCase)).Length > 0;
        }

        /// <summary>Starts the installed SpsLauncher, through the logon task when possible.</summary>
        public static bool StartLauncher(string spsDirectory)
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
                Process.Start(new ProcessStartInfo(InstalledLauncherPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = InstallDirectory,
                    Arguments = CreateLauncherArguments(spsDirectory),
                }).Dispose();
                Logger.Log("Started SpsLauncher directly: " + InstalledLauncherPath, true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to start SpsLauncher: " + ex.GetType().Name + ": " + ex.Message, true);
                return false;
            }
        }

        /// <summary>
        /// Asks every running SpsLauncher to exit, and kills ones that do not exit within <see cref="LauncherExitTimeoutMilliseconds"/>.
        /// </summary>
        public static void StopLaunchers()
        {
            int[] processIds = FindLauncherProcessIds(path => true);
            if (processIds.Length == 0)
            {
                return;
            }

            // Graceful exit removes the tray icon. Killing leaves a ghost icon until the mouse hovers it.
            EventWaitHandle exitEvent;
            try
            {
                if (EventWaitHandle.TryOpenExisting(LauncherExitEventName, out exitEvent))
                {
                    using (exitEvent)
                    {
                        exitEvent.Set();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to open the SpsLauncher exit event: " + ex.GetType().Name + ": " + ex.Message, true);
            }

            foreach (int processId in processIds)
            {
                try
                {
                    using (Process process = Process.GetProcessById(processId))
                    {
                        if (!process.WaitForExit(LauncherExitTimeoutMilliseconds))
                        {
                            process.Kill();
                            Logger.Log("Killed SpsLauncher because it did not exit: pid=" + processId, true);
                        }
                        else
                        {
                            Logger.Log("Stopped SpsLauncher: pid=" + processId, true);
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

        private static string CreateTaskXml(string spsDirectory, bool autoRun)
        {
            string systemDirectory = Environment.SystemDirectory;
            string userId = WindowsIdentity.GetCurrent().Name;
            string powershell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(CreateTaskScript(spsDirectory, autoRun)));

            XNamespace ns = TaskNamespace;
            var task = new XElement(ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description",
                        "Starts SpsLauncher at logon while SpsGui exists; otherwise uninstalls it and deletes this task.")),
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
                    new XElement(ns + "ExecutionTimeLimit", "PT1H"),
                    new XElement(ns + "Enabled", "true")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        // conhost --headless hides the console window; -EncodedCommand needs no quoting of the script.
                        new XElement(ns + "Command", Path.Combine(systemDirectory, "conhost.exe")),
                        new XElement(ns + "Arguments",
                            "--headless \"" + powershell + "\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encodedScript))));

            return task.ToString();
        }

        private static string CreateTaskScript(string spsDirectory, bool autoRun)
        {
            string spsGui = Path.Combine(spsDirectory, SpsGuiExeName);
            string installDir = InstallDirectory;
            string launcher = InstalledLauncherPath;

            // While SpsGui exists, start SpsLauncher (AutoRun only). Otherwise stop WinDivert if loaded from here,
            // remove the install directory, and delete this task once the directory is gone.
            return
                "$ErrorActionPreference='SilentlyContinue'\n" +
                "$spsGui=" + PsLiteral(spsGui) + "\n" +
                "$installDir=" + PsLiteral(installDir) + "\n" +
                "$launcher=" + PsLiteral(launcher) + "\n" +
                "$launcherArgs=" + PsLiteral(CreateLauncherArguments(spsDirectory)) + "\n" +
                "$task=" + PsLiteral(TaskName) + "\n" +
                "$autoRun=$" + (autoRun ? "true" : "false") + "\n" +
                "if (Test-Path -LiteralPath $spsGui) {\n" +
                "  if ($autoRun) { Start-Process -FilePath $launcher -WorkingDirectory $installDir -ArgumentList $launcherArgs }\n" +
                "} else {\n" +
                "  $d = Get-CimInstance Win32_SystemDriver -Filter \"Name='WinDivert'\"\n" +
                "  if ($d -and $d.PathName -like ('*'+$installDir+'*')) { & sc.exe stop WinDivert | Out-Null }\n" +
                "  Get-Process SpsLauncher | Where-Object { $_.Path -and $_.Path.StartsWith($installDir) } | Stop-Process -Force\n" +
                "  Remove-Item -LiteralPath $installDir -Recurse -Force\n" +
                "  if (-not (Test-Path -LiteralPath $installDir)) { & schtasks.exe /delete /tn $task /f | Out-Null }\n" +
                "}\n";
        }

        private static string CreateLauncherArguments(string spsDirectory)
        {
            return SpsDirArgument + " " + CommandLine.Quote(spsDirectory);
        }

        private static string PsLiteral(string value)
        {
            // PowerShell single-quoted literal: only ' needs escaping, by doubling it.
            return "'" + value.Replace("'", "''") + "'";
        }
    }
}
