using SpsLogic;
using System;
using System.Threading;
using System.Windows.Forms;

namespace SpsLauncher
{
    internal static class Launcher
    {
        private const string SingleInstanceMutexName = "SpsLauncher_SingleInstance";

        private const int Success = 0;
        private const int Failure = 1;

        [STAThread]
        private static int Main(string[] args)
        {
            // Started from Task Scheduler etc., the current directory is not the exe directory.
            // Logger writes relative to the current directory, so align it with SpsGui.
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            Environment.CurrentDirectory = baseDirectory;

            if (args.Length > 0)
            {
                return RunCommand(args[0]);
            }

            RunResident(baseDirectory);
            return Success;
        }

        /// <summary>
        /// --register: register the logon task and start it. --unregister: delete the logon task.
        /// </summary>
        private static int RunCommand(string command)
        {
            switch (command)
            {
                case "--register":
                    // StartupTask logs the result.
                    return StartupTask.Register(StartupTask.DefaultLauncherPath, runNow: true) ? Success : Failure;
                case "--unregister":
                    return StartupTask.Unregister() ? Success : Failure;
                default:
                    LauncherLog.Write("Unknown command: " + command);
                    return Failure;
            }
        }

        private static void RunResident(string baseDirectory)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, SingleInstanceMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ResidentContext(baseDirectory));
            }
        }
    }

    /// <summary>
    /// Wraps <see cref="Logger"/>. The log file is shared with SpsGui, so a write collision must not crash this app.
    /// </summary>
    internal static class LauncherLog
    {
        // Logger keeps a static writer, so serialize the thread pool (polling) and UI thread writes.
        private static readonly object syncRoot = new object();

        public static void Write(string message)
        {
            lock (syncRoot)
            {
                try
                {
                    // The file line has no caller name, so the prefix tells it apart from SpsGui lines.
                    Logger.Log("[SpsLauncher] " + message, true);
                }
                catch (Exception)
                {
                    // Ignore: the file is probably held by SpsGui at this moment.
                }
            }
        }
    }
}
