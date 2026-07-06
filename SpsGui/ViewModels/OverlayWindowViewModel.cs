using CommunityToolkit.Mvvm.ComponentModel;
using SpsGui.Models.Services;
using SpsLogic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace SpsGui.ViewModels
{
    /// <summary>
    /// Provides target-window and row data for the transparent ping overlay.
    /// </summary>
    public class OverlayWindowViewModel : ObservableObject, IDisposable
    {
        private bool isDisposed;

        /// <summary>
        /// Initializes the overlay view model for a target game window.
        /// </summary>
        /// <param name="targetWindowInfo">Target window followed by the overlay. Must not be null.</param>
        /// <param name="applicationTitleService">Service used to create empty-overlay title text.</param>
        public OverlayWindowViewModel(WindowInfo targetWindowInfo, IApplicationTitleService applicationTitleService)
        {
            TargetWindowInfo = targetWindowInfo ?? throw new ArgumentNullException(nameof(targetWindowInfo));
            if (applicationTitleService == null)
            {
                throw new ArgumentNullException(nameof(applicationTitleService));
            }

            ApplicationTitle = applicationTitleService.CreateApplicationTitle();
            AppConfig.Instance.PropertyChanged += AppConfig_PropertyChanged;
        }

        /// <summary>
        /// Gets the window followed by overlay behavior.
        /// </summary>
        public WindowInfo TargetWindowInfo { get; private set; }

        /// <summary>
        /// Gets rows displayed in the overlay.
        /// </summary>
        public ObservableCollection<PingProfileSnapshot> Profiles { get; } =
            new ObservableCollection<PingProfileSnapshot>();

        /// <summary>
        /// Gets the title text shown while there are no overlay rows.
        /// </summary>
        public string ApplicationTitle { get; private set; }

        public Visibility EmptyTitleVisibility
        {
            get { return Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility TableVisibility
        {
            get { return Profiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible; }
        }

        /// <summary>
        /// Gets whether the overlay is enabled.
        /// </summary>
        public bool OverlayEnabled
        {
            get { return AppConfig.Instance.OverlayEnabled; }
        }

        public double OverlayOffsetX
        {
            get { return AppConfig.Instance.OverlayOffsetX; }
        }

        public double OverlayOffsetY
        {
            get { return AppConfig.Instance.OverlayOffsetY; }
        }

        public Visibility ShowNameVisibility
        {
            get { return AppConfig.Instance.OverlayShowName ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ShowStatusVisibility
        {
            get { return AppConfig.Instance.OverlayShowStatus ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ShowAverageVisibility
        {
            get { return AppConfig.Instance.OverlayShowAverage ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ShowLossVisibility
        {
            get { return AppConfig.Instance.OverlayShowLoss ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ShowBoxPlotVisibility
        {
            get { return AppConfig.Instance.ShowBoxPlot ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ShowChartVisibility
        {
            get { return AppConfig.Instance.OverlayShowChart ? Visibility.Visible : Visibility.Collapsed; }
        }

        /// <summary>
        /// Replaces overlay rows with the latest profile snapshots.
        /// </summary>
        /// <param name="snapshots">Profile snapshots to show. Null is treated as empty.</param>
        public void UpdateProfiles(IEnumerable<PingProfileSnapshot> snapshots)
        {
            Profiles.Clear();

            if (snapshots != null)
            {
                foreach (PingProfileSnapshot snapshot in snapshots)
                {
                    if (snapshot != null)
                    {
                        Profiles.Add(snapshot);
                    }
                }
            }

            OnPropertyChanged(nameof(EmptyTitleVisibility));
            OnPropertyChanged(nameof(TableVisibility));
        }

        /// <summary>
        /// Releases AppConfig change subscriptions.
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            AppConfig.Instance.PropertyChanged -= AppConfig_PropertyChanged;
        }

        private void AppConfig_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppConfig.OverlayEnabled))
            {
                OnPropertyChanged(nameof(OverlayEnabled));
            }

            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppConfig.OverlayOffsetX))
            {
                OnPropertyChanged(nameof(OverlayOffsetX));
            }

            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppConfig.OverlayOffsetY))
            {
                OnPropertyChanged(nameof(OverlayOffsetY));
            }

            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppConfig.OverlayShowName))
            {
                OnPropertyChanged(nameof(ShowNameVisibility));
            }

            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppConfig.OverlayShowStatus))
            {
                OnPropertyChanged(nameof(ShowStatusVisibility));
            }

            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppConfig.OverlayShowAverage))
            {
                OnPropertyChanged(nameof(ShowAverageVisibility));
            }

            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppConfig.OverlayShowLoss))
            {
                OnPropertyChanged(nameof(ShowLossVisibility));
            }

            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppConfig.ShowBoxPlot))
            {
                OnPropertyChanged(nameof(ShowBoxPlotVisibility));
            }

            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppConfig.OverlayShowChart))
            {
                OnPropertyChanged(nameof(ShowChartVisibility));
            }
        }
    }
}
