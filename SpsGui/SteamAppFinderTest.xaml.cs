using SpsLogic;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SpsGui
{
    public partial class SteamAppFinderTest : Window
    {
        private readonly DispatcherTimer refreshTimer;
        private SteamAppFinder finder = new SteamAppFinder();
        private IntPtr? selectedCandidateWindowHandle;
        private SteamAppFinderRow selectedCandidateRow;
        private bool isRefreshingCandidates;

        public SteamAppFinderTest()
        {
            InitializeComponent();
            DataContext = this;

            refreshTimer = new DispatcherTimer(DispatcherPriority.Background);
            refreshTimer.Interval = TimeSpan.FromSeconds(2);
            refreshTimer.Tick += RefreshTimer_Tick;
            refreshTimer.Start();

            RefreshDetections("startup");
        }

        public ObservableCollection<SteamAppFinderRow> CandidateRows { get; } =
            new ObservableCollection<SteamAppFinderRow>();

        public ObservableCollection<SteamAppFinderRow> HiddenRows { get; } =
            new ObservableCollection<SteamAppFinderRow>();

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (AutoRefreshCheckBox.IsChecked == true)
            {
                RefreshDetections("timer");
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshDetections("manual");
        }

        private void ManualSelectButton_Click(object sender, RoutedEventArgs e)
        {
            bool wasAutoRefreshEnabled = AutoRefreshCheckBox.IsChecked == true;
            AutoRefreshCheckBox.IsChecked = false;
            refreshTimer.Stop();

            try
            {
                SteamAppFinder.WindowInfo[] windows = GetVisibleWindows();
                var windowDialog = new WindowSelectDialogTest(windows)
                {
                    Owner = this
                };

                if (windowDialog.ShowDialog() != true || windowDialog.SelectedWindow == null)
                {
                    AppendLog("Manual selection cancelled.");
                    return;
                }

                SteamAppFinder.WindowInfo selectedWindow = windowDialog.SelectedWindow;
                string initialSteamAppId = FindDetectedSteamAppId(selectedWindow);

                var appIdDialog = new SteamAppIdSelectDialogTest(selectedWindow, initialSteamAppId)
                {
                    Owner = this
                };

                if (appIdDialog.ShowDialog() != true)
                {
                    AppendLog($"SteamAppId selection cancelled: {FormatWindowName(selectedWindow)}");
                    return;
                }

                AppendLog(
                    $"Manual selected: hwnd=0x{selectedWindow.Handle.ToInt64():X}, " +
                    $"pid={selectedWindow.ProcessId}, steamAppId={appIdDialog.SteamAppId}, " +
                    $"name=\"{FormatWindowName(selectedWindow)}\", path=\"{FormatText(selectedWindow.ProcessPath)}\"");
                RegisterDetectedGame(selectedWindow.ProcessPath, appIdDialog.SteamAppId, "Manual");
            }
            catch (Exception ex)
            {
                AppendLog($"Manual selection failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (wasAutoRefreshEnabled)
                {
                    AutoRefreshCheckBox.IsChecked = true;
                    refreshTimer.Start();
                    RefreshDetections("manual-complete");
                }
                else
                {
                    refreshTimer.Start();
                }
            }
        }

        private void ResetFinderButton_Click(object sender, RoutedEventArgs e)
        {
            finder = new SteamAppFinder();
            selectedCandidateWindowHandle = null;
            selectedCandidateRow = null;
            UpdateMonitorButton();
            AppendLog("SteamAppFinder instance reset.");
            RefreshDetections("reset");
        }

        private void RefreshDetections(string source)
        {
            try
            {
                SteamAppFinder.SteamAppInfo[] apps = finder.GetSteamProcesses();

                isRefreshingCandidates = true;
                try
                {
                    CandidateRows.Clear();
                    foreach (SteamAppFinder.SteamAppInfo app in apps
                        .Where(app => app.IsVisible)
                        .OrderBy(app => FormatWindowName(app.Info)))
                    {
                        CandidateRows.Add(new SteamAppFinderRow(app));
                    }

                    RestoreCandidateSelection();
                }
                finally
                {
                    isRefreshingCandidates = false;
                }

                HiddenRows.Clear();
                foreach (SteamAppFinder.SteamAppInfo app in apps
                    .Where(app => !app.IsVisible)
                    .OrderBy(app => FormatWindowName(app.Info)))
                {
                    HiddenRows.Add(new SteamAppFinderRow(app));
                }

                SummaryTextBlock.Text =
                    $"detected={apps.Length}, candidates={CandidateRows.Count}, hidden={HiddenRows.Count}, selectedHwnd={FormatSelectedWindowHandle()}, " +
                    $"last={DateTime.Now:HH:mm:ss}";

                Logger.DebugLog($"SteamAppFinder refresh: source={source}, {SummaryTextBlock.Text}");
            }
            catch (Exception ex)
            {
                AppendLog($"Refresh failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void CandidateListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isRefreshingCandidates)
            {
                return;
            }

            selectedCandidateRow = CandidateListView.SelectedItem as SteamAppFinderRow;
            selectedCandidateWindowHandle = selectedCandidateRow == null
                ? (IntPtr?)null
                : selectedCandidateRow.App.Info.Handle;

            UpdateMonitorButton();
        }

        private void MonitorButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedCandidateRow == null)
            {
                return;
            }

            SteamAppFinder.SteamAppInfo app = selectedCandidateRow.App;
            AppendLog(
                $"Auto monitor selected: hwnd=0x{app.Info.Handle.ToInt64():X}, " +
                $"pid={app.Info.ProcessId}, steamAppId={app.SteamAppId}, " +
                $"name=\"{FormatWindowName(app.Info)}\", path=\"{FormatText(app.Info.ProcessPath)}\"");
            RegisterDetectedGame(app.Info.ProcessPath, app.SteamAppId, "Auto");
        }

        private void RestoreCandidateSelection()
        {
            SteamAppFinderRow rowToSelect = null;

            if (CandidateRows.Count == 1)
            {
                rowToSelect = CandidateRows[0];
            }
            else if (selectedCandidateWindowHandle.HasValue)
            {
                rowToSelect = CandidateRows.FirstOrDefault(row => row.App.Info.Handle == selectedCandidateWindowHandle.Value);
            }

            CandidateListView.SelectedItem = rowToSelect;
            selectedCandidateRow = rowToSelect;

            if (rowToSelect != null)
            {
                selectedCandidateWindowHandle = rowToSelect.App.Info.Handle;
            }

            UpdateMonitorButton();
        }

        private void UpdateMonitorButton()
        {
            if (MonitorButton == null || SelectionStatusTextBlock == null)
            {
                return;
            }

            if (selectedCandidateRow == null)
            {
                MonitorButton.IsEnabled = false;
                MonitorButton.Content = "Monitor";
                SelectionStatusTextBlock.Text = selectedCandidateWindowHandle.HasValue
                    ? $"Waiting for HWND {FormatWindowHandle(selectedCandidateWindowHandle.Value)}"
                    : "No candidate selected";
                return;
            }

            MonitorButton.IsEnabled = true;
            MonitorButton.Content = "Monitor " + ShortenText(selectedCandidateRow.Name, 36);
            SelectionStatusTextBlock.Text = $"Selected HWND {selectedCandidateRow.WindowHandle}";
        }

        private string FormatSelectedWindowHandle()
        {
            return selectedCandidateRow != null
                ? selectedCandidateRow.WindowHandle
                : selectedCandidateWindowHandle.HasValue ? FormatWindowHandle(selectedCandidateWindowHandle.Value) : "-";
        }

        private void RegisterDetectedGame(string processPath, string steamAppId, string source)
        {
            if (string.IsNullOrWhiteSpace(processPath))
            {
                AppendLog($"{source} registration skipped: process path is empty.");
                return;
            }

            if (string.IsNullOrWhiteSpace(steamAppId))
            {
                AppendLog($"{source} registration skipped: SteamAppId is empty. path=\"{processPath}\"");
                return;
            }

            try
            {
                GameConfig.Instance.RegisteredGames[processPath] = steamAppId;
                AppendLog($"{source} registered GameConfig: steamAppId={steamAppId}, path=\"{processPath}\"");
            }
            catch (Exception ex)
            {
                AppendLog($"{source} GameConfig registration failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string ShortenText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }

            if (maxLength <= 3)
            {
                return text.Substring(0, maxLength);
            }

            return text.Substring(0, maxLength - 3) + "...";
        }

        private SteamAppFinder.WindowInfo[] GetVisibleWindows()
        {
            var windows = new System.Collections.Generic.List<SteamAppFinder.WindowInfo>();
            finder.EnumWindows(windows.Add);
            return windows.ToArray();
        }

        private string FindDetectedSteamAppId(SteamAppFinder.WindowInfo selectedWindow)
        {
            if (selectedWindow == null)
            {
                return string.Empty;
            }

            try
            {
                SteamAppFinder.SteamAppInfo[] apps = finder.GetSteamProcesses();
                SteamAppFinder.SteamAppInfo matchedApp = apps.FirstOrDefault(app =>
                    app.Info != null &&
                    (app.Info.Handle == selectedWindow.Handle ||
                     app.Info.ProcessId == selectedWindow.ProcessId ||
                     string.Equals(app.Info.ProcessPath, selectedWindow.ProcessPath, StringComparison.OrdinalIgnoreCase)));

                return matchedApp == null ? string.Empty : matchedApp.SteamAppId;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            refreshTimer.Stop();
        }

        private void AppendLog(string message)
        {
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] [UI:{Thread.CurrentThread.ManagedThreadId}] {message}{Environment.NewLine}");
            LogTextBox.ScrollToEnd();
        }

        internal static string FormatWindowName(SteamAppFinder.WindowInfo window)
        {
            if (window == null)
            {
                return "-";
            }

            if (string.IsNullOrWhiteSpace(window.Title))
            {
                return FormatText(window.ProcessName);
            }

            return $"{window.Title} ({FormatText(window.ProcessName)})";
        }

        internal static string FormatText(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? "-" : text;
        }

        internal static string FormatWindowHandle(IntPtr handle)
        {
            return "0x" + handle.ToInt64().ToString("X");
        }
    }

    public sealed class SteamAppFinderRow
    {
        public SteamAppFinderRow(SteamAppFinder.SteamAppInfo app)
        {
            App = app ?? throw new ArgumentNullException(nameof(app));
        }

        public SteamAppFinder.SteamAppInfo App { get; private set; }

        public string Name
        {
            get { return SteamAppFinderTest.FormatWindowName(App.Info); }
        }

        public string SteamAppId
        {
            get { return SteamAppFinderTest.FormatText(App.SteamAppId); }
        }

        public uint ProcessId
        {
            get { return App.Info.ProcessId; }
        }

        public string ProcessName
        {
            get { return SteamAppFinderTest.FormatText(App.Info.ProcessName); }
        }

        public string ProcessPath
        {
            get { return SteamAppFinderTest.FormatText(App.Info.ProcessPath); }
        }

        public string WindowHandle
        {
            get { return SteamAppFinderTest.FormatWindowHandle(App.Info.Handle); }
        }
    }
}
