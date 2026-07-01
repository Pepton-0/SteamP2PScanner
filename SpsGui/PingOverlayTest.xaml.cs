using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SpsLogic;
using Steamworks;

namespace SpsGui
{
    public partial class PingOverlayTest : Window
    {
        private readonly DispatcherTimer updateTimer;
        private readonly Random random = new Random();

        public ObservableCollection<PingProfileSnapshot> Profiles { get; } = new ObservableCollection<PingProfileSnapshot>();

        public PingOverlayTest()
        {
            InitializeComponent();
            DataContext = this;

            Profiles.Add(PingProfileSnapshot.CreateSteam(new ConnectionStats(10, "Kent2"), CreateInitialStats(36, 8, 0.05)));
            Profiles.Add(PingProfileSnapshot.CreateSteam(new ConnectionStats(10, "Player Alpha"), CreateInitialStats(72, 22, 0.12)));
            Profiles.Add(PingProfileSnapshot.CreateDns(new ConnectionStats(10, "Cloudflare DNS"), CreateInitialStats(18, 4, 0.02)));
            Profiles.Add(PingProfileSnapshot.CreateDns(new ConnectionStats(10, "Google DNS"), CreateInitialStats(28, 6, 0.03)));

            updateTimer = new DispatcherTimer(DispatcherPriority.Background);
            updateTimer.Interval = TimeSpan.FromMilliseconds(900);
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();
        }

        private IList<int> CreateInitialStats(int center, int spread, double lossRate)
        {
            var values = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                values.Add(CreateNextPing(center, spread, lossRate));
            }

            return values;
        }

        private int CreateNextPing(int center, int spread, double lossRate)
        {
            if (random.NextDouble() < lossRate)
            {
                return -1;
            }

            int jitter = random.Next(-spread, spread + 1);
            int spike = random.NextDouble() < 0.12 ? random.Next(spread, spread * 4 + 1) : 0;
            return Math.Max(1, center + jitter + spike);
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            for (int i = 0; i < Profiles.Count; i++)
            {
                PingProfileSnapshot profile = Profiles[i];

                int center;
                int spread;
                double lossRate;

                if (profile.Name.Contains("Cloudflare"))
                {
                    center = 18;
                    spread = 4;
                    lossRate = 0.02;
                }
                else if (profile.Name.Contains("Google"))
                {
                    center = 28;
                    spread = 6;
                    lossRate = 0.03;
                }
                else if (profile.Name.Contains("Alpha"))
                {
                    center = 72;
                    spread = 22;
                    lossRate = 0.12;
                }
                else
                {
                    center = 36;
                    spread = 8;
                    lossRate = 0.05;
                }

                profile.PushPing(CreateNextPing(center, spread, lossRate));
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            updateTimer.Stop();
            base.OnClosed(e);
        }
    }
    
    public sealed class PingProfileSnapshot : ObservableObject
    {
        private readonly ConnectionStats sourceStats;

