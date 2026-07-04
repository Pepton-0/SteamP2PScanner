using MahApps.Metro.Controls;
using SpsLogic;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;

namespace SpsGui.Views
{
    /// <summary>
    /// Shows the Steam console command and requires copying before closing.
    /// </summary>
    public partial class SteamConsoleCommandDialog : MetroWindow
    {
        private readonly string command;
        private bool hasCopied;

        /// <summary>
        /// Initializes the Steam console command dialog.
        /// </summary>
        /// <param name="command">Command text to display and copy. Must not be null.</param>
        public SteamConsoleCommandDialog(string command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            InitializeComponent();
            this.command = command;
            CommandTextBox.Text = command;
            CommandTextBox.SelectAll();
            CommandTextBox.Focus();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(command);
                hasCopied = true;
            }
            catch (ExternalException ex)
            {
                Logger.Log("Failed to copy Steam console command because " + ex.Message, true);
                MessageBox.Show(this, "Failed to copy the Steam console command.");
            }

            CloseButton.IsEnabled = true;
            CloseButton.Focus();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void MetroWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!hasCopied)
            {
                e.Cancel = true;
            }
        }
    }
}
