using CommunityToolkit.Mvvm.ComponentModel;
using SpsLogic;
using System.Reflection;

namespace SpsGui.ViewModels
{
    /// <summary>
    /// Coordinates the root window state and swaps the active screen view model.
    /// </summary>
    public class CoreWindowViewModel : ObservableObject
    {
        private object currentViewModel;

        /// <summary>
        /// Initializes the root window view model.
        /// </summary>
        /// <param name="steamAppFinder">Finder service used by the initial screen.</param>
        public CoreWindowViewModel(ISteamAppFinder steamAppFinder)
        {
            ApplicationTitle = "SteamP2PScanner " + Assembly.GetExecutingAssembly().GetName().Version;

            var initialScreen = new InitialScreenViewModel(steamAppFinder);
            initialScreen.ProfileRequested += OnProfileRequested;
            CurrentViewModel = initialScreen;
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
            private set { SetProperty(ref currentViewModel, value); }
        }

        private void OnProfileRequested(object sender, SteamAppInfo appInfo)
        {
            CurrentViewModel = new ProfileScreenViewModel(appInfo);
        }
    }
}
