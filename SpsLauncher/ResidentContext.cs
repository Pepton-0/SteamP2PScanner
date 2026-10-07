using SpsLogic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SpsLauncher
{
    /// <summary>Tray resident application. It starts SpsGui to monitor a game registered in game_config.json
    /// when the game's window appears.</summary>
    internal sealed class ResidentContext : ApplicationContext
    {
        private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2); // TODO change this value if its too long.

        private const string SpsGuiExeName = "SpsGui.exe";
        private const string TrayTitle = "SteamP2PScanner - Auto Monitoring";

        private readonly NotifyIcon notifyIcon;
        private readonly GameConfigWatcher configWatcher;
        private readonly WindowWatcher windowWatcher;
        private readonly string spsDirectory;
        private readonly string spsGuiPath;
        // Games SpsGui was started for. A game may show several windows.
        private readonly HashSet<uint> launchedProcessIds = new HashSet<uint>();
        // Set when config was refreshed for the last SpsGui exit, until a SpsGui window appears again.
        private bool spsGuiExitHandled;
        // Marshals the exit request from the thread pool to the UI thread.
        private readonly Control uiInvoker;
        private readonly EventWaitHandle exitEvent;
        private readonly RegisteredWaitHandle exitWait;

        public ResidentContext(string spsDirectory)
        {
            this.spsDirectory = spsDirectory;
            configWatcher = new GameConfigWatcher(Path.Combine(spsDirectory, GameConfig.RelativePath));
            configWatcher.Refresh(force: true);
            spsGuiPath = Path.Combine(spsDirectory, SpsGuiExeName);

            windowWatcher = new WindowWatcher(PollingInterval);
            windowWatcher.WindowAppeared += OnWindowAppeared;
            windowWatcher.WindowDisappeared += OnWindowDisappeared;
            windowWatcher.Start();

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripLabel(TrayTitle) { Font = new Font(menu.Font, FontStyle.Bold) });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit Auto Monitor", null, (s, e) => ExitThread());

            notifyIcon = new NotifyIcon
            {
                Icon = AppIcon.Load(SystemInformation.SmallIconSize),
                Text = TrayTitle,
                ContextMenuStrip = menu,
                Visible = true,
            };

            uiInvoker = new Control();
            // An invisible control has no window handle until Handle is read, and BeginInvoke needs one.
            IntPtr invokerHandle = uiInvoker.Handle;

            // SpsGui sets this event when AutoRun is turned off, so exit gracefully and remove the tray icon.
            exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, StartupTask.LauncherExitEventName);
            exitWait = ThreadPool.RegisterWaitForSingleObject(
                exitEvent,
                (state, timedOut) =>
                {
                    LauncherLog.Write("Exit requested.");
                    uiInvoker.BeginInvoke((Action)ExitThread);
                },
                null,
                Timeout.Infinite,
                true);

            LauncherLog.Write("Started. config=" + configWatcher.ConfigPath);
        }

        private void OnWindowAppeared(object sender, WindowInfo window)
        {
            if (IsSpsGuiWindow(window))
            {
                spsGuiExitHandled = false;
                return;
            }

            string steamAppId;
            if (!configWatcher.Current.TryGetSteamAppId(window.ProcessPath, out steamAppId))
            {
                return;
            }

            launchedProcessIds.RemoveWhere(processId => !IsProcessRunning(processId));
            if (launchedProcessIds.Contains(window.ProcessId))
            {
                return;
            }

            var options = new SpsGuiStartupOptions(steamAppId, window.ProcessId, window.Handle);
            if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(SpsGuiExeName)).Length > 0)
            {
                LauncherLog.Write("Registered game appeared but SpsGui is already running: " + options);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(spsGuiPath, options.ToArguments())
                {
                    UseShellExecute = true,
                    // SpsGui resolves config and logs against the current directory.
                    WorkingDirectory = spsDirectory,
                }).Dispose();
                launchedProcessIds.Add(window.ProcessId);
                LauncherLog.Write("Started SpsGui for " + options + ", processPath=" + window.ProcessPath);
            }
            catch (Exception ex)
            {
                LauncherLog.Write("Failed to start SpsGui: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void OnWindowDisappeared(object sender, WindowInfo window)
        {
            // Only SpsGui writes game_config.json, so its exit (no SpsGui window left) is the moment to pick up registrations.
            if (!IsSpsGuiWindow(window) || spsGuiExitHandled || windowWatcher.HasWindowOf(spsGuiPath))
            {
                return;
            }

            spsGuiExitHandled = true;
            LauncherLog.Write("Refreshed observation because SpsGui exited.");
            configWatcher.Refresh();
        }

        private bool IsSpsGuiWindow(WindowInfo window)
        {
            return string.Equals(window.ProcessPath, spsGuiPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProcessRunning(uint processId)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    return !process.HasExited;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        protected override void ExitThreadCore()
        {
            LauncherLog.Write("Exit.");
            TimeSpan exitStart = Logger.GetTimestamp();
            windowWatcher.Dispose();
            exitWait.Unregister(null);
            exitEvent.Dispose();
            uiInvoker.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            LauncherLog.Write("Exit cleanup: " + Logger.GetElapsedMillsec(exitStart) + " ms");
            base.ExitThreadCore();
        }
    }
}
