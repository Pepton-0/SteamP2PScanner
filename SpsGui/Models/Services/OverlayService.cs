using SpsGui.Views;
using SpsLogic;
using System;
using System.Collections.Generic;
using System.Windows;
using OverlayWindowViewModel = SpsGui.ViewModels.OverlayWindowViewModel;

namespace SpsGui.Models.Services
{
    public interface IOverlayService
    {
        void Show(WindowInfo targetWindowInfo);

        void UpdateProfiles(IEnumerable<SpsGui.ViewModels.PingProfileSnapshot> snapshots);

        void Close();
    }

    /// <summary>
    /// Owns the lifetime of the overlay window used by the active profiling session.
    /// </summary>
    public class OverlayService : IOverlayService
    {
        private readonly IApplicationTitleService applicationTitleService;
        private OverlayWindow window;
        private OverlayWindowViewModel viewModel;

        public OverlayService(IApplicationTitleService applicationTitleService)
        {
            this.applicationTitleService = applicationTitleService ?? throw new ArgumentNullException(nameof(applicationTitleService));
        }

        public void Show(WindowInfo targetWindowInfo)
        {
            if (targetWindowInfo == null)
            {
                throw new ArgumentNullException(nameof(targetWindowInfo));
            }

            RunOnUiThread(() =>
            {
                Close();
                viewModel = new OverlayWindowViewModel(targetWindowInfo, applicationTitleService);
                window = new OverlayWindow
                {
                    DataContext = viewModel
                };
                window.Closed += OverlayWindow_Closed;
                window.Show();
            });
        }

        public void UpdateProfiles(IEnumerable<SpsGui.ViewModels.PingProfileSnapshot> snapshots)
        {
            RunOnUiThread(() =>
            {
                if (viewModel != null)
                {
                    viewModel.UpdateProfiles(snapshots);
                }
            });
        }

        public void Close()
        {
            RunOnUiThread(() =>
            {
                if (window != null)
                {
                    window.Closed -= OverlayWindow_Closed;
                    window.Close();
                    window = null;
                }

                if (viewModel != null)
                {
                    viewModel.Dispose();
                    viewModel = null;
                }
            });
        }

        private void OverlayWindow_Closed(object sender, EventArgs e)
        {
            if (viewModel != null)
            {
                viewModel.Dispose();
                viewModel = null;
            }

            window = null;
        }

        private static void RunOnUiThread(Action action)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }
    }
}
