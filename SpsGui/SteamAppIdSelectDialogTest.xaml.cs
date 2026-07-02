using MahApps.Metro.Controls;
using SpsLogic;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace SpsGui
{
    public partial class SteamAppIdSelectDialogTest : MetroWindow
    {
        public SteamAppIdSelectDialogTest(
            SteamAppFinder.WindowInfo window,
            string initialSteamAppId)
        {
            InitializeComponent();

            WindowTextBlock.Text = SteamAppFinderTest.FormatWindowName(window);
            SteamAppIdTextBox.Text = initialSteamAppId ?? string.Empty;
            SteamAppIdTextBox.SelectAll();
            SteamAppIdTextBox.Focus();
            RefreshValidation();
        }

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
            ValidationTextBlock.Text = valid || text.Length == 0 ? string.Empty : "SteamAppId must be a positive integer.";
            return valid;
        }
    }
}
