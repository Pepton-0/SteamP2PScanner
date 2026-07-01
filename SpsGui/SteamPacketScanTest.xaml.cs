using SpsLogic;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace SpsGui
{
    /// <summary>
    /// Provides an application-window test surface for monitoring Armored Core 6 Steam P2P traffic.
    /// </summary>
    public partial class SteamPacketScanTest : Window
    {
        private const string TargetGameProcessName = "armoredcore6";

        private readonly DispatcherTimer updateTimer;
        private readonly List<PingProfileSnapshot> oldProfiles = new List<PingProfileSnapshot>();
        private IPacketScan packetScan;
        private SteamPeerManager steamPeerManager;
        private bool steamApiInitialized;
        private bool monitoringStarted;
        private int timerTickCount;
        private int executedUpdateCount;
        private string lastUpdateResult = "not called";

        /// <summary>
        /// Gets the fixed Steam game process name used by this test window.
        /// </summary>
        public string GameProcessName => TargetGameProcessName;

        /// <summary>
        /// Gets the Steam IPC log path used by <see cref="SteamPeerManager"/>.
        /// </summary>
        public string SteamLogPath => AppConfig.Instance?.SteamLogPath ?? "-";

        /// <summary>
        /// Gets packet scan rows displayed with the same visual shape as the overlay profiler.
        /// </summary>
        public ObservableCollection<PingProfileSnapshot> PacketProfiles { get; } =
            new ObservableCollection<PingProfileSnapshot>();

        /// <summary>
        /// Gets the Steam peers currently reported by <see cref="SteamPeerManager"/>.
        /// </summary>
        public ObservableCollection<SteamPeerDisplaySnapshot> Peers { get; } =
            new ObservableCollection<SteamPeerDisplaySnapshot>();

        /// <summary>
        /// Initializes the Armored Core 6 Steam packet scan test window.
        /// </summary>
        public SteamPacketScanTest()
        {
            InitializeComponent();
            DataContext = this;

            updateTimer = new DispatcherTimer(DispatcherPriority.Background);
            updateTimer.Interval = TimeSpan.FromSeconds(1);
            updateTimer.Tick += UpdateTimer_Tick;

            SteamApiTextBlock.Text = "not initialized";
            PacketScanTextBlock.Text = "not constructed";
            RefreshUpdateStatus();
            AppendLog("SteamPacketScanTest initialized.");
        }

        /// <summary>
        /// Starts Steam API, PacketScan, and Steam peer monitoring for the configured game process.
        /// </summary>
        /// <param name="sender">Button that requested monitoring to start.</param>
        /// <param name="e">Click event payload.</param>
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
        }

        /// <summary>
        /// Runs one manual monitor update.
        /// </summary>
        /// <param name="sender">Button that requested an update.</param>
        /// <param name="e">Click event payload.</param>
        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            TryUpdateMonitoring("manual");
        }

        /// <summary>
        /// Stops timers and disposes packet capture resources.
        /// </summary>
        /// <param name="sender">Button that requested monitoring to stop.</param>
        /// <param name="e">Click event payload.</param>
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopMonitoring("manual");
        }

        /// <summary>
        /// Updates Steam peer discovery and packet scan histories when automatic updates are enabled.
        /// </summary>
        /// <param name="sender">Timer that requested an update.</param>
        /// <param name="e">Timer event payload.</param>
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            timerTickCount++;

            if (AutoUpdateCheckBox.IsChecked == true)
            {
                TryUpdateMonitoring("timer");
            }

            RefreshUpdateStatus();
        }

        /// <summary>
        /// Releases native Steam and capture resources before the window closes.
        /// </summary>
        /// <param name="sender">Window that is closing.</param>
        /// <param name="e">Cancel event payload.</param>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            StopMonitoring("window closing");
        }

        /// <summary>
        /// Builds the runtime monitoring objects and starts periodic updates.
        /// </summary>
        /// <returns>True when all monitoring resources were created successfully.</returns>
        private bool StartMonitoring()
        {
            try
            {
                if (monitoringStarted)
                {
                    AppendLog("Start skipped: monitoring already started.");
                    return true;
                }

                InitializeSteamApi();
                packetScan = new PacketScan(ReadPatienceMilliseconds());
                steamPeerManager = new SteamPeerManager(packetScan, TargetGameProcessName);

                monitoringStarted = true;
                PacketScanTextBlock.Text = "constructed";
                StateTextBlock.Text = "SteamPacketScan: monitoring";
                updateTimer.Start();
                TryUpdateMonitoring("startup");
                AppendLog($"Monitoring started for process={TargetGameProcessName}.");
                return true;
            }
            catch (Exception ex)
            {
                AppendException("Start failed", ex);
                StopMonitoring("start failed");
                StateTextBlock.Text = "SteamPacketScan: start failed";
                return false;
            }
        }

        /// <summary>
        /// Initializes Steamworks.NET once for Steam peer inspection.
        /// </summary>
        private void InitializeSteamApi()
        {
            if (steamApiInitialized)
            {
                return;
            }

            Environment.SetEnvironmentVariable("SteamAppId", 1888160.ToString());

            if (!SteamAPI.IsSteamRunning())
            {
                throw new InvalidOperationException("Steam is not running.");
            }

            if (!SteamAPI.Init())
            {
                throw new InvalidOperationException("SteamAPI.Init returned false.");
            }

            steamApiInitialized = true;
            SteamApiTextBlock.Text = "initialized";
            AppendLog("Steam API initialized.");
        }

        /// <summary>
        /// Stops monitoring and releases owned unmanaged resources.
        /// </summary>
        /// <param name="reason">Short label describing why monitoring stopped.</param>
        private void StopMonitoring(string reason)
        {
            updateTimer.Stop();
            monitoringStarted = false;
            steamPeerManager = null;

            if (packetScan != null)
            {
                try
                {
                    packetScan.Dispose();
                }
                catch (Exception ex)
                {
                    AppendException("PacketScan dispose failed", ex);
                }
                finally
                {
                    packetScan = null;
                }
            }

            if (steamApiInitialized)
            {
                SteamAPI.Shutdown();
                steamApiInitialized = false;
                SteamApiTextBlock.Text = "shutdown";
            }

            PacketScanTextBlock.Text = "not constructed";
            StateTextBlock.Text = "SteamPacketScan: stopped";
            PeerCountTextBlock.Text = Peers.Count.ToString(CultureInfo.InvariantCulture);
            RefreshUpdateStatus();
            AppendLog($"Monitoring stopped: {reason}.");
        }

        /// <summary>
        /// Updates Steam callbacks, peer discovery, peer state, packet loss checks, and UI snapshots.
        /// </summary>
        /// <param name="source">Short label identifying the update caller.</param>
        /// <returns>True when the update completed without throwing.</returns>
        private bool TryUpdateMonitoring(string source)
        {
            try
            {
                if (!monitoringStarted || packetScan == null || steamPeerManager == null)
                {
                    lastUpdateResult = $"{source}: skipped at {DateTime.Now:HH:mm:ss}";
                    RefreshUpdateStatus();
                    return false;
                }

                SteamAPI.RunCallbacks();
                steamPeerManager.UpdatePeerList();
                steamPeerManager.UpdatePeerStats();
                packetScan.Update();

                executedUpdateCount++;
                lastUpdateResult = $"{source}: executed at {DateTime.Now:HH:mm:ss}";
                LastUpdateTextBlock.Text = lastUpdateResult;
                RefreshPeers();
                RefreshPacketProfiles();
                RefreshUpdateStatus();
                return true;
            }
            catch (Exception ex)
            {
                lastUpdateResult = $"{source}: threw {ex.GetType().Name} at {DateTime.Now:HH:mm:ss}";
                AppendException($"Update failed ({source})", ex);
                RefreshUpdateStatus();
                return false;
            }
        }

        /// <summary>
        /// Reads the packet loss timeout in milliseconds from the UI.
        /// </summary>
        /// <returns>Positive timeout in milliseconds.</returns>
        private double ReadPatienceMilliseconds()
        {
            double value;
            if (!double.TryParse(PatienceTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                value <= 0)
            {
                throw new InvalidOperationException("Patience ms must be a positive number.");
            }

            return value;
        }

        /// <summary>
        /// Copies the current Steam peer list into bindable display rows.
        /// </summary>
        private void RefreshPeers()
        {
            Peers.Clear();

            foreach (SteamPeerBase peer in steamPeerManager.GetPeers())
            {
                Peers.Add(SteamPeerDisplaySnapshot.FromPeer(peer));
            }

            PeerCountTextBlock.Text = Peers.Count.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Copies active and newly old PacketScan histories into overlay-style profiler rows.
        /// </summary>
        private void RefreshPacketProfiles()
        {
            var profiles = new List<PingProfileSnapshot>();

            packetScan.ForEachActiveHistory((state, netId, history) =>
            {
                profiles.Add(PingProfileSnapshot.FromPacketScan(state, netId, history));
            });

            foreach (PacketScan.PlayerPingHistory history in packetScan.TakeUnseenOldHistories())
            {
                oldProfiles.Add(PingProfileSnapshot.FromPacketScan("Old", null, history));
            }

            profiles.AddRange(oldProfiles);

            PacketProfiles.Clear();
            foreach (PingProfileSnapshot profile in profiles)
            {
                PacketProfiles.Add(profile);
            }
        }

        /// <summary>
        /// Rewrites the update status line using current counters.
        /// </summary>
        private void RefreshUpdateStatus()
        {
            if (UpdateStatusTextBlock == null)
            {
                return;
            }

            UpdateStatusTextBlock.Text =
                $"Update: ticks={timerTickCount}, executed={executedUpdateCount}, last={lastUpdateResult}";
        }

        /// <summary>
        /// Appends a short exception summary to the test log.
        /// </summary>
        /// <param name="label">Operation label that failed.</param>
        /// <param name="ex">Exception raised by the operation.</param>
        private void AppendException(string label, Exception ex)
        {
            AppendLog($"{label}: {ex.GetType().Name}: {ex.Message}");
        }

        /// <summary>
        /// Appends a timestamped line to the local test log.
        /// </summary>
        /// <param name="message">Message to append as a single log line.</param>
        private void AppendLog(string message)
        {
            if (LogTextBox == null)
            {
                return;
            }

            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            LogTextBox.ScrollToEnd();
        }
    }

    /// <summary>
    /// Immutable display snapshot for a Steam peer row.
    /// </summary>
    public sealed class SteamPeerDisplaySnapshot
    {
        /// <summary>
        /// Gets the Steam persona name captured for the row.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the Steam ID rendered as an unsigned integer string.
        /// </summary>
        public string SteamIDText { get; private set; }

        /// <summary>
        /// Gets the connection API name reported by the peer.
        /// </summary>
        public string ConnectionTypeName { get; private set; }

        /// <summary>
        /// Gets whether the peer route uses Steam relay or direct P2P.
        /// </summary>
        public string RouteText { get; private set; }

        /// <summary>
        /// Creates a display row from a live Steam peer.
        /// </summary>
        /// <param name="peer">Connected Steam peer to copy values from; must not be null.</param>
        /// <returns>Detached snapshot suitable for WPF binding.</returns>
        public static SteamPeerDisplaySnapshot FromPeer(SteamPeerBase peer)
        {
            if (peer == null)
            {
                throw new ArgumentNullException(nameof(peer));
            }

            return new SteamPeerDisplaySnapshot
            {
                Name = peer.Name,
                SteamIDText = peer.SteamID.m_SteamID.ToString(CultureInfo.InvariantCulture),
                ConnectionTypeName = peer.ConnectionTypeName,
                RouteText = peer.UsingRelay ? "relay" : "direct"
            };
        }
    }
}
