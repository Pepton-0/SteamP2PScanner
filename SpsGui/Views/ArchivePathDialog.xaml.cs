using MahApps.Metro.Controls;
using System;
using System.Windows;

namespace SpsGui.Views
{
    /// <summary>
    /// Shows the exported packet archive path and offers clipboard copy.
    /// </summary>
    public partial class ArchivePathDialog : MetroWindow
    {
        private readonly string archivePath;

        /// <summary>
        /// Initializes the archive path dialog.
        /// </summary>
        /// <param name="archivePath">Absolute or relative archive path to display. Must not be null.</param>
        public ArchivePathDialog(string archivePath)
        {
            if (archivePath == null)
            {
                throw new ArgumentNullException(nameof(archivePath));
            }

            InitializeComponent();
            this.archivePath = archivePath;
            PathTextBox.Text = archivePath;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(archivePath);
            DialogResult = true;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
