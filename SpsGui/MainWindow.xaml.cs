using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SpsLogic;

namespace SpsGui
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer uiHeartbeatTimer;
        private readonly DispatcherTimer resultPollTimer;
        private readonly Stopwatch uiStopwatch;
        private readonly List<double> recentPings;
        private DnsPing dnsPing;
        private TimeSpan lastHeartbeat;
        private double maxUiDelayMilliseconds;
        private int activityValue;
        private bool activityIncreasing = true;

        public MainWindow()
        {
            InitializeComponent();

            uiStopwatch = Stopwatch.StartNew();
            lastHeartbeat = uiStopwatch.Elapsed;
            recentPings = new List<double>();

            uiHeartbeatTimer = new DispatcherTimer(DispatcherPriority.Render);
            uiHeartbeatTimer.Interval = TimeSpan.FromMilliseconds(100);
            uiHeartbeatTimer.Tick += UiHeartbeatTimer_Tick;
            uiHeartbeatTimer.Start();

            resultPollTimer = new DispatcherTimer(DispatcherPriority.Background);
            resultPollTimer.Interval = TimeSpan.FromMilliseconds(500);
            resultPollTimer.Tick += ResultPollTimer_Tick;
            resultPollTimer.Start();

            ThreadTextBlock.Text = $"UI thread: {Thread.CurrentThread.ManagedThreadId}";
            AppendLog("Window initialized.");
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (dnsPing != null)
                {
                    AppendLog("DnsPing is already running.");
                    return;
                }

                var serviceItem = DnsServiceComboBox.SelectedItem as ComboBoxItem;
                string serviceAddress = serviceItem?.Tag as string;
                string queryName = GetQueryName();

                if (!IPAddress.TryParse(serviceAddress, out var address))
                {
                    AppendLog($"Invalid DNS service address: {serviceAddress}");
                    return;
                }

                if (!int.TryParse(DelayTextBox.Text, out int delayMs) ||
                    !int.TryParse(PatienceTextBox.Text, out int patienceMs))
                {
                    AppendLog("Interval and timeout must be integer milliseconds.");
                    return;
                }

                recentPings.Clear();
                LatestPingTextBlock.Text = "-";
                AveragePingTextBlock.Text = "-";
                CountTextBlock.Text = "0 / 0";

                dnsPing = new DnsPing(
                    new IPEndPoint(address, 53),
                    queryName,
                    delayMs,
                    patienceMs);

                dnsPing.Start();

                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                StateTextBlock.Text = $"Running: {serviceAddress}, {queryName}";
                AppendLog($"Started DnsPing: server={serviceAddress}, query={queryName}, delay={delayMs}ms, timeout={patienceMs}ms");
            }
            catch (Exception ex)
            {
                AppendLog($"Start failed: {ex.GetType().Name}: {ex.Message}");
                SafeDisposeDnsPing();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopDnsPing("Stopped by user.");
        }

        private void UiStressButton_Click(object sender, RoutedEventArgs e)
        {
            var started = Stopwatch.GetTimestamp();
            long checksum = 0;

            while ((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency < 500.0)
            {
                checksum++;
            }

            AppendLog($"UI stress completed. checksum={checksum}");
        }

        private void UiHeartbeatTimer_Tick(object sender, EventArgs e)
        {
            var now = uiStopwatch.Elapsed;
            double intervalMs = (now - lastHeartbeat).TotalMilliseconds;
            lastHeartbeat = now;

            double delayMs = Math.Max(0.0, intervalMs - uiHeartbeatTimer.Interval.TotalMilliseconds);
            if (delayMs > maxUiDelayMilliseconds)
            {
                maxUiDelayMilliseconds = delayMs;
            }

            DispatcherLagTextBlock.Text = $"Dispatcher lag: {delayMs:0.0} ms";
            MaxUiDelayTextBlock.Text = $"{maxUiDelayMilliseconds:0.0} ms";
            UiHeartbeatText.Text = $"UI alive {DateTime.Now:HH:mm:ss}";
            UiHeartbeatDot.Opacity = UiHeartbeatDot.Opacity > 0.5 ? 0.35 : 1.0;

            if (activityIncreasing)
            {
                activityValue += 5;
                if (activityValue >= 100)
                {
                    activityValue = 100;
                    activityIncreasing = false;
                }
            }
            else
            {
                activityValue -= 5;
                if (activityValue <= 0)
                {
                    activityValue = 0;
                    activityIncreasing = true;
                }
            }

            ActivityProgressBar.Value = activityValue;
        }

        private void ResultPollTimer_Tick(object sender, EventArgs e)
        {
            if (dnsPing == null)
            {
                return;
            }

            try
            {
                var pings = dnsPing.ExtractQueuedPings();
                if (pings.Length > 0)
                {
                    var lostCount = pings.Count(ping => ping < 0);
                    var validPings = pings.Where(ping => ping >= 0).ToArray();

                    recentPings.AddRange(validPings);
                    if (recentPings.Count > 60)
                    {
                        recentPings.RemoveRange(0, recentPings.Count - 60);
                    }

                    double latest = pings[pings.Length - 1];
                    LatestPingTextBlock.Text = latest < 0 ? "Loss" : $"{latest:0} ms";
                    AveragePingTextBlock.Text = recentPings.Count == 0 ? "-" : $"{recentPings.Average():0.0} ms";

                    if (validPings.Length > 0)
                    {
                        AppendLog($"Received {validPings.Length} ping(s): {string.Join(", ", validPings)} ms");
                    }

                    if (lostCount > 0)
                    {
                        AppendLog($"Detected packet loss: {lostCount} packet(s)");
                    }
                }

                var counts = dnsPing.GetSuccessFailureCounts();
                CountTextBlock.Text = $"{counts.Sucess} / {counts.Failure}";
            }
            catch (Exception ex)
            {
                AppendLog($"Poll failed: {ex.GetType().Name}: {ex.Message}");
                StopDnsPing("Stopped because polling failed.");
            }
        }

        private string GetQueryName()
        {
            if (QueryNameComboBox.Text != null)
            {
                return QueryNameComboBox.Text.Trim();
            }

            var item = QueryNameComboBox.SelectedItem as ComboBoxItem;
            return item?.Content?.ToString()?.Trim() ?? "example.com";
        }

        private void StopDnsPing(string reason)
        {
            SafeDisposeDnsPing();

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StateTextBlock.Text = "Stopped";
            AppendLog(reason);
        }

        private void SafeDisposeDnsPing()
        {
            var old = dnsPing;
            dnsPing = null;

            if (old == null)
            {
                return;
            }

            try
            {
                old.Dispose();
            }
            catch (Exception ex)
            {
                AppendLog($"Dispose failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void AppendLog(string message)
        {
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] [UI:{Thread.CurrentThread.ManagedThreadId}] {message}{Environment.NewLine}");
            LogTextBox.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            resultPollTimer.Stop();
            uiHeartbeatTimer.Stop();
            SafeDisposeDnsPing();
            base.OnClosed(e);
        }
    }
}
