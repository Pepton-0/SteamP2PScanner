using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpsGui.Models;
using SpsGui.Views;
using SpsLogic;
using System;
using System.Reflection;
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

        public IRelayCommand<object> ExitCommand { get; private set; }

        /// <summary>
        /// Initializes the root window view model.
        /// </summary>
        /// <param name="steamAppFinder">Finder service used by the initial screen.</param>
        public CoreWindowViewModel(ISteamAppFinder steamAppFinder, IConductor conductor)
        {
            (Conductor) = conductor;

            ApplicationTitle = "SteamP2PScanner " + Assembly.GetExecutingAssembly().GetName().Version;

            var initialScreen = new InitialScreenViewModel(steamAppFinder);
            initialScreen.ProfileRequested += OnProfileRequested;
            CurrentViewModel = initialScreen;
            ExitCommand = new RelayCommand<object>(OnExit);
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
            SteamPeerManager manager = null;
            try
            {
                manager = Conductor.CreateSteamPeerManager(appInfo);
            }
            catch(Exception e)
            {
                // TODO i18n
                Logger.Log("Failed to initialize steam api because " + e.Message, true);
                MessageBox.Show("Initializing steam api is failed because " + e.Message);
                manager = null;
            }

            if (manager != null)
            {
                if (GameConfig.Instance.RegisteredGames[appInfo.Info.ProcessPath] != appInfo.SteamAppId)
                {
                    // overwrite the gameconfig file
                    GameConfig.Instance.RegisteredGames[appInfo.Info.ProcessPath] = appInfo.SteamAppId;
                }
                CurrentProcessName = appInfo.Info.ProcessName;
                CurrentViewModel = new ProfileScreenViewModel(appInfo, manager, Conductor.PacketScan);
                // TODO create overlay
            }
        }

        private void OnExit(object sender)
        {
            var window = sender as CoreWindow;
            window.Hide();
            SteamPeerManager.ShutdownSteamApi();
            App.Current.Shutdown();
        }
    }
}
