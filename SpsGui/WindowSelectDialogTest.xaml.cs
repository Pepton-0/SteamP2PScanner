using MahApps.Metro.Controls;
using SpsLogic;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace SpsGui
{
    public partial class WindowSelectDialogTest : MetroWindow, INotifyPropertyChanged
    {
        private WindowRow selectedWindowRow;
        private string selectionStatus = "No window selected";

        public WindowSelectDialogTest(SteamAppFinder.WindowInfo[] windows)
        {
            InitializeComponent();
            DataContext = this;

            foreach (SteamAppFinder.WindowInfo window in (windows ?? Array.Empty<SteamAppFinder.WindowInfo>())
                .OrderBy(w => w.ProcessName)
                .ThenBy(w => w.Title))
            {
                Windows.Add(new WindowRow(window));
            }
        }

        public ObservableCollection<WindowRow> Windows { get; } = new ObservableCollection<WindowRow>();

        public WindowRow SelectedWindowRow
        {
            get { return selectedWindowRow; }
            set
            {
                if (selectedWindowRow == value)
                {
                    return;
                }

                selectedWindowRow = value;
                SelectedWindow = selectedWindowRow?.Info;
                SelectionStatus = selectedWindowRow == null
                    ? "No window selected"
                    : $"{selectedWindowRow.Title} / PID {selectedWindowRow.ProcessId}";
                OkButton.IsEnabled = selectedWindowRow != null;
                RaisePropertyChanged();
            }
        }

        public string SelectionStatus
        {
            get { return selectionStatus; }
            private set
            {
                if (selectionStatus == value)
                {
                    return;
                }

                selectionStatus = value;
                RaisePropertyChanged();
            }
        }

        public SteamAppFinder.WindowInfo SelectedWindow { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWindow == null)
            {
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void WindowsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedWindow != null)
            {
                DialogResult = true;
            }
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class WindowRow
    {
        public WindowRow(SteamAppFinder.WindowInfo info)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
        }

        public SteamAppFinder.WindowInfo Info { get; private set; }

        public string Title
        {
            get { return string.IsNullOrWhiteSpace(Info.Title) ? "-" : Info.Title; }
        }

        public string ProcessName
        {
            get { return Info.ProcessName; }
        }

        public string ProcessPath
        {
            get { return string.IsNullOrWhiteSpace(Info.ProcessPath) ? "-" : Info.ProcessPath; }
        }

        public uint ProcessId
        {
            get { return Info.ProcessId; }
        }

        public uint ThreadId
        {
            get { return Info.ThreadId; }
        }

        public string HandleText
        {
            get { return "0x" + Info.Handle.ToInt64().ToString("X", CultureInfo.InvariantCulture); }
        }
    }
}
