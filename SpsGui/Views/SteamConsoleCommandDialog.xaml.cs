using MahApps.Metro.Controls;
using SpsLogic;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace SpsGui.Views
{
    /// <summary>
    /// Shows the Steam console command and requires copying before closing.
    /// </summary>
    public partial class SteamConsoleCommandDialog : MetroWindow
    {
        private const int ClipboardRetryCount = 50;
        private const int ClipboardRetryDelayMilliseconds = 100;
        private readonly string command;
        private bool canClose;

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
            IntPtr ownerHandle = new WindowInteropHelper(this).Handle;
            if (!TrySetClipboardText(command, ownerHandle))
            {
                MessageBox.Show(this, "Failed to copy the Steam console command.");
            }

            canClose = true;
            CloseButton.IsEnabled = true;
            CloseButton.Focus();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void MetroWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!canClose)
            {
                e.Cancel = true;
            }
        }

        private static bool TrySetClipboardText(string text, IntPtr ownerHandle)
        {
            Win32Exception lastException = null;

            for (int i = 0; i < ClipboardRetryCount; i++)
            {
                if (TrySetClipboardTextNative(text, ownerHandle, out lastException))
                {
                    return true;
                }

                Thread.Sleep(ClipboardRetryDelayMilliseconds);
            }

            if (lastException != null)
            {
                Logger.Log("Failed to copy Steam console command because " + lastException.Message, true);
            }

            return false;
        }

        private static bool TrySetClipboardTextNative(string text, IntPtr ownerHandle, out Win32Exception exception)
        {
            exception = null;
            IntPtr globalMemory = IntPtr.Zero;
            bool clipboardOpened = false;

            try
            {
                int byteCount = (text.Length + 1) * sizeof(char);
                globalMemory = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, new UIntPtr((uint)byteCount));
                if (globalMemory == IntPtr.Zero)
                {
                    exception = new Win32Exception(Marshal.GetLastWin32Error());
                    return false;
                }

                IntPtr lockedMemory = GlobalLock(globalMemory);
                if (lockedMemory == IntPtr.Zero)
                {
                    exception = new Win32Exception(Marshal.GetLastWin32Error());
                    return false;
                }

                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, lockedMemory, text.Length);
                }
                finally
                {
                    GlobalUnlock(globalMemory);
                }

                if (!OpenClipboard(ownerHandle))
                {
                    exception = new Win32Exception(Marshal.GetLastWin32Error());
                    return false;
                }

                clipboardOpened = true;
                if (!EmptyClipboard())
                {
                    exception = new Win32Exception(Marshal.GetLastWin32Error());
                    return false;
                }

                if (SetClipboardData(CF_UNICODETEXT, globalMemory) == IntPtr.Zero)
                {
                    exception = new Win32Exception(Marshal.GetLastWin32Error());
                    return false;
                }

                globalMemory = IntPtr.Zero;
                return true;
            }
            finally
            {
                if (clipboardOpened)
                {
                    CloseClipboard();
                }

                if (globalMemory != IntPtr.Zero)
                {
                    GlobalFree(globalMemory);
                }
            }
        }

        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint GMEM_ZEROINIT = 0x0040;
        private const uint CF_UNICODETEXT = 13;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);
    }
}
