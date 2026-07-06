using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

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
