using MahApps.Metro.Controls;
using System.Windows;

namespace SpsGui.Views
{
    /// <summary>
    /// Prompts the user to open the release page for a newer application version.
    /// </summary>
    public partial class NewVersionDialog : MetroWindow
    {
        /// <summary>
        /// Initializes the new version dialog.
        /// </summary>
        /// <param name="version">Release version shown in the title.</param>
        public NewVersionDialog(string oldVer, string newVer)
        {
            InitializeComponent();
            Title = App.Current.Resources["NewVersionTitle"].ToString().Replace("@", oldVer).Replace("*", newVer);
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            App.Current.Shutdown();
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
