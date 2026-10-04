using SpsLogic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SpsLauncher
{
    /// <summary>
    /// Detects started and exited processes by polling the process list.
    /// Polling is used instead of WMI process traces or Process.Exited because those need
    /// administrator privileges, or rights an elevated SpsGui does not grant to this process.
    /// </summary>
    internal sealed class ProcessWatcher : IDisposable
    {
        private readonly TimeSpan interval;
        private readonly Timer timer;
        // pid to exe path. The path is null when it could not be read (protected process etc.).
        private Dictionary<int, string> knownProcesses;
        private int polling;
        private bool disposed;

        /// <summary>
        /// Raised on a thread pool thread for each process started after <see cref="Start"/>.
        /// </summary>
        public event EventHandler<ProcessEventArgs> ProcessLaunched;

        /// <summary>
        /// Raised on a thread pool thread for each exited process whose path was known,
        /// including processes already running at <see cref="Start"/>.
        /// </summary>
        public event EventHandler<ProcessEventArgs> ProcessExited;

        public ProcessWatcher(TimeSpan interval)
        {
            this.interval = interval;
            timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Takes the current process list as a baseline and starts polling.
        /// Processes already running at this point are not reported as launched.
        /// </summary>
        public void Start()
        {
            knownProcesses = new Dictionary<int, string>();
            foreach (int processId in SnapshotProcessIds())
            {
                knownProcesses[processId] = WinApi.TryGetProcessPath(processId);
            }

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
                HashSet<int> latestIds = SnapshotProcessIds();
                var latest = new Dictionary<int, string>(latestIds.Count);
                var launched = new List<ProcessEventArgs>();
                foreach (int processId in latestIds)
                {
                    string processPath;
                    if (!knownProcesses.TryGetValue(processId, out processPath))
                    {
                        processPath = WinApi.TryGetProcessPath(processId);
                        if (processPath != null)
                        {
                            launched.Add(new ProcessEventArgs(processId, processPath));
                        }
                    }

                    latest[processId] = processPath;
                }

                var exited = new List<ProcessEventArgs>();
                foreach (KeyValuePair<int, string> known in knownProcesses)
                {
                    if (known.Value != null && !latest.ContainsKey(known.Key))
                    {
                        exited.Add(new ProcessEventArgs(known.Key, known.Value));
                    }
                }

                knownProcesses = latest;

                foreach (ProcessEventArgs args in exited)
                {
                    ProcessExited?.Invoke(this, args);
                }

                foreach (ProcessEventArgs args in launched)
                {
                    ProcessLaunched?.Invoke(this, args);
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Write("Process polling failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref polling, 0);
            }
        }

        private static HashSet<int> SnapshotProcessIds()
        {
            var ids = new HashSet<int>();
            foreach (Process process in Process.GetProcesses())
            {
                ids.Add(process.Id);
                process.Dispose();
            }

            return ids;
        }
    }

    internal sealed class ProcessEventArgs : EventArgs
    {
        public ProcessEventArgs(int processId, string processPath)
        {
            ProcessId = processId;
            ProcessPath = processPath;
        }

        public int ProcessId { get; private set; }

        public string ProcessPath { get; private set; }
    }
}
