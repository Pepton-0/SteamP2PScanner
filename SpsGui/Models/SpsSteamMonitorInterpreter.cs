using Newtonsoft.Json;
using SpsLogic;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace SpsGui.Models
{
    /// <summary>
    /// Owns the SteamMonitor process and translates its messages into Sps-side actions.
    /// </summary>
    public sealed class SpsSteamMonitorInterpreter : ISteamMonitorInterpreter
    {
        private const int ShutdownWaitMilliseconds = 2000;

        private readonly IPacketScan packetScan;
        private readonly object inputLock = new object();
        private readonly Process process;
        private int requestIndex;
        private bool disposed;
        private bool childReportedExit;

        public SpsSteamMonitorInterpreter(IPacketScan packetScan, SteamAppInfo appInfo)
        {
            this.packetScan = packetScan ?? throw new ArgumentNullException(nameof(packetScan));
            if (appInfo == null)
            {
                throw new ArgumentNullException(nameof(appInfo));
            }

            process = StartProcess(appInfo);
        }

        public void UpdatePeerList()
        {
            SendCommand(SteamMonitorMessageType.UpdatePeerList);
        }

        public void UpdatePeerStats()
        {
            SendCommand(SteamMonitorMessageType.UpdatePeerStats);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            try
            {
                if (!process.HasExited)
                {
                    SendCommand(SteamMonitorMessageType.Shutdown);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to send SteamMonitor shutdown: " + ex.Message, true);
            }

            try
            {
                if (!process.WaitForExit(ShutdownWaitMilliseconds) && !process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to stop SteamMonitor: " + ex.Message, true);
            }
            finally
            {
                process.Dispose();
            }
        }

        private Process StartProcess(SteamAppInfo appInfo)
        {
            string exePath = ResolveSteamMonitorPath();
            var startInfo = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                Arguments = BuildArguments(appInfo)
            };

            var child = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            child.OutputDataReceived += OnOutputDataReceived;
            child.ErrorDataReceived += OnErrorDataReceived;
            child.Exited += OnExited;

            if (!child.Start())
            {
                throw new InvalidOperationException("SteamMonitor process did not start.");
            }

            child.BeginOutputReadLine();
            child.BeginErrorReadLine();

            Logger.Log("Started SteamMonitor: pid=" + child.Id.ToString(CultureInfo.InvariantCulture), true);
            return child;
        }

        private static string ResolveSteamMonitorPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string exePath = Path.Combine(baseDir, "SteamMonitor.exe");
            if (File.Exists(exePath))
            {
                return exePath;
            }

            throw new FileNotFoundException("SteamMonitor.exe was not found next to SpsGui.exe.", exePath);
        }

        private static string BuildArguments(SteamAppInfo appInfo)
        {
            int parentPid = Process.GetCurrentProcess().Id;
            int targetPid = appInfo.Info == null ? 0 : (int)appInfo.Info.ProcessId;
            string gameProcessName = appInfo.Info == null ? string.Empty : appInfo.Info.ProcessName;

            return
                "--steamAppId " + Quote(appInfo.SteamAppId) + " " +
                "--gameProcessName " + Quote(gameProcessName) + " " +
                "--parentPid " + parentPid.ToString(CultureInfo.InvariantCulture) + " " +
                "--targetPid " + targetPid.ToString(CultureInfo.InvariantCulture);
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                value = string.Empty;
            }

            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private void SendCommand(string type)
        {
            if (disposed && type != SteamMonitorMessageType.Shutdown)
            {
                return;
            }

            if (process.HasExited)
            {
                return;
            }

            var message = new SteamMonitorMessage
            {
                Type = type,
                RequestId = (++requestIndex).ToString(CultureInfo.InvariantCulture)
            };

            string json = JsonConvert.SerializeObject(message);
            lock (inputLock)
            {
                process.StandardInput.WriteLine(json);
                process.StandardInput.Flush();
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            SteamMonitorMessage message;
            try
            {
                message = JsonConvert.DeserializeObject<SteamMonitorMessage>(e.Data);
            }
            catch (Exception ex)
            {
                Logger.Log("Invalid SteamMonitor message: " + ex.Message + " line=" + e.Data, true);
                return;
            }

            if (message == null)
            {
                return;
            }

            HandleMessage(message);
        }

        private void HandleMessage(SteamMonitorMessage message)
        {
            if (message.Type == SteamMonitorMessageType.Register)
            {
                packetScan.Register(message.NetId, message.Name, message.Id);
            }
            else if (message.Type == SteamMonitorMessageType.Unregister)
            {
                packetScan.Unregister(message.NetId);
            }
            else if (message.Type == SteamMonitorMessageType.Log)
            {
                Logger.Log("SteamMonitor: " + message.Message, message.LeaveToFile);
            }
            else if (message.Type == SteamMonitorMessageType.Error)
            {
                Logger.Log("SteamMonitor error: " + message.Message, true);
            }
            else if (message.Type == SteamMonitorMessageType.Exited)
            {
                childReportedExit = true;
                Logger.Log("SteamMonitor exited: " + message.Reason, true);
            }
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Logger.Log("SteamMonitor stderr: " + e.Data, true);
            }
        }

        private void OnExited(object sender, EventArgs e)
        {
            if (!disposed && !childReportedExit)
            {
                Logger.Log("SteamMonitor process exited unexpectedly: code=" + process.ExitCode.ToString(CultureInfo.InvariantCulture), true);
            }
        }
    }
}
