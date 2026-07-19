using CommunityToolkit.Mvvm.ComponentModel;
using SharpPcap;
using SpsGui.Models.Services;
using SpsGui.Views;
using SpsLogic;
using SpsLogic.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OverlayPingProfileSnapshot = SpsGui.ViewModels.PingProfileSnapshot;
using OverlayWindowViewModel = SpsGui.ViewModels.OverlayWindowViewModel;

namespace SpsGui
{
    /// <summary>
    /// Test surface for attaching the real overlay window to any visible top-level window.
    /// </summary>
    public partial class OverlayWindowTest : Window, INotifyPropertyChanged
    {
        private readonly SteamAppFinder steamAppFinder = new SteamAppFinder();
        private readonly ApplicationTitleService applicationTitleService =
            new ApplicationTitleService(new VersionCheckService());
        private readonly DispatcherTimer updateTimer;
        private readonly Random random = new Random();
        private readonly List<TestOverlayRow> testRows = new List<TestOverlayRow>();
        private WindowRow selectedWindowRow;
        private string selectionStatus = "No window selected";
        private OverlayWindow overlayWindow;
        private OverlayWindowViewModel overlayViewModel;

        public OverlayWindowTest()
        {
            InitializeComponent();
            DataContext = this;

            CreateTestRows();
            RefreshWindows();

            updateTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(900)
            };
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();
        }

        public ObservableCollection<WindowRow> Windows { get; } = new ObservableCollection<WindowRow>();

        public WindowRow SelectedWindowRow
        {
            get { return selectedWindowRow; }
            set
            {
                if (selectedWindowRow == value)
                {
                    return;
                }

                selectedWindowRow = value;
                SelectionStatus = selectedWindowRow == null
                    ? "No window selected"
                    : selectedWindowRow.ProcessName + ": " + selectedWindowRow.Title;
                RaisePropertyChanged();
            }
        }

