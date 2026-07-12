using MahApps.Metro.Controls;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SpsGui.Views
{
    /// <summary>
    /// Prompts the user to set a valid steam.exe path.
    /// </summary>
    public partial class SteamExePathDialog : MetroWindow
    {
        /// <summary>
        /// Initializes the Steam executable path dialog.
        /// </summary>
        /// <param name="initialSteamExePath">Initial steam.exe path text.</param>
        public SteamExePathDialog(string initialSteamExePath)
        {
            InitializeComponent();

            SteamExePathTextBox.Text = initialSteamExePath ?? string.Empty;
            SteamExePathTextBox.SelectAll();
            SteamExePathTextBox.Focus();
            RefreshValidation();
        }

        /// <summary>
        /// Gets the validated steam.exe path after Save is pressed.
        /// </summary>
        public string SteamExePath { get; private set; }

        private void SteamExePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshValidation();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!RefreshValidation())
            {
                return;
            }

            SteamExePath = SteamExePathTextBox.Text.Trim();
            DialogResult = true;
        }

        private bool RefreshValidation()
        {
            string path = SteamExePathTextBox.Text.Trim();
            bool valid = File.Exists(path);
            SaveButton.IsEnabled = valid;
            ValidationTextBlock.Text = valid ? string.Empty : (string)Application.Current.Resources["SteamExePathInvalidMessage"];
            return valid;
        }
    }
}
