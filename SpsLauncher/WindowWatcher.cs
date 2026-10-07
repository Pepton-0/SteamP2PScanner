using SpsLogic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace SpsLauncher
{
    /// <summary>Detects appeared and disappeared windows by polling <see cref="AppWindowFilter"/>.</summary>
    internal sealed class WindowWatcher : IDisposable
    {
        private readonly TimeSpan interval;
        private readonly Timer timer;
        private readonly uint ownProcessId;
        private Dictionary<IntPtr, WindowInfo> knownWindows = new Dictionary<IntPtr, WindowInfo>();
        // Seen once. Reported on the next poll if still present, so short-lived splash windows are ignored.
        private Dictionary<IntPtr, WindowInfo> pendingWindows = new Dictionary<IntPtr, WindowInfo>();
        private int polling;
        private bool disposed;

        /// <summary>Raised on a thread pool thread for a window seen in two consecutive polls after <see cref="Start"/>.</summary>
        public event EventHandler<WindowInfo> WindowAppeared;

        /// <summary>Raised on a thread pool thread for a known window that is gone.</summary>
        public event EventHandler<WindowInfo> WindowDisappeared;

        public WindowWatcher(TimeSpan interval)
        {
            this.interval = interval;
            using (Process process = Process.GetCurrentProcess())
            {
                ownProcessId = (uint)process.Id;
            }

            timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>Starts polling. Windows that exist now are not reported as appeared.</summary>
        public void Start()
        {
            knownWindows = SnapshotWindows();
            timer.Change(interval, interval);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            timer.Dispose();
        }

        private void OnTick(object state)
        {
            // Skip the tick if the previous one is still running.
            if (Interlocked.Exchange(ref polling, 1) == 1)
            {
                return;
            }

            try
            {
                Dictionary<IntPtr, WindowInfo> latest = SnapshotWindows();
                var appeared = new List<WindowInfo>();
                var nextPending = new Dictionary<IntPtr, WindowInfo>();
                foreach (KeyValuePair<IntPtr, WindowInfo> window in latest)
                {
                    if (knownWindows.ContainsKey(window.Key))
                    {
                        continue;
                    }

                    if (pendingWindows.ContainsKey(window.Key))
                    {
                        appeared.Add(window.Value);
                    }
                    else
                    {
                        nextPending[window.Key] = window.Value;
                    }
                }

                List<WindowInfo> disappeared = knownWindows
                    .Where(window => !latest.ContainsKey(window.Key))
                    .Select(window => window.Value)
                    .ToList();

                knownWindows = latest
                    .Where(window => !nextPending.ContainsKey(window.Key))
                    .ToDictionary(window => window.Key, window => window.Value);
                pendingWindows = nextPending;

                foreach (WindowInfo window in disappeared)
                {
                    WindowDisappeared?.Invoke(this, window);
                }

                foreach (WindowInfo window in appeared)
                {
                    WindowAppeared?.Invoke(this, window);
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Write("Window polling failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref polling, 0);
            }
        }

        /// <summary>True if a known or pending window belongs to the exe. Call from the event handlers.</summary>
        public bool HasWindowOf(string processPath)
        {
            return knownWindows.Values.Concat(pendingWindows.Values).Any(window =>
                string.Equals(window.ProcessPath, processPath, StringComparison.OrdinalIgnoreCase));
        }

        private Dictionary<IntPtr, WindowInfo> SnapshotWindows()
        {
            var windows = new Dictionary<IntPtr, WindowInfo>();
            AppWindowFilter.Enumerate(ownProcessId, window => windows[window.Handle] = window);
            return windows;
        }
    }
}
