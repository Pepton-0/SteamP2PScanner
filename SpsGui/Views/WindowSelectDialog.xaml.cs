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

namespace SpsGui.Views
{
    /// <summary>
    /// Lets the user select one visible top-level window.
    /// </summary>
    public partial class WindowSelectDialog : MetroWindow, INotifyPropertyChanged
    {
        private WindowRow selectedWindowRow;
        private string selectionStatus = "No window selected";

        /// <summary>
        /// Initializes the dialog with visible windows.
        /// </summary>
        /// <param name="windows">Visible windows to show. Null is treated as an empty list.</param>
        public WindowSelectDialog(WindowInfo[] windows)
        {
            InitializeComponent();
            DataContext = this;

            foreach (WindowInfo window in (windows ?? Array.Empty<WindowInfo>())
                .OrderBy(w => w.ProcessName)
                .ThenBy(w => w.Title))
            {
                Windows.Add(new WindowRow(window));
            }
        }

        /// <summary>
        /// Gets rows shown by the dialog.
        /// </summary>
        public ObservableCollection<WindowRow> Windows { get; } = new ObservableCollection<WindowRow>();

        /// <summary>
        /// Gets or sets the selected window row.
        /// </summary>
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
                SelectedWindow = selectedWindowRow == null ? null : selectedWindowRow.Info;
                SelectionStatus = selectedWindowRow == null
                    ? "No window selected"
                    : selectedWindowRow.Title;
                OkButton.IsEnabled = selectedWindowRow != null;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Gets selection state text.
        /// </summary>
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

        /// <summary>
        /// Gets the selected raw window information after OK is pressed.
        /// </summary>
        public WindowInfo SelectedWindow { get; private set; }

        /// <summary>
        /// Raised when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedWindow != null)
            {
                DialogResult = true;
            }
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

    /// <summary>
    /// Adapts window information for grid display.
    /// </summary>
    public sealed class WindowRow
    {
        /// <summary>
        /// Initializes a row.
        /// </summary>
        /// <param name="info">Window information to expose. Must not be null.</param>
        public WindowRow(WindowInfo info)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
        }

        /// <summary>
        /// Gets the underlying window information.
        /// </summary>
        public WindowInfo Info { get; private set; }

        /// <summary>
        /// Gets the window title or a placeholder when empty.
        /// </summary>
        public string Title
        {
            get { return string.IsNullOrWhiteSpace(Info.Title) ? "-" : Info.Title; }
        }

        /// <summary>
        /// Gets the owning process name.
        /// </summary>
        public string ProcessName
        {
            get { return Info.ProcessName; }
        }

        /// <summary>
        /// Gets the owning process executable path.
        /// </summary>
        public string ProcessPath
        {
            get { return string.IsNullOrWhiteSpace(Info.ProcessPath) ? "-" : Info.ProcessPath; }
        }

        /// <summary>
        /// Gets the owning process id.
        /// </summary>
        public uint ProcessId
        {
            get { return Info.ProcessId; }
        }

        /// <summary>
        /// Gets the hexadecimal window handle.
        /// </summary>
        public string HandleText
        {
            get { return "0x" + Info.Handle.ToInt64().ToString("X", CultureInfo.InvariantCulture); }
        }
    }
}