        private PingProfileSnapshot(ConnectionStats stats)
        {
            sourceStats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        public static PingProfileSnapshot CreateSteam(
            ConnectionStats stats,
            IEnumerable<int> initialPings,
            PacketScan.PacketArchive packetArchive = null,
            CSteamID? steamId = null,
            bool usingRelay = false)
        {
            var snapshot = new PingProfileSnapshot(stats);
            snapshot.PacketArchive = packetArchive;
            snapshot.SteamID = steamId ?? new CSteamID(0);
            snapshot.UsingRelay = usingRelay;
            snapshot.UsingDns = false;
            foreach (int ping in initialPings)
            {
                stats.PushPing(ping);
            }

            snapshot.RefreshFromStats();
            return snapshot;
        }

        public static PingProfileSnapshot CreateDns(ConnectionStats stats, IEnumerable<int> initialPings)
        {
            var snapshot = new PingProfileSnapshot(stats);
            snapshot.PacketArchive = null;
            snapshot.SteamID = new CSteamID(0);
            snapshot.UsingRelay = false;
            snapshot.UsingDns = true;
            foreach (int ping in initialPings)
            {
                stats.PushPing(ping);
            }

            snapshot.RefreshFromStats();
            return snapshot;
        }

        public static PingProfileSnapshot FromPacketScan(
            string state,
            ulong? netId,
            PacketScan.PlayerPingHistory history,
            CSteamID? steamId = null,
            bool usingRelay = false)
        {
            if (history == null)
            {
                throw new ArgumentNullException(nameof(history));
            }

            var snapshot = new PingProfileSnapshot(history.Stats);
            snapshot.State = state;
            snapshot.NetIdValue = netId;
            snapshot.PacketArchive = history.Archive;
            snapshot.SteamID = steamId ?? new CSteamID(0);
            snapshot.UsingRelay = usingRelay;
            snapshot.UsingDns = false;
            snapshot.RefreshFromStats();
            return snapshot;
        }

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
        private CSteamID steamID = new CSteamID(0);
        private bool usingRelay;
        private bool usingDns;

        public string State
        {
            get => state;
            private set => SetProperty(ref state, value);
        }

        public string Name
        {
            get => name;
            private set => SetProperty(ref name, value);
        }

        public DateTime StartedAt
        {
            get => startedAt;
            private set => SetProperty(ref startedAt, value);
        }

        public double Min
        {
            get => min;
            private set => SetProperty(ref min, value);
        }

        public double Max
        {
            get => max;
            private set => SetProperty(ref max, value);
        }

        public double Avg
        {
            get => avg;
            private set
            {
                if (SetProperty(ref avg, value))
                {
                    OnPropertyChanged(nameof(AverageText));
                }
            }
        }

        public double Loss
        {
            get => loss;
            private set
            {
                if (SetProperty(ref loss, value))
                {
                    OnPropertyChanged(nameof(LossText));
                }
            }
        }

        public double Q1
        {
            get => q1;
            private set => SetProperty(ref q1, value);
        }

        public double Med
        {
            get => med;
            private set => SetProperty(ref med, value);
        }

        public double Q3
        {
            get => q3;
            private set => SetProperty(ref q3, value);
        }

        public double[] RecentPings
        {
            get => recentPings;
            private set => SetProperty(ref recentPings, value);
        }

        public PacketScan.PacketArchive PacketArchive
        {
            get => packetArchive;
            private set => SetProperty(ref packetArchive, value);
        }

        public CSteamID SteamID
        {
            get => steamID;
            private set => SetProperty(ref steamID, value);
        }

        public bool UsingRelay
        {
            get => usingRelay;
            private set
            {
                if (SetProperty(ref usingRelay, value))
                {
                    OnPropertyChanged(nameof(UsingRelayText));
                }
            }
        }

        public bool UsingDns
        {
            get => usingDns;
            private set
            {
                if (SetProperty(ref usingDns, value))
                {
                    OnPropertyChanged(nameof(SteamInfoVisibility));
                }
            }
        }

        public ulong? NetIdValue
        {
            get => netIdValue;
            private set
            {
                if (SetProperty(ref netIdValue, value))
                {
                    OnPropertyChanged(nameof(NetId));
                }
            }
        }

        public string NetId => NetIdValue.HasValue
            ? NetIdValue.Value.ToString(CultureInfo.InvariantCulture)
            : "-";

        public string AverageText => Avg < 0
            ? "-"
            : $"{Math.Round(Avg).ToString(CultureInfo.InvariantCulture)}ms";

        public string LossText => Loss < 0
            ? "0%"
            : $"{Math.Round(Loss).ToString(CultureInfo.InvariantCulture)}%";

        public string MinText => FormatPing(Min);
        public string Q1Text => FormatPing(Q1);
        public string MedText => FormatPing(Med);
        public string Q3Text => FormatPing(Q3);
        public string MaxText => FormatPing(Max);
        public string Latest => FormatLatest(RecentPings);
        public string Recent => FormatRecent(RecentPings);
        public string SteamIDText => SteamID.m_SteamID.ToString(CultureInfo.InvariantCulture);
        public string UsingRelayText => UsingRelay ? "relay" : "direct";
        public Visibility SteamInfoVisibility => UsingDns ? Visibility.Collapsed : Visibility.Visible;

        public void PushPing(int value)
        {
            sourceStats.PushPing(value);
            RefreshFromStats();
        }

        public void RefreshFromStats()
        {
            sourceStats.ReadValues((nameValue, startedAtValue, minValue, maxValue, avgValue, lossValue, q1Value, medValue, q3Value, recentPingsValue) =>
            {
                Name = nameValue;
                StartedAt = startedAtValue;
                Min = minValue;
                Max = maxValue;
                Avg = avgValue;
                Loss = lossValue;
                Q1 = q1Value;
                Med = medValue;
                Q3 = q3Value;
                RecentPings = recentPingsValue ?? new double[0];

                OnPropertyChanged(nameof(MinText));
                OnPropertyChanged(nameof(Q1Text));
                OnPropertyChanged(nameof(MedText));
                OnPropertyChanged(nameof(Q3Text));
                OnPropertyChanged(nameof(MaxText));
                OnPropertyChanged(nameof(Latest));
                OnPropertyChanged(nameof(Recent));
                OnPropertyChanged(nameof(SteamIDText));
                OnPropertyChanged(nameof(UsingRelayText));
                return 0;
            });
        }

        private static string FormatPing(double value)
        {
            return value < 0 ? "-" : value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string FormatRecent(IEnumerable<double> values)
        {
            if (values == null)
            {
                return "-";
            }

            return string.Join(", ", values.Select(value => value < 0 ? "loss" : value.ToString("0.0", CultureInfo.InvariantCulture)));
        }

        private static string FormatLatest(IEnumerable<double> values)
        {
            if (values == null)
            {
                return "-";
            }

            double? latest = null;
            foreach (double value in values)
            {
                latest = value;
            }

            if (!latest.HasValue)
            {
                return "-";
            }

            return latest.Value < 0 ? "loss" : latest.Value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }

    public sealed class BoxPlotView : FrameworkElement
    {
        public BoxPlotView()
        {
            DataContextChanged += BoxPlotView_DataContextChanged;
        }

        private void BoxPlotView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyPropertyChanged oldValue)
            {
                oldValue.PropertyChanged -= DataContext_PropertyChanged;
            }

            if (e.NewValue is INotifyPropertyChanged newValue)
            {
                newValue.PropertyChanged += DataContext_PropertyChanged;
            }

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (!(DataContext is PingProfileSnapshot stats))
            {
                return;
            }

            double width = ActualWidth;
            double height = ActualHeight;
            double plotLeft = 0;
            double plotRight = Math.Max(plotLeft + 1, width - 2);
            double centerY = height * 0.66;
            double boxTop = Math.Max(20, centerY - 7);
            double boxBottom = Math.Min(height - 2, centerY + 7);

            var axisPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 220, 230, 240)), 1);
            var boxPen = new Pen(new SolidColorBrush(Color.FromRgb(226, 232, 240)), 1.2);
            var medianPen = new Pen(new SolidColorBrush(Color.FromRgb(250, 204, 21)), 2);
            var averagePen = new Pen(new SolidColorBrush(Color.FromRgb(34, 197, 94)), 2);
            var fillBrush = new SolidColorBrush(Color.FromArgb(80, 125, 211, 252));

