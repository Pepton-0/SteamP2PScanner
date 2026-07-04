using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpsGui.Models.Services;
using SpsLogic;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SpsGui.ViewModels
{
    /// <summary>
    /// Provides live profiling state for the selected Steam application.
    /// </summary>
    public class ProfileScreenViewModel : ObservableObject, IDisposable
    {
        private const int DnsDelayMilliseconds = 1000;
        private const int DnsPatienceMilliseconds = 3000;
        private const string DnsQueryName = "example.com";

        private readonly SteamAppInfo appInfo;
        private readonly SteamPeerManager manager;
        private readonly IPacketScan packetScan;
        private readonly IDialogService dialogService;
        private readonly IOverlayService overlayService;
        private readonly DispatcherTimer updateTimer;
        private readonly Dictionary<string, PingProfileSnapshot> activeSnapshots =
            new Dictionary<string, PingProfileSnapshot>(StringComparer.OrdinalIgnoreCase);
        private DnsPing dnsPing;
        private PingProfileSnapshot dnsSnapshot;
        private int previousActiveCount;
        private bool isDisposed;

        /// <summary>
        /// Initializes the profiling screen and starts periodic packet monitor updates.
        /// </summary>
        /// <param name="appInfo">Selected Steam application information. Must not be null.</param>
        /// <param name="manager">Steam peer manager prepared for the selected application. Must not be null.</param>
        /// <param name="packetScan">Packet scanner shared with the peer manager. Must not be null.</param>
        /// <param name="dialogService">Service used to show modal dialogs.</param>
        /// <param name="overlayService">Service used to update overlay rows.</param>
        public ProfileScreenViewModel(
            SteamAppInfo appInfo,
            SteamPeerManager manager,
            IPacketScan packetScan,
            IDialogService dialogService,
            IOverlayService overlayService)
        {
            this.appInfo = appInfo ?? throw new ArgumentNullException(nameof(appInfo));
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.packetScan = packetScan ?? throw new ArgumentNullException(nameof(packetScan));
            this.dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            this.overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));

            ExportArchiveCommand = new AsyncRelayCommand<PingProfileSnapshot>(ExportArchiveAsync, CanExportArchive);

            updateTimer = new DispatcherTimer(DispatcherPriority.Background);
            updateTimer.Interval = TimeSpan.FromSeconds(1);
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();
        }

        /// <summary>
        /// Gets current active ping monitor rows.
        /// </summary>
        public ObservableCollection<PingProfileSnapshot> CurrentProfiles { get; } =
            new ObservableCollection<PingProfileSnapshot>();

        /// <summary>
        /// Gets past ping monitor rows kept for the current application lifetime.
        /// </summary>
        public ObservableCollection<PingProfileSnapshot> HistoryProfiles { get; } =
            new ObservableCollection<PingProfileSnapshot>();

        /// <summary>
        /// Gets the command that saves a packet archive and shows its path.
        /// </summary>
        public IAsyncRelayCommand<PingProfileSnapshot> ExportArchiveCommand { get; private set; }

        /// <summary>
        /// Gets the detected or registered Steam application id.
        /// </summary>
        public string SteamAppId
        {
            get { return appInfo.SteamAppId; }
        }

        /// <summary>
        /// Gets or sets whether the box plot should be shown in overlay-like views.
        /// </summary>
        public bool ShowBoxPlot
        {
            get { return AppConfig.Instance.ShowBoxPlot; }
            set
            {
                if (AppConfig.Instance.ShowBoxPlot != value)
                {
                    AppConfig.Instance.ShowBoxPlot = value;
                    OnPropertyChanged(nameof(ShowBoxPlot));
                    OnPropertyChanged(nameof(OverlayShowBoxPlot));
                }
            }
        }

        public bool OverlayEnabled
        {
            get { return AppConfig.Instance.OverlayEnabled; }
            set
            {
                if (AppConfig.Instance.OverlayEnabled != value)
                {
                    AppConfig.Instance.OverlayEnabled = value;
                    OnPropertyChanged(nameof(OverlayEnabled));
                }
            }
        }

        public bool OverlayShowName
        {
            get { return AppConfig.Instance.OverlayShowName; }
            set
            {
                if (AppConfig.Instance.OverlayShowName != value)
                {
                    AppConfig.Instance.OverlayShowName = value;
                    OnPropertyChanged(nameof(OverlayShowName));
                }
            }
        }

        public bool OverlayShowStatus
        {
            get { return AppConfig.Instance.OverlayShowStatus; }
            set
            {
                if (AppConfig.Instance.OverlayShowStatus != value)
                {
                    AppConfig.Instance.OverlayShowStatus = value;
                    OnPropertyChanged(nameof(OverlayShowStatus));
                }
            }
        }

        public bool OverlayShowAverage
        {
            get { return AppConfig.Instance.OverlayShowAverage; }
            set
            {
                if (AppConfig.Instance.OverlayShowAverage != value)
                {
                    AppConfig.Instance.OverlayShowAverage = value;
                    OnPropertyChanged(nameof(OverlayShowAverage));
                }
            }
        }

        public bool OverlayShowLoss
        {
            get { return AppConfig.Instance.OverlayShowLoss; }
            set
            {
                if (AppConfig.Instance.OverlayShowLoss != value)
                {
                    AppConfig.Instance.OverlayShowLoss = value;
                    OnPropertyChanged(nameof(OverlayShowLoss));
                }
            }
        }

        public bool OverlayShowBoxPlot
        {
            get { return AppConfig.Instance.ShowBoxPlot; }
            set
            {
                if (AppConfig.Instance.ShowBoxPlot != value)
                {
                    AppConfig.Instance.ShowBoxPlot = value;
                    OnPropertyChanged(nameof(OverlayShowBoxPlot));
                    OnPropertyChanged(nameof(ShowBoxPlot));
                }
            }
        }

        public bool OverlayShowChart
        {
            get { return AppConfig.Instance.OverlayShowChart; }
            set
            {
                if (AppConfig.Instance.OverlayShowChart != value)
                {
                    AppConfig.Instance.OverlayShowChart = value;
                    OnPropertyChanged(nameof(OverlayShowChart));
                }
            }
        }

        /// <summary>
        /// Stops timers and network monitors owned by this view model.
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            updateTimer.Stop();
            updateTimer.Tick -= UpdateTimer_Tick;
            overlayService.Close();
            StopDnsPing();
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                SteamAPI.RunCallbacks();
                manager.UpdatePeerList();
                manager.UpdatePeerStats();
                packetScan.Update();
                RefreshCurrentProfiles();
                RefreshHistoryProfiles();
            }
            catch (Exception ex)
            {
                Logger.Log("Profile update failed: " + ex.GetType().Name + ": " + ex.Message, true);
            }
        }

        private void RefreshCurrentProfiles()
        {
            var next = new List<PingProfileSnapshot>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var peers = manager.GetPeers().ToArray();

            packetScan.ForEachActiveHistory((state, netId, history) =>
            {
                string key = CreateSnapshotKey(netId, history);
                seenKeys.Add(key);

                PingProfileSnapshot snapshot;
                if (!activeSnapshots.TryGetValue(key, out snapshot))
                {
                    snapshot = PingProfileSnapshot.FromPacketScan(state, netId, history);
                    activeSnapshots[key] = snapshot;
                }
                else
                {
                    snapshot.RefreshFromStats();
                }

                ApplyPeerInfo(snapshot, peers);
                next.Add(snapshot);
            });

            foreach (string key in activeSnapshots.Keys.ToArray())
            {
                if (!seenKeys.Contains(key))
                {
                    activeSnapshots.Remove(key);
                }
            }

            UpdateDnsPing(next.Count);
            if (dnsSnapshot != null)
            {
                AppendDnsSamples();
                next.Add(dnsSnapshot);
            }

            ReplaceCollection(CurrentProfiles, next);
            overlayService.UpdateProfiles(CurrentProfiles);
            previousActiveCount = next.Count(snapshot => !snapshot.UsingDns);
        }

        private void RefreshHistoryProfiles()
        {
            foreach (PacketScan.PlayerPingHistory history in packetScan.TakeUnseenOldHistories())
            {
                var snapshot = PingProfileSnapshot.FromPacketScan("Old", null, history);
                HistoryProfiles.Insert(0, snapshot);
            }

            ExportArchiveCommand.NotifyCanExecuteChanged();
        }

        private void UpdateDnsPing(int activePeerCount)
        {
            if (previousActiveCount == 0 && activePeerCount > 0)
            {
                StartDnsPing();
            }
            else if (previousActiveCount > 0 && activePeerCount == 0)
            {
                StopDnsPing();
            }
        }

        private void StartDnsPing()
        {
            if (dnsPing != null || string.IsNullOrEmpty(AppConfig.Instance.DnsIp))
            {
                return;
            }

            try
            {
                var endpoint = new IPEndPoint(IPAddress.Parse(AppConfig.Instance.DnsIp), 53);
                dnsPing = new DnsPing(endpoint, DnsQueryName, DnsDelayMilliseconds, DnsPatienceMilliseconds);
                dnsPing.Start();
                dnsSnapshot = PingProfileSnapshot.CreateDns(
                    new ConnectionStats(10, "DNS " + AppConfig.Instance.DnsIp),
                    new double[0]);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to start DNS ping: " + ex.GetType().Name + ": " + ex.Message, true);
                StopDnsPing();
            }
        }

        private void StopDnsPing()
        {
            if (dnsPing != null)
            {
                dnsPing.Dispose();
                dnsPing = null;
            }

            dnsSnapshot = null;
        }

        private void AppendDnsSamples()
        {
            if (dnsPing == null || dnsSnapshot == null)
            {
                return;
            }

            foreach (double sample in dnsPing.ExtractQueuedPings())
            {
                dnsSnapshot.PushPing(sample);
            }
        }

        private async Task ExportArchiveAsync(PingProfileSnapshot snapshot)
        {
            if (snapshot == null || snapshot.PacketArchive == null)
            {
                return;
            }

            try
            {
                string fileName = CreateArchiveFileName(snapshot);
                await snapshot.PacketArchive.SaveCaptureAsync(fileName).ConfigureAwait(true);
                string path = Path.GetFullPath(Path.Combine("archives", fileName));
                dialogService.ShowArchivePathDialog(path);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to export archive: " + ex.GetType().Name + ": " + ex.Message, true);
                MessageBox.Show("Failed to export archive: " + ex.Message);
            }
        }

        private bool CanExportArchive(PingProfileSnapshot snapshot)
        {
            return snapshot != null && snapshot.PacketArchive != null;
        }

        private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
        {
            collection.Clear();
            foreach (T item in items)
            {
                collection.Add(item);
            }
        }

        private static string CreateSnapshotKey(ulong? netId, PacketScan.PlayerPingHistory history)
        {
            if (netId.HasValue)
            {
                return netId.Value.ToString(CultureInfo.InvariantCulture);
            }

            return history.Stats.Name;
        }

        private static void ApplyPeerInfo(PingProfileSnapshot snapshot, SteamPeerBase[] peers)
        {
            SteamPeerBase peer = peers.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, snapshot.Name, StringComparison.OrdinalIgnoreCase));

            if (peer != null)
            {
                snapshot.SetSteamPeerInfo(peer.SteamID.m_SteamID, peer.UsingRelay);
            }
        }

        private static string CreateArchiveFileName(PingProfileSnapshot snapshot)
        {
            string safeName = string.Concat((snapshot.Name ?? "peer")
                .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            return safeName + "_" + snapshot.StartedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".pcap";
        }
    }

    /// <summary>
    /// Bindable snapshot for a Steam or DNS ping profile row.
    /// </summary>
    public sealed class PingProfileSnapshot : ObservableObject
    {
        private readonly ConnectionStats sourceStats;
        private string state = "-";
        private string name;
        private DateTime startedAt;
        private double min;
        private double max;
        private double avg;
        private double loss;
        private double q1;
        private double med;
        private double q3;
        private double[] recentPings = new double[0];
        private ulong? netIdValue;
        private PacketScan.PacketArchive packetArchive;
        private ulong steamID;
        private bool usingRelay;
        private bool usingDns;

        private PingProfileSnapshot(ConnectionStats stats)
        {
            sourceStats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <summary>
        /// Creates a DNS profile snapshot from a statistics source and initial samples.
        /// </summary>
        /// <param name="stats">Statistics object to back the snapshot. Must not be null.</param>
        /// <param name="initialPings">Initial ping samples. Null is treated as empty.</param>
        /// <returns>A DNS snapshot refreshed from the supplied samples.</returns>
        public static PingProfileSnapshot CreateDns(ConnectionStats stats, IEnumerable<double> initialPings)
        {
            var snapshot = new PingProfileSnapshot(stats);
            snapshot.PacketArchive = null;
            snapshot.UsingRelay = false;
            snapshot.UsingDns = true;

            foreach (double ping in initialPings ?? new double[0])
            {
                stats.PushPing(ping);
            }

            snapshot.RefreshFromStats();
            return snapshot;
        }

        /// <summary>
        /// Creates a Steam packet scan snapshot from a packet history.
        /// </summary>
        /// <param name="state">Short state label for the history. Null is displayed as "-".</param>
        /// <param name="netId">Optional packet scan network identifier.</param>
        /// <param name="history">Packet scan history backing the row. Must not be null.</param>
        /// <returns>A Steam snapshot refreshed from the packet history statistics.</returns>
        public static PingProfileSnapshot FromPacketScan(
            string state,
            ulong? netId,
            PacketScan.PlayerPingHistory history)
        {
            if (history == null)
            {
                throw new ArgumentNullException(nameof(history));
            }

            var snapshot = new PingProfileSnapshot(history.Stats);
            snapshot.State = string.IsNullOrWhiteSpace(state) ? "-" : state;
            snapshot.NetIdValue = netId;
            snapshot.PacketArchive = history.Archive;
            snapshot.UsingRelay = false;
            snapshot.UsingDns = false;
            snapshot.RefreshFromStats();
            return snapshot;
        }

        /// <summary>
        /// Gets the row state label.
        /// </summary>
        public string State
        {
            get { return state; }
            private set { SetProperty(ref state, value); }
        }

        /// <summary>
        /// Gets the display name for the monitored endpoint.
        /// </summary>
        public string Name
        {
            get { return name; }
            private set { SetProperty(ref name, value); }
        }

        /// <summary>
        /// Gets the time when the statistics source started.
        /// </summary>
        public DateTime StartedAt
        {
            get { return startedAt; }
            private set
            {
                if (SetProperty(ref startedAt, value))
                {
                    OnPropertyChanged(nameof(StartedAtText));
                }
            }
        }

        /// <summary>
        /// Gets the minimum successful ping.
        /// </summary>
        public double Min
        {
            get { return min; }
            private set { SetProperty(ref min, value); }
        }

        /// <summary>
        /// Gets the maximum successful ping.
        /// </summary>
        public double Max
        {
            get { return max; }
            private set { SetProperty(ref max, value); }
        }

        /// <summary>
        /// Gets the recent average ping.
        /// </summary>
        public double Avg
        {
            get { return avg; }
            private set
            {
                if (SetProperty(ref avg, value))
                {
                    OnPropertyChanged(nameof(AverageText));
                }
            }
        }

        /// <summary>
        /// Gets packet loss percentage.
        /// </summary>
        public double Loss
        {
            get { return loss; }
            private set
            {
                if (SetProperty(ref loss, value))
                {
                    OnPropertyChanged(nameof(LossText));
                }
            }
        }

        /// <summary>
        /// Gets the first quartile ping.
        /// </summary>
        public double Q1
        {
            get { return q1; }
            private set { SetProperty(ref q1, value); }
        }

        /// <summary>
        /// Gets the median ping.
        /// </summary>
        public double Med
        {
            get { return med; }
            private set { SetProperty(ref med, value); }
        }

        /// <summary>
        /// Gets the third quartile ping.
        /// </summary>
        public double Q3
        {
            get { return q3; }
            private set { SetProperty(ref q3, value); }
        }

        /// <summary>
        /// Gets recent samples where negative values represent packet loss.
        /// </summary>
        public double[] RecentPings
        {
            get { return recentPings; }
            private set { SetProperty(ref recentPings, value); }
        }

        /// <summary>
        /// Gets the packet archive associated with this row, or null for DNS rows.
        /// </summary>
        public PacketScan.PacketArchive PacketArchive
        {
            get { return packetArchive; }
            private set { SetProperty(ref packetArchive, value); }
        }

        /// <summary>
        /// Gets the Steam ID associated with this row.
        /// </summary>
        public ulong SteamID
        {
            get { return steamID; }
            private set
            {
                if (SetProperty(ref steamID, value))
                {
                    OnPropertyChanged(nameof(SteamIDText));
                }
            }
        }

        /// <summary>
        /// Gets whether Steam relay is used.
        /// </summary>
        public bool UsingRelay
        {
            get { return usingRelay; }
            private set
            {
                if (SetProperty(ref usingRelay, value))
                {
                    OnPropertyChanged(nameof(UsingRelayText));
                    OnPropertyChanged(nameof(UsingRelayVisibility));
                }
            }
        }

        /// <summary>
        /// Gets whether this row measures DNS reachability rather than Steam P2P.
        /// </summary>
        public bool UsingDns
        {
            get { return usingDns; }
            private set
            {
                if (SetProperty(ref usingDns, value))
                {
                    OnPropertyChanged(nameof(UsingDnsText));
                    OnPropertyChanged(nameof(UsingDnsVisibility));
                }
            }
        }

        /// <summary>
        /// Gets the optional packet scan network identifier.
        /// </summary>
        public ulong? NetIdValue
        {
            get { return netIdValue; }
            private set { SetProperty(ref netIdValue, value); }
        }

        /// <summary>
        /// Gets started time formatted for table cells.
        /// </summary>
        public string StartedAtText
        {
            get { return StartedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Gets average ping text rounded to an integer.
        /// </summary>
        public string AverageText
        {
            get { return Avg < 0 ? "-" : Math.Round(Avg).ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Gets packet loss text rounded to one decimal place.
        /// </summary>
        public string LossText
        {
            get { return Loss < 0 ? "0.0%" : Loss.ToString("0.0", CultureInfo.InvariantCulture) + "%"; }
        }

        /// <summary>
        /// Gets Steam ID text for history display.
        /// </summary>
        public string SteamIDText
        {
            get { return SteamID == 0 ? "-" : SteamID.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Gets relay marker text.
        /// </summary>
        public string UsingRelayText
        {
            get { return UsingRelay ? "relay" : string.Empty; }
        }

        /// <summary>
        /// Gets DNS marker text.
        /// </summary>
        public string UsingDnsText
        {
            get { return UsingDns ? "dns" : string.Empty; }
        }

        /// <summary>
        /// Gets visibility for relay marker content while preserving cell width.
        /// </summary>
        public Visibility UsingRelayVisibility
        {
            get { return UsingRelay ? Visibility.Visible : Visibility.Hidden; }
        }

        /// <summary>
        /// Gets visibility for DNS marker content while preserving cell width.
        /// </summary>
        public Visibility UsingDnsVisibility
        {
            get { return UsingDns ? Visibility.Visible : Visibility.Hidden; }
        }

        /// <summary>
        /// Adds one ping sample and refreshes all bindable values.
        /// </summary>
        /// <param name="value">Ping in milliseconds, or a negative value for packet loss.</param>
        public void PushPing(double value)
        {
            sourceStats.PushPing(value);
            RefreshFromStats();
        }

        /// <summary>
        /// Updates Steam metadata for this snapshot.
        /// </summary>
        /// <param name="steamId">Steam ID associated with the peer.</param>
        /// <param name="usesRelay">True when the peer currently uses Steam relay.</param>
        public void SetSteamPeerInfo(ulong steamId, bool usesRelay)
        {
            SteamID = steamId;
            UsingRelay = usesRelay;
        }

        /// <summary>
        /// Refreshes the snapshot from its statistics source.
        /// </summary>
        public void RefreshFromStats()
        {
            sourceStats.ReadValues((nameValue, startedAtValue, minValue, maxValue, avgValue, lossValue, q1Value, medValue, q3Value, recentPingsValue) =>
            {
                Name = nameValue;
                StartedAt = startedAtValue;
                Min = NormalizeMetric(minValue);
                Max = NormalizeMetric(maxValue);
                Avg = avgValue;
                Loss = lossValue;
                Q1 = NormalizeMetric(q1Value);
                Med = NormalizeMetric(medValue);
                Q3 = NormalizeMetric(q3Value);
                RecentPings = recentPingsValue ?? new double[0];
                return 0;
            });
        }

        private static double NormalizeMetric(double value)
        {
            return value < 0 ? 0 : value;
        }
    }
}
