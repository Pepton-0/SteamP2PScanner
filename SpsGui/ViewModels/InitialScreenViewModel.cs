using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpsGui.Views;
using SpsLogic;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace SpsGui.ViewModels
{
    /// <summary>
    /// Provides startup screen state for Steam executable setup and Steam application selection.
    /// </summary>
    public class InitialScreenViewModel : ObservableObject
    {
        private readonly ISteamAppFinder steamAppFinder;
        private readonly DispatcherTimer autoDetectTimer;
        private readonly RelayCommand monitorSelectedCommand;
        private SteamAppCandidateViewModel selectedCandidate;
        private IntPtr? selectedWindowHandle;
        private bool isRefreshingAutoCandidates;
        private string steamExePath;

        /// <summary>
        /// Raised when the user selected a Steam application and profiling should start.
        /// </summary>
        public event EventHandler<SteamAppInfo> ProfileRequested;

        /// <summary>
        /// Initializes the startup screen and starts periodic automatic Steam app detection.
        /// </summary>
        /// <param name="steamAppFinder">Finder used to enumerate windows and detect Steam application ids.</param>
        public InitialScreenViewModel(ISteamAppFinder steamAppFinder)
        {
            this.steamAppFinder = steamAppFinder ?? throw new ArgumentNullException(nameof(steamAppFinder));

            steamExePath = AppConfig.Instance.SteamExe;

            ManualSelectCommand = new RelayCommand(ManualSelect);
            monitorSelectedCommand = new RelayCommand(MonitorSelected, CanMonitorSelected);
            MonitorSelectedCommand = monitorSelectedCommand;

            autoDetectTimer = new DispatcherTimer(DispatcherPriority.Background);
            autoDetectTimer.Interval = TimeSpan.FromSeconds(1);
            autoDetectTimer.Tick += (sender, args) => RefreshAutoCandidates("timer");
            autoDetectTimer.Start();

            RefreshAutoCandidates("startup");
        }

        /// <summary>
        /// Gets candidates visible to automatic selection.
        /// </summary>
        public ObservableCollection<SteamAppCandidateViewModel> AutoCandidates { get; } =
            new ObservableCollection<SteamAppCandidateViewModel>();

        /// <summary>
        /// Gets a command that opens manual window and app id selection dialogs.
        /// </summary>
        public IRelayCommand ManualSelectCommand { get; private set; }

        /// <summary>
        /// Gets a command that starts profiling for the selected automatic candidate.
        /// </summary>
        public IRelayCommand MonitorSelectedCommand { get; private set; }

        /// <summary>
        /// Gets or sets the Steam executable path editable on the startup screen.
        /// </summary>
        public string SteamExePath
        {
            get { return steamExePath; }
            set
            {
                if (SetProperty(ref steamExePath, value) &&
                    File.Exists(value))
                {
                    AppConfig.Instance.SteamExe = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected automatic candidate.
        /// </summary>
        public SteamAppCandidateViewModel SelectedCandidate
        {
            get { return selectedCandidate; }
            set
            {
                IntPtr? previousHandle = selectedCandidate == null
                    ? (IntPtr?)null
                    : selectedCandidate.AppInfo.Info.Handle;

                if (SetProperty(ref selectedCandidate, value))
                {
                    if (selectedCandidate == null)
                    {
                        if (!isRefreshingAutoCandidates)
                        {
                            selectedWindowHandle = null;
                        }
                    }
                    else
                    {
                        selectedWindowHandle = selectedCandidate.AppInfo.Info.Handle;
                        if (!previousHandle.HasValue ||
                            previousHandle.Value != selectedWindowHandle.Value ||
                            !isRefreshingAutoCandidates)
                        {
                            LogSelectedCandidate(selectedCandidate);
                        }
                    }

                    OnPropertyChanged(nameof(MonitorButtonText));
                    monitorSelectedCommand.NotifyCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Gets the current automatic selection button text.
        /// </summary>
        public string MonitorButtonText
        {
            get
            {
                return SelectedCandidate == null
                    ? "Nothing detected"
                    : "Monitor " + SelectedCandidate.DisplayName;
            }
        }

        private void ManualSelect()
        {
            autoDetectTimer.Stop();

            try
            {
                WindowInfo[] windows = EnumerateWindows();
                var windowDialog = new WindowSelectDialog(windows)
                {
                    Owner = Application.Current.MainWindow
                };

                if (windowDialog.ShowDialog() != true || windowDialog.SelectedWindow == null)
                {
                    return;
                }

                WindowInfo window = windowDialog.SelectedWindow;
                string steamAppId = GameConfig.Instance.RegisteredGames[window.ProcessPath];

                if (string.IsNullOrWhiteSpace(steamAppId))
                {
                    steamAppId = FindDetectedSteamAppId(window);
                    var appIdDialog = new SteamAppIdDialog(window, steamAppId)
                    {
                        Owner = Application.Current.MainWindow
                    };

                    if (appIdDialog.ShowDialog() != true)
                    {
                        return;
                    }

                    steamAppId = appIdDialog.SteamAppId;
                }

                RequestProfile(new SteamAppInfo(window, steamAppId, true));
            }
            catch (Exception ex)
            {
                Logger.Log("Manual Steam app selection failed: " + ex, true);
            }
            finally
            {
                autoDetectTimer.Start();
            }
        }

        private void MonitorSelected()
        {
            if (SelectedCandidate == null)
            {
                return;
            }

            SteamAppInfo appInfo = SelectedCandidate.AppInfo;

            RequestProfile(appInfo);
        }

        private bool CanMonitorSelected()
        {
            return SelectedCandidate != null;
        }

        private void RefreshAutoCandidates(string source)
        {
            try
            {
                SteamAppInfo[] apps = steamAppFinder.GetSteamProcesses()
                    .Where(app => app.IsVisible)
                    .OrderBy(app => FormatWindowName(app.Info))
                    .ToArray();

                isRefreshingAutoCandidates = true;
                try
                {
                    AutoCandidates.Clear();
                    foreach (SteamAppInfo app in apps)
                    {
                        AutoCandidates.Add(new SteamAppCandidateViewModel(app));
                    }

                    RestoreSelection();
                }
                finally
                {
                    isRefreshingAutoCandidates = false;
                }

                Logger.DebugLog($"InitialScreen auto refresh: source={source}, count={AutoCandidates.Count}");
            }
            catch (Exception ex)
            {
                Logger.DebugLog("Auto Steam app detection failed: " + ex);
            }
        }

        private void RestoreSelection()
        {
            SteamAppCandidateViewModel next = null;

            if (AutoCandidates.Count == 1)
            {
                next = AutoCandidates[0];
            }
            else if (selectedWindowHandle.HasValue)
            {
                next = AutoCandidates.FirstOrDefault(candidate =>
                    candidate.AppInfo.Info.Handle == selectedWindowHandle.Value);
            }

            SelectedCandidate = next;
        }

        private WindowInfo[] EnumerateWindows()
        {
            var windows = new System.Collections.Generic.List<WindowInfo>();
            steamAppFinder.EnumWindows(windows.Add);
            return windows.ToArray();
        }

        private string FindDetectedSteamAppId(WindowInfo selectedWindow)
        {
            SteamAppInfo matched = steamAppFinder.GetSteamProcesses().FirstOrDefault(app =>
                app.Info.Handle == selectedWindow.Handle ||
                app.Info.ProcessId == selectedWindow.ProcessId ||
                string.Equals(app.Info.ProcessPath, selectedWindow.ProcessPath, StringComparison.OrdinalIgnoreCase));

            return matched == null ? string.Empty : matched.SteamAppId;
        }

        private void RequestProfile(SteamAppInfo appInfo)
        {
            autoDetectTimer.Stop();
            ProfileRequested?.Invoke(this, appInfo);
        }

        private static void LogSelectedCandidate(SteamAppCandidateViewModel candidate)
        {
            if (candidate == null)
            {
                return;
            }

            SteamAppInfo appInfo = candidate.AppInfo;
            WindowInfo window = appInfo.Info;
            Logger.DebugLog(
                "InitialScreen auto candidate selected: " +
                "name=\"" + candidate.DisplayName + "\", " +
                "steamAppId=" + appInfo.SteamAppId + ", " +
                "hwnd=" + candidate.WindowHandle + ", " +
                "pid=" + window.ProcessId + ", " +
                "processName=\"" + window.ProcessName + "\", " +
                "processPath=\"" + window.ProcessPath + "\"");
        }

        internal static string FormatWindowName(WindowInfo window)
        {
            if (window == null)
            {
                return "-";
            }

            if (string.IsNullOrWhiteSpace(window.Title))
            {
                return string.IsNullOrWhiteSpace(window.ProcessName) ? "-" : window.ProcessName;
            }

            return window.Title + " (" + window.ProcessName + ")";
        }
    }

    /// <summary>
    /// Exposes a Steam application detection result for list binding.
    /// </summary>
    public class SteamAppCandidateViewModel
    {
        /// <summary>
        /// Initializes a candidate row.
        /// </summary>
        /// <param name="appInfo">Detected Steam application information. Must not be null.</param>
        public SteamAppCandidateViewModel(SteamAppInfo appInfo)
        {
            AppInfo = appInfo ?? throw new ArgumentNullException(nameof(appInfo));
        }

        /// <summary>
        /// Gets the source detection information.
        /// </summary>
        public SteamAppInfo AppInfo { get; private set; }

        /// <summary>
        /// Gets the user-facing window name.
        /// </summary>
        public string DisplayName
        {
            get { return InitialScreenViewModel.FormatWindowName(AppInfo.Info); }
        }

        /// <summary>
        /// Gets the Steam application id.
        /// </summary>
        public string SteamAppId
        {
            get { return AppInfo.SteamAppId; }
        }

        /// <summary>
        /// Gets the hexadecimal window handle text.
        /// </summary>
        public string WindowHandle
        {
            get { return "0x" + AppInfo.Info.Handle.ToInt64().ToString("X"); }
        }
    }
}
