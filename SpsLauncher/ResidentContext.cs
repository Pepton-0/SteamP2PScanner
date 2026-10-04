using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SpsLauncher
{
    /// <summary>
    /// Tray resident application. It watches process launches and reports the ones registered in game_config.json.
    /// </summary>
    internal sealed class ResidentContext : ApplicationContext
    {
        private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2); // TODO change this value if its too long.

        private const string SpsGuiExeName = "SpsGui.exe";

        private readonly NotifyIcon notifyIcon;
        private readonly GameConfigWatcher configWatcher;
        private readonly ProcessWatcher processWatcher;
        private readonly string spsGuiPath;
        // Marshals the exit request from the thread pool to the UI thread.
        private readonly Control uiInvoker;
        private readonly EventWaitHandle exitEvent;
        private readonly RegisteredWaitHandle exitWait;

        public ResidentContext(string baseDirectory)
        {
            configWatcher = new GameConfigWatcher(Path.Combine(baseDirectory, SpsLogic.GameConfig.RelativePath));
            configWatcher.Refresh(force: true);
            spsGuiPath = Path.Combine(baseDirectory, SpsGuiExeName);

            processWatcher = new ProcessWatcher(PollingInterval);
            processWatcher.ProcessLaunched += OnProcessLaunched;
            processWatcher.ProcessExited += OnProcessExited;
            processWatcher.Start();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Exit", null, (s, e) => ExitThread());

            notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "SpsLauncher",
                ContextMenuStrip = menu,
                Visible = true,
            };

            uiInvoker = new Control();
            // An invisible control has no window handle until Handle is read, and BeginInvoke needs one.
            IntPtr invokerHandle = uiInvoker.Handle;

            // SpsGui sets this event when AutoRun is turned off, so exit gracefully and remove the tray icon.
            exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SpsLogic.StartupTask.LauncherExitEventName);
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

        private void OnProcessLaunched(object sender, ProcessEventArgs e)
        {
            string steamAppId;
            if (!configWatcher.Current.TryGetSteamAppId(e.ProcessPath, out steamAppId))
            {
                return;
            }

            // Step 2: only report. Launching SpsGui comes later.
            LauncherLog.Write(
                "Registered game launched: steamAppId=" + steamAppId +
                ", pid=" + e.ProcessId +
                ", processPath=" + e.ProcessPath);
        }

        private void OnProcessExited(object sender, ProcessEventArgs e)
        {
            // Only SpsGui writes game_config.json, so its exit is the moment to pick up registrations.
            if (!string.Equals(e.ProcessPath, spsGuiPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            LauncherLog.Write("Refreshed observation because SpsGui exited.");
            configWatcher.Refresh();
        }

        protected override void ExitThreadCore()
        {
            LauncherLog.Write("Exit.");
            TimeSpan exitStart = SpsLogic.Logger.GetTimestamp();
            processWatcher.Dispose();
            exitWait.Unregister(null);
            exitEvent.Dispose();
            uiInvoker.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            LauncherLog.Write("Exit cleanup: " + SpsLogic.Logger.GetElapsedMillsec(exitStart) + " ms");
            base.ExitThreadCore();
        }
    }
}
