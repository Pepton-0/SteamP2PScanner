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

            IsArchiveUsable = packetScan.IsArchiveUsable();
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
        /// Gets whether packet archives can be exported by the selected scanner.
        /// </summary>
        public bool IsArchiveUsable { get; }

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

        public double OverlayOffsetX
        {
            get { return AppConfig.Instance.OverlayOffsetX; }
            set
            {
                if (AppConfig.Instance.OverlayOffsetX != value)
                {
                    AppConfig.Instance.OverlayOffsetX = value;
                    OnPropertyChanged(nameof(OverlayOffsetX));
                }
            }
        }

        public double OverlayOffsetY
        {
            get { return AppConfig.Instance.OverlayOffsetY; }
            set
            {
                if (AppConfig.Instance.OverlayOffsetY != value)
                {
                    AppConfig.Instance.OverlayOffsetY = value;
                    OnPropertyChanged(nameof(OverlayOffsetY));
                }
            }
        }

        public bool IgnoreLatest
        {
            get { return AppConfig.Instance.IgnoreLatest; }
            set
            {
                if (AppConfig.Instance.IgnoreLatest != value)
                {
                    AppConfig.Instance.IgnoreLatest = value;
                    OnPropertyChanged(nameof(IgnoreLatest));
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
            double overlayBoxPlotLimit = 0.0;

            packetScan.ForEachActiveHistory((state, netId, history) =>
            {
                string key = CreateSnapshotKey(netId, history);
                seenKeys.Add(key);
                overlayBoxPlotLimit = Math.Max(overlayBoxPlotLimit, NormalizeLimitSource(history.Stats.Max));

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
            overlayService.UpdateProfiles(CreateOverlayProfiles(CurrentProfiles, overlayBoxPlotLimit));
            previousActiveCount = next.Count(snapshot => !snapshot.UsingDns);
        }

        private void RefreshHistoryProfiles()
        {
            foreach (BasePlayerPingHistory history in packetScan.TakeUnseenOldHistories())
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
                    new ConnectionStats(10, "DNS " + AppConfig.Instance.DnsIp, 0),
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
            if (!IsArchiveUsable || snapshot == null || snapshot.PacketArchive == null)
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
            return IsArchiveUsable && snapshot != null && snapshot.PacketArchive != null;
        }

        private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
        {
            collection.Clear();
            foreach (T item in items)
            {
                collection.Add(item);
            }
        }

        private static string CreateSnapshotKey(ulong? netId, BasePlayerPingHistory history)
        {
            if (netId.HasValue)
            {
                return netId.Value.ToString(CultureInfo.InvariantCulture);
            }

            return history.Stats.Name;
        }

        private static IEnumerable<PingProfileSnapshot> CreateOverlayProfiles(
            IEnumerable<PingProfileSnapshot> snapshots,
            double boxPlotLimit)
        {
            return snapshots.Select(snapshot => snapshot.CreateOverlaySnapshot(boxPlotLimit)).ToArray();
        }

        private static double NormalizeLimitSource(double value)
        {
            return value < 0 ? 0.0 : value;
        }

        private static string CreateArchiveFileName(PingProfileSnapshot snapshot)
        {
            string safeName = string.Concat((snapshot.Name ?? "peer")
                .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            return safeName + "_" + snapshot.StartedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".pcap";
        }
    }
}