        public string SelectionStatus
        {
            get { return selectionStatus; }
            private set
            {
                if (selectionStatus == value)
                {
                    return;
                }

                selectionStatus = value;
                RaisePropertyChanged();
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
                    RaisePropertyChanged();
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
                    RaisePropertyChanged();
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
                    RaisePropertyChanged();
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
                    RaisePropertyChanged();
                }
            }
        }

        public bool ShowBoxPlot
        {
            get { return AppConfig.Instance.ShowBoxPlot; }
            set
            {
                if (AppConfig.Instance.ShowBoxPlot != value)
                {
                    AppConfig.Instance.ShowBoxPlot = value;
                    RaisePropertyChanged();
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
                    RaisePropertyChanged();
                }
            }
        }

        public double OverlayOffsetX
        {
            get { return AppConfig.Instance.OverlayOffsetX; }
            set
            {
                if (Math.Abs(AppConfig.Instance.OverlayOffsetX - value) > double.Epsilon)
                {
                    AppConfig.Instance.OverlayOffsetX = value;
                    RaisePropertyChanged();
                }
            }
        }

        public double OverlayOffsetY
        {
            get { return AppConfig.Instance.OverlayOffsetY; }
            set
            {
                if (Math.Abs(AppConfig.Instance.OverlayOffsetY - value) > double.Epsilon)
                {
                    AppConfig.Instance.OverlayOffsetY = value;
                    RaisePropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshWindows();
        }

        private void ShowButton_Click(object sender, RoutedEventArgs e)
        {
            ShowOverlay();
        }

        private void CloseOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            CloseOverlay();
        }

        private void WindowsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ShowOverlay();
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            foreach (TestOverlayRow row in testRows)
            {
                row.Push(CreateNextPing(row.Center, row.Spread, row.LossRate));
            }

            UpdateOverlayProfiles();
        }

        private void RefreshWindows()
        {
            WindowInfo selected = SelectedWindowRow == null ? null : SelectedWindowRow.Info;
            Windows.Clear();

            var rows = new List<WindowInfo>();
            steamAppFinder.EnumWindows(window =>
            {
                if (window.Handle != IntPtr.Zero && window.Handle != new System.Windows.Interop.WindowInteropHelper(this).Handle)
                {
                    rows.Add(window);
                }
            });

            foreach (WindowInfo window in rows
                .OrderBy(window => window.ProcessName)
                .ThenBy(window => window.Title))
            {
                Windows.Add(new WindowRow(window));
            }

            SelectedWindowRow = Windows.FirstOrDefault(row =>
                selected != null &&
                row.Info.Handle == selected.Handle);
        }

        private void ShowOverlay()
        {
            if (SelectedWindowRow == null)
            {
                SelectionStatus = "Select a target window first";
                return;
            }

            CloseOverlay();

            overlayViewModel = new OverlayWindowViewModel(SelectedWindowRow.Info, applicationTitleService);
            overlayWindow = new OverlayWindow
            {
                DataContext = overlayViewModel
            };
            overlayWindow.Closed += OverlayWindow_Closed;
            overlayWindow.Show();
            UpdateOverlayProfiles();
        }

        private void CloseOverlay()
        {
            if (overlayWindow != null)
            {
                overlayWindow.Closed -= OverlayWindow_Closed;
                overlayWindow.Close();
                overlayWindow = null;
            }

            if (overlayViewModel != null)
            {
                overlayViewModel.Dispose();
                overlayViewModel = null;
            }
        }

        private void OverlayWindow_Closed(object sender, EventArgs e)
        {
            if (overlayViewModel != null)
            {
                overlayViewModel.Dispose();
                overlayViewModel = null;
            }

            overlayWindow = null;
        }

        private void UpdateOverlayProfiles()
        {
            if (overlayViewModel == null)
            {
                return;
            }

            double limit = testRows
                .Select(row => row.Snapshot.Max)
                .DefaultIfEmpty(0.0)
                .Max();

            overlayViewModel.UpdateProfiles(testRows
                .Select(row => row.Snapshot.CreateOverlaySnapshot(limit))
                .ToArray());
        }

        private void CreateTestRows()
        {
            testRows.Add(TestOverlayRow.CreateSteam(
                "kate",
                76561198000000001,
                28,
                7,
                0.02,
                random));
            testRows.Add(TestOverlayRow.CreateSteam(
                "A",
                76561198000000002,
                74,
                24,
                0.08,
                random));
            testRows.Add(TestOverlayRow.CreateDns(
                "DNS 8.8.8.8",
                12,
                4,
                0.01,
                random));
        }

        private double CreateNextPing(int center, int spread, double lossRate)
        {
            if (random.NextDouble() < lossRate)
            {
                return -1;
            }

            int jitter = random.Next(-spread, spread + 1);
            int spike = random.NextDouble() < 0.10 ? random.Next(spread, spread * 4 + 1) : 0;
            return Math.Max(1, center + jitter + spike);
        }

        protected override void OnClosed(EventArgs e)
        {
            updateTimer.Stop();
            updateTimer.Tick -= UpdateTimer_Tick;
            CloseOverlay();
            base.OnClosed(e);
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal sealed class TestOverlayRow
    {
        private readonly ConnectionStats stats;

        private TestOverlayRow(ConnectionStats stats, OverlayPingProfileSnapshot snapshot, int center, int spread, double lossRate)
        {
            this.stats = stats;
            Snapshot = snapshot;
            Center = center;
            Spread = spread;
            LossRate = lossRate;
        }

        public OverlayPingProfileSnapshot Snapshot { get; private set; }

        public int Center { get; private set; }

        public int Spread { get; private set; }

        public double LossRate { get; private set; }

        public static TestOverlayRow CreateSteam(
            string name,
            ulong steamId,
            int center,
            int spread,
            double lossRate,
            Random random)
        {
            var history = new TestPlayerPingHistory(name, steamId);
            PushInitialSamples(history.Stats, center, spread, lossRate, random);
            OverlayPingProfileSnapshot snapshot = OverlayPingProfileSnapshot.FromPacketScan(
                "Active",
                steamId,
                history);
            return new TestOverlayRow(history.Stats, snapshot, center, spread, lossRate);
        }

        public static TestOverlayRow CreateDns(
            string name,
            int center,
            int spread,
            double lossRate,
            Random random)
        {
            var stats = new ConnectionStats(10, name, 0);
            PushInitialSamples(stats, center, spread, lossRate, random);
            OverlayPingProfileSnapshot snapshot = OverlayPingProfileSnapshot.CreateDns(stats, new double[0]);
            return new TestOverlayRow(stats, snapshot, center, spread, lossRate);
        }

        public void Push(double ping)
        {
            stats.PushPing(ping);
            Snapshot.RefreshFromStats();
        }

        private static void PushInitialSamples(ConnectionStats stats, int center, int spread, double lossRate, Random random)
        {
            for (int i = 0; i < 10; i++)
            {
                if (random.NextDouble() < lossRate)
                {
                    stats.PushPing(-1);
                    continue;
                }

                int jitter = random.Next(-spread, spread + 1);
                stats.PushPing(Math.Max(1, center + jitter));
            }
        }
    }

    internal sealed class TestPlayerPingHistory : BasePlayerPingHistory
    {
        private readonly TestPacketArchive archive = new TestPacketArchive();
        private readonly ConnectionStats stats;

        public TestPlayerPingHistory(string name, ulong steamId)
        {
            stats = new ConnectionStats(10, name, steamId);
        }

        public override BasePacketArchive Archive
        {
            get { return archive; }
        }

        public override ConnectionStats Stats
        {
            get { return stats; }
        }

        public override void ReportSend(ClassicStunTransactionId id, PosixTimeval time)
        {
        }

        public override void ReportReceive(ClassicStunTransactionId id, PosixTimeval time)
        {
        }

        public override void Update()
        {
        }

        public override void Dispose()
        {
            archive.Dispose();
        }
    }

    internal sealed class TestPacketArchive : BasePacketArchive
    {
        public override void AddCapture(object capture)
        {
        }

        public override System.Threading.Tasks.Task SaveCaptureAsync(string fileName)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public override void Dispose()
        {
        }
    }
}