            double max = Math.Max(1, stats.Max);
            double xZero = ToX(0, max, plotLeft, plotRight);
            double xMin = ToX(stats.Min, max, plotLeft, plotRight);
            double xQ1 = ToX(stats.Q1, max, plotLeft, plotRight);
            double xMedian = ToX(stats.Med, max, plotLeft, plotRight);
            double xQ3 = ToX(stats.Q3, max, plotLeft, plotRight);
            double xAverage = ToX(stats.Avg, max, plotLeft, plotRight);
            double xMax = ToX(stats.Max, max, plotLeft, plotRight);

            drawingContext.DrawLine(axisPen, new Point(xZero, centerY), new Point(plotRight, centerY));
            drawingContext.DrawLine(boxPen, new Point(xMin, centerY), new Point(xQ1, centerY));
            drawingContext.DrawLine(boxPen, new Point(xQ3, centerY), new Point(xMax, centerY));
            drawingContext.DrawLine(boxPen, new Point(xMin, centerY - 6), new Point(xMin, centerY + 6));
            drawingContext.DrawLine(boxPen, new Point(xMax, centerY - 6), new Point(xMax, centerY + 6));
            drawingContext.DrawRectangle(fillBrush, boxPen, new Rect(new Point(xQ1, boxTop), new Point(xQ3, boxBottom)));
            drawingContext.DrawLine(averagePen, new Point(xAverage, boxTop - 1), new Point(xAverage, boxBottom + 1));
            drawingContext.DrawLine(medianPen, new Point(xMedian, boxTop - 1), new Point(xMedian, boxBottom + 1));

