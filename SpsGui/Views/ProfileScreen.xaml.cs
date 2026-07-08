using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;
using SpsGui.ViewModels;

namespace SpsGui.Views
{
    /// <summary>
    /// Interaction logic for the profiling screen.
    /// </summary>
    public partial class ProfileScreen : UserControl
    {
        /// <summary>
        /// Initializes profiling screen components.
        /// </summary>
        public ProfileScreen()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            var viewModel = DataContext as ProfileScreenViewModel;
            if (viewModel != null &&
                !viewModel.IsArchiveUsable &&
                HistoryGridView.Columns.Contains(ArchiveColumn))
            {
                HistoryGridView.Columns.Remove(ArchiveColumn);
            }
        }

        private void OnSteamProfileNavigate(object sender, RequestNavigateEventArgs e)
        {
            if (e.Uri == null)
            {
                return;
            }

            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
