using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpsGui.Models;
using SpsGui.Models.Services;
using SpsLogic;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace SpsGui.ViewModels
{
    /// <summary>
    /// Coordinates the root window state and swaps the active screen view model.
    /// </summary>
    public class CoreWindowViewModel : ObservableObject
    {
        private object currentViewModel;
        private readonly IConductor Conductor;
        private readonly IDialogService dialogService;
        private readonly IOverlayService overlayService;
        private readonly IFindSteamExeService findSteamExeService;
        private bool isExiting;

        public IRelayCommand<object> ExitCommand { get; private set; }

        public IRelayCommand<object> TestCommand { get; private set; }

        /// <summary>
        /// Initializes the root window view model.
        /// </summary>
        /// <param name="steamAppFinder">Finder service used by the initial screen.</param>
        /// <param name="conductor">Application conductor used to create runtime model objects.</param>
        /// <param name="applicationTitleService">Service used to create application title text.</param>
        /// <param name="dialogService">Service used to show modal dialogs.</param>
        /// <param name="overlayService">Service used to own the overlay window.</param>
        /// <param name="findSteamExeService">Service used to find steam.exe candidates for path recovery.</param>
        public CoreWindowViewModel(
            ISteamAppFinder steamAppFinder,
            IConductor conductor,
            IApplicationTitleService applicationTitleService,
            IDialogService dialogService,
            IOverlayService overlayService,
            IVersionCheckService verCheckService,
            IFindSteamExeService findSteamExeService)
        {
            Conductor = conductor ?? throw new ArgumentNullException(nameof(conductor));
            if (applicationTitleService == null)
            {
                throw new ArgumentNullException(nameof(applicationTitleService));
            }

            this.dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            this.overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));
            this.findSteamExeService = findSteamExeService ?? throw new ArgumentNullException(nameof(findSteamExeService));
            if (verCheckService == null)
            {
                throw new ArgumentNullException(nameof(verCheckService));
            }

            ApplicationTitle = applicationTitleService.CreateApplicationTitle();

            var initialScreen = new InitialScreenViewModel(steamAppFinder, dialogService);
            initialScreen.ProfileRequested += OnProfileRequested;
            CurrentViewModel = initialScreen;
            ExitCommand = new RelayCommand<object>(OnExit);
            TestCommand = new RelayCommand<object>((d) => Logger.Log("something"));

            Task.Run(() =>
            {
                try
                {
                    if (verCheckService.FetchLatest())
                    {
                        string v = verCheckService.GetLatestRelease()["tag_name"].ToString();
                        string currentVersion = verCheckService.GetVersion();
                        if (IsNewerVersion(currentVersion, v))
                        {
                            if (AppConfig.Instance.IgnoreLatest)
                            {
                                Logger.Log(
                                    $"New version dialog was ignored because IgnoreLatest is enabled. {currentVersion} -> {v}",
                                    true);
                                return;
                            }

                            Uri downloadUri = new Uri("https://github.com/" + verCheckService.GetRepoName() + "/releases/tag/" + v);
                            App.Current.Dispatcher.Invoke(() =>
                            {
                                dialogService.ShowNewVersionDownloadDialog(v, downloadUri);
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to check latest version because " + ex.Message, true);
                }
            });
        }

        /// <summary>
        /// Gets the text shown on the left side of the title area.
        /// </summary>
        public string ApplicationTitle { get; private set; }

        /// <summary>
        /// Gets the currently displayed screen view model.
        /// </summary>
        public object CurrentViewModel
        {
            get { return currentViewModel; }
            private set
            {
                IDisposable disposable = currentViewModel as IDisposable;
                if (SetProperty(ref currentViewModel, value) && disposable != null)
                {
                    disposable.Dispose();
                }
            }
        }

        /// <summary>
        /// Gets the process name shown in the right side title commands.
        /// </summary>
        public string CurrentProcessName
        {
            get { return currentProcessName; }
            private set { SetProperty(ref currentProcessName, value); }
        }
        private string currentProcessName = string.Empty;

        private void OnProfileRequested(object sender, SteamAppInfo appInfo)
        {
            // Check steam paths faster than steam command and manager
            LogSteamPathDiagnostics();
            if (!File.Exists(AppConfig.Instance.SteamExe))
            {
                SteamExeCandidate[] steamExeCandidates = FindSteamExeCandidates();
                string steamExe = dialogService.ShowSteamExePathDialog(AppConfig.Instance.SteamExe, steamExeCandidates);
                if (string.IsNullOrWhiteSpace(steamExe))
                {
                    return;
                }

                AppConfig.Instance.SteamExe = steamExe;
                LogSteamPathDiagnostics();
                if (!File.Exists(AppConfig.Instance.SteamExe))
                {
                    return;
                }
            }

            //maybe requesting the command faster than manager resolves
            //the problem that manager cannot load ipc file somehow.
            RequestManualSteamConsoleCommand();

            SteamPeerManager manager = null;
            try
            {
                manager = Conductor.CreateSteamPeerManager(appInfo);
            }
            catch(Exception e)
            {
                // TODO i18n
                Logger.Log("Failed to initialize steam api because " + e.Message, true);
                dialogService.ShowMessage("Initializing steam api is failed because " + e.Message);
                return;
            }

            if (manager == null)
            {
                Logger.Log("Failed to initialize steam api because SteamPeerManager was not created.", true);
                dialogService.ShowMessage("Initializing steam api is failed.");
                return;
            }

            if (GameConfig.Instance.RegisteredGames[appInfo.Info.ProcessPath] != appInfo.SteamAppId)
            {
                // overwrite the gameconfig file
                GameConfig.Instance.RegisteredGames[appInfo.Info.ProcessPath] = appInfo.SteamAppId;
            }
            CurrentProcessName = appInfo.Info.ProcessName;
            CurrentViewModel = new ProfileScreenViewModel(appInfo, manager, Conductor.PacketScan, dialogService, overlayService);
            overlayService.Show(appInfo.Info);
        }

        private int RequestManualSteamConsoleCommand()
        {
            int result = Conductor.RequestSteamConsole();
            if (result == 1)
            {
                dialogService.ShowSteamConsoleCommandDialog(Conductor.SteamConsoleCommand);
            }
            else if(result == -1)
            {
                dialogService.ShowMessage("Failed to show steam console for some reason.");
            }

            return result;
        }

        private SteamExeCandidate[] FindSteamExeCandidates()
        {
            try
            {
                FindSteamExeResult result = findSteamExeService.Find();
                foreach (string message in result.Messages)
                {
                    Logger.Log("FindSteamExeService: " + message, true);
                }

                Logger.Log("FindSteamExeService candidates: " + result.Candidates.Length, true);
                return result.Candidates;
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to find steam.exe candidates: " + ex.GetType().Name + ": " + ex.Message, true);
                return new SteamExeCandidate[0];
            }
        }

        private static void LogSteamPathDiagnostics()
        {
            Logger.Log("AppConfig.SteamExe: " + AppConfig.Instance.SteamExe, true);
            Logger.Log("AppConfig.SteamLogPath: " + AppConfig.Instance.SteamLogPath, true);
            Logger.Log("AppConfig.SteamExe exists: " + File.Exists(AppConfig.Instance.SteamExe), true);
            Logger.Log("AppConfig.SteamLogDir exists: " + Directory.Exists(AppConfig.Instance.SteamLogDir), true);
        }

        private static bool IsNewerVersion(string currentVersionText, string latestVersionText)
        {
            Version currentVersion;
            Version latestVersion;
            if (Version.TryParse(NormalizeVersion(currentVersionText), out currentVersion) &&
                Version.TryParse(NormalizeVersion(latestVersionText), out latestVersion))
            {
                return currentVersion.CompareTo(latestVersion) < 0;
            }

            return string.Compare(currentVersionText, latestVersionText, StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string NormalizeVersion(string versionText)
        {
            if (string.IsNullOrWhiteSpace(versionText))
            {
                return string.Empty;
            }

            return versionText.Trim().TrimStart('v', 'V');
        }

        private void OnExit(object sender)
        {
            if (isExiting)
            {
                return;
            }

            isExiting = true;
            overlayService.Close();
            SteamPeerManager.ShutdownSteamApi();
            if (Application.Current != null)
            {
                Application.Current.Shutdown();
            }
        }
    }
}
