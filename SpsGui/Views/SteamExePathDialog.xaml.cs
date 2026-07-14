using MahApps.Metro.Controls;
using SpsLogic;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SpsGui.Views
{
    /// <summary>
    /// Prompts the user to set a valid steam.exe path.
    /// </summary>
    public partial class SteamExePathDialog : MetroWindow
    {
        private static readonly Brush SuccessStatusBrush = new SolidColorBrush(Color.FromRgb(46, 125, 50));
        private static readonly Brush FailureStatusBrush = new SolidColorBrush(Color.FromRgb(198, 40, 40));

        private SteamExeCandidate selectedAutoCandidate;
        private bool isInitializing;

        /// <summary>
        /// Initializes the Steam executable path dialog.
        /// </summary>
        /// <param name="initialSteamExePath">Initial steam.exe path text.</param>
        /// <param name="autoCandidates">Auto-detected steam.exe candidates to present before manual input.</param>
        public SteamExePathDialog(string initialSteamExePath, SteamExeCandidate[] autoCandidates)
        {
            InitializeComponent();

            isInitializing = true;
            AutoCandidateListView.ItemsSource = (autoCandidates ?? new SteamExeCandidate[0])
                .Where(candidate => candidate != null)
                .Select(candidate => new SteamExeCandidateRow(candidate))
                .ToArray();
            SteamExePathTextBox.Text = initialSteamExePath ?? string.Empty;
            isInitializing = false;

            SetNeutralStatus();
            Loaded += SteamExePathDialog_Loaded;
        }

        /// <summary>
        /// Gets the validated steam.exe path after Save is pressed.
        /// </summary>
        public string SteamExePath { get; private set; }

        private void AutoCandidateListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = AutoCandidateListView.SelectedItem as SteamExeCandidateRow;
            selectedAutoCandidate = row == null ? null : row.Candidate;
            if (selectedAutoCandidate == null)
            {
                SetNeutralStatus();
                return;
            }

            SteamExePath = selectedAutoCandidate.Path;
            SaveButton.IsEnabled = true;
            SelectionStatusTextBlock.Foreground = SuccessStatusBrush;
            SelectionStatusTextBlock.Text = FormatResource("SteamExePathAutoSelectedStatus", selectedAutoCandidate.Path);
        }

        private void SteamExePathDialog_Loaded(object sender, RoutedEventArgs e)
        {
            if (AutoCandidateListView.Items.Count > 0)
            {
                AutoCandidateListView.Focus();
            }
        }

        private void SteamExePathTextBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (isInitializing)
            {
                return;
            }

            SelectManualInput();
        }

        private void SteamExePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isInitializing)
            {
                return;
            }

            SelectManualInput();
        }

        private void SelectManualInput()
        {
            selectedAutoCandidate = null;
            AutoCandidateListView.SelectedItem = null;
            RefreshManualSelection();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedAutoCandidate != null)
            {
                SteamExePath = selectedAutoCandidate.Path;
                DialogResult = true;
                return;
            }

            if (!RefreshManualSelection())
            {
                return;
            }

            SteamExePath = SteamExePathTextBox.Text.Trim();
            DialogResult = true;
        }

        private bool RefreshManualSelection()
        {
            string path = SteamExePathTextBox.Text.Trim();
            bool valid = File.Exists(path);
            SaveButton.IsEnabled = valid;
            SelectionStatusTextBlock.Foreground = valid ? SuccessStatusBrush : FailureStatusBrush;
            SelectionStatusTextBlock.Text = valid
                ? FormatResource("SteamExePathManualValidStatus", path)
                : FormatResource("SteamExePathManualInvalidStatus", path);
            return valid;
        }

        private void SetNeutralStatus()
        {
            SteamExePath = null;
            SaveButton.IsEnabled = false;
            SelectionStatusTextBlock.Foreground = FailureStatusBrush;

            bool hasCandidates = AutoCandidateListView.Items.Count > 0;
            SelectionStatusTextBlock.Text = hasCandidates
                ? (string)Application.Current.Resources["SteamExePathNoSelectionStatus"]
                : (string)Application.Current.Resources["SteamExePathNoAutoCandidatesStatus"];
        }

        private static string FormatResource(string key, string value)
        {
            string template = (string)Application.Current.Resources[key];
            return template.Replace("*", string.IsNullOrWhiteSpace(value) ? "-" : value);
        }

        private sealed class SteamExeCandidateRow
        {
            public SteamExeCandidateRow(SteamExeCandidate candidate)
            {
                Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
            }

            public SteamExeCandidate Candidate { get; private set; }

            public string Path
            {
                get { return Candidate.Path; }
            }

            public string SourceText
            {
                get { return "(" + Candidate.Source + ")"; }
            }
        }
    }
}
