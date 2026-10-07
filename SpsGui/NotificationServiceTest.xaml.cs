using Microsoft.Win32;
using SpsGui.Models.Services;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SpsGui
{
    /// <summary>
    /// Manual test window for NotificationService. Logger output is shown in the log box.
    /// </summary>
    public partial class NotificationServiceTest : Window
    {
        private readonly INotificationService service = new NotificationService();
        private readonly TraceListener logListener;
        private Forms.NotifyIcon rawIcon;

        public NotificationServiceTest()
        {
            InitializeComponent();
            logListener = new DelegateTraceListener(AppendLog);
            Trace.Listeners.Add(logListener);
            Closed += (s, e) => CleanUp();
            AppendLog("NotificationServiceTest initialized.");
            CheckWindowsSettings();
        }

        private void NotifyNowButton_Click(object sender, RoutedEventArgs e)
        {
            NotifyWithService();
        }

        private async void NotifyLaterButton_Click(object sender, RoutedEventArgs e)
        {
            int seconds;
            if (!int.TryParse(DelayTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds) || seconds < 0)
            {
                AppendLog("Delay must be a non-negative integer.");
                return;
            }

            AppendLog("Notify after " + seconds + " sec. Switch to the game now.");
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            NotifyWithService();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            service.Close();
            AppendLog("Service icon closed.");
        }

        private void RawNotifyButton_Click(object sender, RoutedEventArgs e)
        {
            // Bypasses the service to see whether Windows shows the balloon at all.
            if (rawIcon == null)
            {
                rawIcon = new Forms.NotifyIcon
                {
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(Forms.Application.ExecutablePath),
                    Text = "Notification test (raw)",
                };
                rawIcon.BalloonTipShown += (s, args) => AppendLog("Raw: BalloonTipShown");
                rawIcon.BalloonTipClosed += (s, args) => AppendLog("Raw: BalloonTipClosed");
                rawIcon.BalloonTipClicked += (s, args) => AppendLog("Raw: BalloonTipClicked");
            }

            rawIcon.Visible = true;
            rawIcon.ShowBalloonTip(10000, TitleTextBox.Text, MessageTextBox.Text, Forms.ToolTipIcon.Info);
            AppendLog("Raw: ShowBalloonTip called.");
        }

        private void CheckSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            CheckWindowsSettings();
        }

        private void NotifyWithService()
        {
            service.Notify(TitleTextBox.Text, MessageTextBox.Text);
            AppendLog("Service: Notify called.");
        }

        /// <summary>0 in either value means Windows notifications are turned off for this user.</summary>
        private void CheckWindowsSettings()
        {
            AppendLog("ToastEnabled = " + ReadUserDword(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled"));
            AppendLog("NOC_GLOBAL_SETTING_TOASTS_ENABLED = " +
                ReadUserDword(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED"));
            AppendLog("Do Not Disturb (incl. auto on while gaming) is not readable here; check Settings > System > Notifications.");
        }

        private static string ReadUserDword(string keyPath, string valueName)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
            {
                object value = key == null ? null : key.GetValue(valueName);
                return value == null ? "(missing, default on)" : value.ToString();
            }
        }

        private void AppendLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendLog(message)));
                return;
            }

            LogTextBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss.ff", CultureInfo.InvariantCulture) + "] " + message.TrimEnd() + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }

        private void CleanUp()
        {
            Trace.Listeners.Remove(logListener);
            service.Close();
            if (rawIcon != null)
            {
                rawIcon.Visible = false;
                rawIcon.Dispose();
            }
        }

        private sealed class DelegateTraceListener : TraceListener
        {
            private readonly Action<string> write;

            public DelegateTraceListener(Action<string> write)
            {
                this.write = write;
            }

            public override void Write(string message)
            {
                write(message);
            }

            public override void WriteLine(string message)
            {
                write(message);
            }
        }
    }
}
