using CommunityToolkit.Mvvm.ComponentModel;
using SpsLogic;
using System;

namespace SpsGui.ViewModels
{
    /// <summary>
    /// Provides profiling screen state for the selected Steam application.
    /// </summary>
    public class ProfileScreenViewModel : ObservableObject
    {
        private readonly SteamAppInfo appInfo;

        /// <summary>
        /// Initializes the profiling screen.
        /// </summary>
        /// <param name="appInfo">Selected Steam application information. Must not be null.</param>
        public ProfileScreenViewModel(SteamAppInfo appInfo)
        {
            this.appInfo = appInfo ?? throw new ArgumentNullException(nameof(appInfo));
        }

        /// <summary>
        /// Gets the selected window title and process name.
        /// </summary>
        public string TargetName
        {
            get { return InitialScreenViewModel.FormatWindowName(appInfo.Info); }
        }

        /// <summary>
        /// Gets the detected or registered Steam application id.
        /// </summary>
        public string SteamAppId
        {
            get { return appInfo.SteamAppId; }
        }

        /// <summary>
        /// Gets the selected process id.
        /// </summary>
        public uint ProcessId
        {
            get { return appInfo.Info.ProcessId; }
        }

        /// <summary>
        /// Gets the selected process executable path.
        /// </summary>
        public string ProcessPath
        {
            get { return appInfo.Info.ProcessPath; }
        }

        /// <summary>
        /// Gets the selected window handle text.
        /// </summary>
        public string WindowHandle
        {
            get { return "0x" + appInfo.Info.Handle.ToInt64().ToString("X"); }
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
                }
            }
        }
    }
}
