using SpsLogic;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace SpsGui.Models.Services
{
    public interface INotificationService
    {
        void Notify(string title, string message);

        void Close();
    }

    public sealed class NotificationService : INotificationService
    {
        private const int BalloonTimeoutMilliseconds = 10000;
        private Forms.NotifyIcon notifyIcon;

        /// <summary>Shows a Windows notification and flashes the main window's taskbar button.
        /// The flash also works when the user turned Windows notifications off.</summary>
        public void Notify(string title, string message)
        {
            ShowBalloon(title, message);
            FlashMainWindow();
        }

        public void Close()
        {
            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
                notifyIcon = null;
            }
        }

        private void ShowBalloon(string title, string message)
        {
            try
            {
                if (notifyIcon == null)
                {
                    notifyIcon = new Forms.NotifyIcon
                    {
                        Icon = AppIcon.Load(Forms.SystemInformation.SmallIconSize),
                        Text = "SteamP2PScanner",
                    };
                }

                notifyIcon.Visible = true;
                notifyIcon.ShowBalloonTip(BalloonTimeoutMilliseconds, title, message, Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to show a notification: " + ex.GetType().Name + ": " + ex.Message, true);
            }
        }

        private static void FlashMainWindow()
        {
            try
            {
                Window window = Application.Current == null ? null : Application.Current.MainWindow;
                IntPtr handle = window == null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                var info = new WinApi.FLASHWINFO
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(WinApi.FLASHWINFO)),
                    hwnd = handle,
                    dwFlags = WinApi.FLASHW_ALL | WinApi.FLASHW_TIMERNOFG,
                };
                WinApi.FlashWindowEx(ref info);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to flash the main window: " + ex.GetType().Name + ": " + ex.Message, true);
            }
        }
    }
}
