using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpsGui.Models;
using SpsGui.Models.Services;
using SpsLogic;
using System;
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
        public CoreWindowViewModel(
            ISteamAppFinder steamAppFinder,
            IConductor conductor,
            IApplicationTitleService applicationTitleService,
            IDialogService dialogService,
            IOverlayService overlayService,
            IVersionCheckService verCheckService)
        {
            Conductor = conductor ?? throw new ArgumentNullException(nameof(conductor));
            if (applicationTitleService == null)
            {
                throw new ArgumentNullException(nameof(applicationTitleService));
            }

            this.dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            this.overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));

            ApplicationTitle = applicationTitleService.CreateApplicationTitle();

            var initialScreen = new InitialScreenViewModel(steamAppFinder, dialogService);
            initialScreen.ProfileRequested += OnProfileRequested;
            CurrentViewModel = initialScreen;
            ExitCommand = new RelayCommand<object>(OnExit);
            TestCommand = new RelayCommand<object>((d) => Logger.Log("something"));

            Task.Run(() =>
            {
                if (verCheckService.FetchLatest())
                {
                    string v = verCheckService.GetLatestRelease()["tag_name"].ToString();
                    if (string.Compare(verCheckService.GetVersion(), v) < 0)
                    {
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            linkUpdate.NavigateUri = new Uri("https://github.com/" + VersionCheck.repositoryName + "/releases/tag/" + v);
                            textUpdate.Text = string.Format("NEW VERSION ({0}), DOWNLOAD HERE", v);
                            this.ShowMessageAsync("New Version Available", string.Format("{0} is out! Click the link in the title bar to download it.", v));
                        });
                    }
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
