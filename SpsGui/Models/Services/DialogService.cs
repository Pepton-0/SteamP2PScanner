using SpsGui.Views;
using SpsLogic;
using System;
using System.Diagnostics;
using System.Windows;

namespace SpsGui.Models.Services
{
    /// <summary>
    /// Provides view models with access to modal dialogs without exposing concrete window types.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>
        /// Shows the window selection dialog.
        /// </summary>
        /// <param name="windows">Candidate windows to display.</param>
        /// <returns>The selected window, or null when the dialog is cancelled.</returns>
        WindowInfo ShowWindowSelectDialog(WindowInfo[] windows);

        /// <summary>
        /// Shows the Steam app id input dialog.
        /// </summary>
        /// <param name="window">Window associated with the app id.</param>
        /// <param name="initialSteamAppId">Initial app id text.</param>
        /// <returns>The entered Steam app id, or null when the dialog is cancelled.</returns>
        string ShowSteamAppIdDialog(WindowInfo window, string initialSteamAppId);

        /// <summary>
        /// Shows the exported archive path dialog.
        /// </summary>
        /// <param name="archivePath">Archive path to display.</param>
        void ShowArchivePathDialog(string archivePath);

        /// <summary>
        /// Shows the Steam console command dialog.
        /// </summary>
        /// <param name="command">Steam console command to display.</param>
        void ShowSteamConsoleCommandDialog(string command);

        /// <summary>
        /// Shows a new version dialog and opens the release page when accepted.
        /// </summary>
        /// <param name="version">Available release version.</param>
        /// <param name="downloadUri">Release page URI.</param>
        void ShowNewVersionDownloadDialog(string version, Uri downloadUri);

        /// <summary>
        /// Shows a plain message owned by the application main window when possible.
        /// </summary>
        /// <param name="message">Message text to display.</param>
        void ShowMessage(string message);
    }

    /// <summary>
    /// Creates and shows application dialogs for view models.
    /// </summary>
    public class DialogService : IDialogService
    {
        private readonly IVersionCheckService VerCheckServ;

        public DialogService(IVersionCheckService verCheckServ)
        {
            this.VerCheckServ = verCheckServ;
        }

        /// <inheritdoc />
        public WindowInfo ShowWindowSelectDialog(WindowInfo[] windows)
        {
            var dialog = new WindowSelectDialog(windows);
            return ShowDialog(dialog) == true ? dialog.SelectedWindow : null;
        }

        /// <inheritdoc />
        public string ShowSteamAppIdDialog(WindowInfo window, string initialSteamAppId)
        {
            var dialog = new SteamAppIdDialog(window, initialSteamAppId);
            return ShowDialog(dialog) == true ? dialog.SteamAppId : null;
        }

        /// <inheritdoc />
        public void ShowArchivePathDialog(string archivePath)
        {
            ShowDialog(new ArchivePathDialog(archivePath));
        }

        /// <inheritdoc />
        public void ShowSteamConsoleCommandDialog(string command)
        {
            ShowDialog(new SteamConsoleCommandDialog(command));
        }

        /// <inheritdoc />
        public void ShowNewVersionDownloadDialog(string version, Uri downloadUri)
        {
            if (downloadUri == null)
            {
                return;
            }

            var dialog = new NewVersionDialog(VerCheckServ.GetVersion(), version);
            if (ShowDialog(dialog) != true)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(downloadUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowMessage("Failed to open download page: " + ex.Message);
            }
        }

        /// <inheritdoc />
        public void ShowMessage(string message)
        {
            Window owner = GetOwner();
            if (owner == null)
            {
                MessageBox.Show(message);
            }
            else
            {
                MessageBox.Show(owner, message);
            }
        }

        private static bool? ShowDialog(Window dialog)
        {
            Window owner = GetOwner();
            if (owner != null && owner != dialog)
            {
                dialog.Owner = owner;
            }

            return dialog.ShowDialog();
        }

        private static Window GetOwner()
        {
            return Application.Current == null ? null : Application.Current.MainWindow;
        }
    }
}
