using SpsLogic;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;

namespace SpsGui
{
    /// <summary>
    /// Manual test window for FindSteamExeService.
    /// </summary>
    public partial class FindSteamExeServiceTest : Window
    {
        private readonly IFindSteamExeService service;
        private string bestPath;

        public FindSteamExeServiceTest()
            : this(new FindSteamExeService())
        {
        }

        public FindSteamExeServiceTest(IFindSteamExeService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            InitializeComponent();
            DataContext = this;
            AppendLog("FindSteamExeServiceTest initialized.");
        }

        /// <summary>
        /// Gets candidate rows shown by the test window.
        /// </summary>
        public ObservableCollection<FindSteamExeCandidateRow> CandidateRows { get; } =
            new ObservableCollection<FindSteamExeCandidateRow>();

        private void FindButton_Click(object sender, RoutedEventArgs e)
        {
            RunFind();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(bestPath))
            {
                return;
            }

            AppConfig.Instance.SteamExe = bestPath;
            AppendLog("AppConfig.SteamExe updated: " + bestPath);
        }

        /// <summary>
        /// Runs the service and refreshes the visual candidate list.
        /// </summary>
        private void RunFind()
        {
            try
            {
                FindSteamExeResult result = service.Find();
                bestPath = result.BestPath;

                CandidateRows.Clear();
                foreach (SteamExeCandidate candidate in result.Candidates)
                {
                    CandidateRows.Add(new FindSteamExeCandidateRow(candidate));
                }

                BestPathTextBlock.Text = string.IsNullOrWhiteSpace(bestPath) ? "-" : bestPath;
                ApplyButton.IsEnabled = !string.IsNullOrWhiteSpace(bestPath);

                AppendLog("Find completed. candidates=" + result.Candidates.Length);
                foreach (string message in result.Messages)
                {
                    AppendLog(message);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Find failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void AppendLog(string message)
        {
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] [UI:{Thread.CurrentThread.ManagedThreadId}] {message}{Environment.NewLine}");
            LogTextBox.ScrollToEnd();
        }
    }

    /// <summary>
    /// Presentation row for a steam.exe candidate.
    /// </summary>
    public sealed class FindSteamExeCandidateRow
    {
        public FindSteamExeCandidateRow(SteamExeCandidate candidate)
        {
            Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        }

        /// <summary>
        /// Gets the source candidate object.
        /// </summary>
        public SteamExeCandidate Candidate { get; private set; }

        /// <summary>
        /// Gets the candidate source text.
        /// </summary>
        public string Source
        {
            get { return Candidate.Source; }
        }

        /// <summary>
        /// Gets the process id text, or '-' for non-process candidates.
        /// </summary>
        public string ProcessIdText
        {
            get { return Candidate.ProcessId.HasValue ? Candidate.ProcessId.Value.ToString() : "-"; }
        }

        /// <summary>
        /// Gets the candidate steam.exe path.
        /// </summary>
        public string Path
        {
            get { return Candidate.Path; }
        }
    }
}
