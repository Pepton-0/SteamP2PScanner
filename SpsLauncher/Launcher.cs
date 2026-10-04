using SpsLogic;
using System;
using System.Threading;
using System.Windows.Forms;

namespace SpsLauncher
{
    internal static class Launcher
    {
        private const string SingleInstanceMutexName = "SpsLauncher_SingleInstance";

        [STAThread]
        private static void Main(string[] args)
        {
            // SpsLauncher runs from Program Files; the Sps directory to watch is passed as an argument.
            // Keep the current directory at the exe directory so logs go next to SpsLauncher.exe.
            Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string spsDirectory = GetSpsDirectory(args);
            if (spsDirectory == null)
            {
                LauncherLog.Write("Exit because " + StartupTask.SpsDirArgument + " is missing.");
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, SingleInstanceMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ResidentContext(spsDirectory));
            }
        }

        /// <summary>
        /// Reads the Sps directory passed as <c>--sps-dir &lt;path&gt;</c>. Returns null when absent.
        /// </summary>
        private static string GetSpsDirectory(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], StartupTask.SpsDirArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }

    /// <summary>Wraps <see cref="Logger"/> so that a failed log write never crashes the resident app.</summary>
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
                    Logger.Log("[SpsLauncher] " + message, true);
                }
                catch (Exception)
                {
                    // Logging is best effort.
                }
            }
        }
    }
}