            Brush medianBrush = new SolidColorBrush(Color.FromRgb(250, 204, 21));
            DrawText(drawingContext, "0", xZero, 0, 14, Brushes.White);

            double labelX = xZero + 22;
            labelX += DrawText(drawingContext, $"Min:{stats.Min:0}/Q1:{stats.Q1:0}/", labelX, 0, 14, Brushes.White);
            labelX += DrawText(drawingContext, $"Mid:{stats.Med:0}", labelX, 0, 14, medianBrush);
            DrawText(drawingContext, $"/Q3:{stats.Q3:0}", labelX, 0, 14, Brushes.White);

            DrawText(drawingContext, stats.Max.ToString("0", CultureInfo.InvariantCulture), Math.Max(0, xMax - 34), 0, 14, Brushes.White);
        }

        private static double ToX(double value, double max, double left, double right)
        {
            if (max <= 0)
            {
                return left;
            }

            return left + (right - left) * Math.Max(0.0, Math.Min(1.0, value / (double)max));
        }

        private void DataContext_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            InvalidateVisual();
        }

        private double DrawText(DrawingContext drawingContext, string text, double x, double y, double fontSize, Brush brush)
        {
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                fontSize,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            drawingContext.DrawText(formattedText, new Point(x, y));
            return formattedText.WidthIncludingTrailingWhitespace;
        }
    }

    public sealed class RecentStatsBarsView : FrameworkElement
    {
        public RecentStatsBarsView()
        {
            DataContextChanged += RecentStatsBarsView_DataContextChanged;
        }

        private void RecentStatsBarsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyPropertyChanged oldValue)
            {
                oldValue.PropertyChanged -= DataContext_PropertyChanged;
            }

            if (e.NewValue is INotifyPropertyChanged stats)
            {
                stats.PropertyChanged += DataContext_PropertyChanged;
            }

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (!(DataContext is PingProfileSnapshot stats))
            {
                return;
            }

            double[] values = stats.RecentPings;
            double[] validValues = values.Where(value => value >= 0).ToArray();
            int min = 0;
            double max = validValues.Length == 0 ? 1 : Math.Max(1, validValues.Max());

            double width = ActualWidth;
            double height = ActualHeight;
            double labelWidth = 42;
            double plotLeft = 0;
            double plotRight = Math.Max(plotLeft + 1, width - labelWidth);
            double plotTop = 4;
            double plotBottom = height - 10;
            double plotHeight = Math.Max(1, plotBottom - plotTop);
            double barGap = 1;
            double barWidth = 4;
            plotRight = Math.Min(plotRight, plotLeft + barWidth * 10 + barGap * 9);

            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);
            drawingContext.DrawLine(gridPen, new Point(plotLeft, plotTop), new Point(plotRight, plotTop));
            drawingContext.DrawLine(gridPen, new Point(plotLeft, plotBottom), new Point(plotRight, plotBottom));

            DrawText(drawingContext, max.ToString(CultureInfo.InvariantCulture), plotRight + 5, plotTop - 3, 14, Brushes.White);
            DrawText(drawingContext, min.ToString(CultureInfo.InvariantCulture), plotRight + 5, plotBottom - 13, 14, Brushes.White);

            for (int i = 0; i < 10; i++)
            {
                double value = i < values.Length ? values[i] : 0;
                double x = plotLeft + i * (barWidth + barGap);
                bool isLoss = value < 0;
                double normalized = isLoss ? 1.0 : Math.Max(0.02, Math.Min(1.0, value / (double)max));
                double barHeight = plotHeight * normalized;
                double y = plotBottom - barHeight;
                Brush brush = isLoss
                    ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                    : new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));

                drawingContext.DrawRectangle(brush, null, new Rect(x, y, barWidth, barHeight));
            }
        }

        private void DataContext_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            InvalidateVisual();
        }

        private void DrawText(DrawingContext drawingContext, string text, double x, double y, double fontSize, Brush brush)
        {
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                fontSize,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            drawingContext.DrawText(formattedText, new Point(x, y));
        }
    }
}
