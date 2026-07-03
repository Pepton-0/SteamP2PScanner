using MahApps.Metro.Controls;
using SpsGui.ViewModels;
using SpsLogic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace SpsGui.Views
{
    /// <summary>
    /// Prompts the user for a Steam application id.
    /// </summary>
    public partial class SteamAppIdDialog : MetroWindow
    {
        /// <summary>
        /// Initializes the Steam app id dialog.
        /// </summary>
        /// <param name="window">Selected window used for the dialog caption.</param>
        /// <param name="initialSteamAppId">Initial id text. Empty is allowed.</param>
        public SteamAppIdDialog(WindowInfo window, string initialSteamAppId)
        {
            InitializeComponent();

            WindowTextBlock.Text = InitialScreenViewModel.FormatWindowName(window);
            SteamAppIdTextBox.Text = initialSteamAppId ?? string.Empty;
            SteamAppIdTextBox.SelectAll();
            SteamAppIdTextBox.Focus();
            RefreshValidation();
        }

        /// <summary>
        /// Gets the validated Steam application id after OK is pressed.
        /// </summary>
        public string SteamAppId { get; private set; }

        private void SteamAppIdTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshValidation();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!RefreshValidation())
            {
                return;
            }

            SteamAppId = SteamAppIdTextBox.Text.Trim();
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private bool RefreshValidation()
        {
            string text = SteamAppIdTextBox.Text.Trim();
            ulong appId;
            bool valid = ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out appId) &&
                         appId > 0;

            OkButton.IsEnabled = valid;
            ValidationTextBlock.Text = valid || text.Length == 0
                ? string.Empty
                : "SteamAppId must be a positive integer.";
            return valid;
        }
    }
}
