//#define INJECT_SPECIFIED_LANG

using SpsLogic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SpsGui.Models
{
    public interface IConductor
    {
        /// <summary>
        /// Gets the packet scanner shared with the current Steam peer manager.
        /// </summary>
        IPacketScan PacketScan { get; }

        /// <summary>
        /// Creates a Steam monitor interpreter for the selected application.
        /// </summary>
        /// <param name="info">Selected Steam application information. Must not be null.</param>
        /// <returns>An interpreter that owns the SteamMonitor child process.</returns>
        ISteamMonitorInterpreter CreateSteamMonitorInterpreter(SteamAppInfo info);

        /// <summary>
        /// To observer steam p2p connection, we have to observe ipc log file.
        /// Check if it already exists and if not open the steam console and input a command.
        /// </summary>
        /// <returns></returns>
        Task<bool> AutoSteamConsoleAsync();

        /// <summary>
        /// Manual version of <c>AutoSteamConsoleAsync()</c>
        /// </summary>
        /// <returns>-1 for error, 0 for none, 1 for success</returns>
        int RequestSteamConsole();

        /// <summary>
        /// Gets the Steam console command that enables IPC logging.
        /// </summary>
        string SteamConsoleCommand { get; }
    }

    public class Conductor : IConductor
    {
        private const string STEAM_COMMAND = "log_ipc \"BeginAuthSession,EndAuthSession,LeaveLobby,SendClanChatMessage\"";
        private const int OpenConsoleDelayMilliseconds = 1500;
        private const int ClipboardDelayMilliseconds = 150;
        private const int ClipboardRestoreDelayMilliseconds = 700;
        private const int ClipboardRetryCount = 50;
        private const int ClipboardRetryDelayMilliseconds = 100;
        private const int SW_RESTORE = 9;
        private static readonly string[] SteamProcessNames = { "steam", "steamwebhelper" };
        private readonly IPacketScan packetScan;

        public Conductor(IPacketScan packetScan)
        {
            this.packetScan = packetScan ?? throw new ArgumentNullException(nameof(packetScan));

            string culture = CultureInfo.CurrentCulture.Name;
            try
            {
                string local = CultureInfo.CreateSpecificCulture(culture).Name;
                var localDictionary = new ResourceDictionary()
                {
#if INJECT_SPECIFIED_LANG
                    Source = new Uri(@"Resources/StringResource." + "en-us" + @".xaml", UriKind.Relative)
#else
                    Source = new Uri(@"Resources/StringResource." + local + @".xaml", UriKind.Relative)
#endif
                };
                App.Current.Resources.MergedDictionaries.Add(localDictionary);
            }
            catch (CultureNotFoundException)
            { // for CultureInfo.CreateSpecificCulture
                Logger.Log(culture + " has no info so failed to load the localization");
            }
            catch
            {
                Logger.Log($"Failed to find local dictionary for {culture} so use default en-us.");
            }
        }

        /// <summary>
        /// Gets the packet scanner shared with the current profiling session.
        /// </summary>
        public IPacketScan PacketScan
        {
            get
            {
                return packetScan;
            }
        }

        /// <summary>
        /// Gets the Steam console command that enables IPC logging.
        /// </summary>
        public string SteamConsoleCommand
        {
            get
            {
                return STEAM_COMMAND;
            }
        }

        /// <summary>
        /// Creates a Steam monitor interpreter and prepares the packet scanner for profiling.
        /// </summary>
        /// <param name="info">Selected Steam application information. Must not be null.</param>
        /// <returns>A Steam monitor interpreter using the conductor-owned packet scanner.</returns>
        public ISteamMonitorInterpreter CreateSteamMonitorInterpreter(SteamAppInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            return new SpsSteamMonitorInterpreter(packetScan, info);
        }
        
        public async Task<bool> AutoSteamConsoleAsync()
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Logger.DebugLog("Wait ui thread");
                return await Application.Current.Dispatcher.Invoke(() => AutoSteamConsoleAsync());
            }

            if (!MustEnterSteamCommand())
            {
                Logger.DebugLog("Skip command because steam already has it");
                return true;
            }

            try
            {
                Logger.Log("Start auto inpnut");
                Process.Start(new ProcessStartInfo("steam://open/console") { UseShellExecute = true });
                await Task.Delay(OpenConsoleDelayMilliseconds);
                if (!TryActivateSteamWindow())
                {
                    Logger.Log("Failed to activate Steam window. Steam console command was not sent.", true);
                    return false;
                }
                await Task.Delay(ClipboardDelayMilliseconds);
                return PasteSteamConsoleCommand();
            }
            catch (Exception e)
            {
                Logger.Log("Failed to open Steam console or enter command because " + e.Message, true);
                return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Sucess or failure</returns>
        public int RequestSteamConsole()
        {
            if (!MustEnterSteamCommand())
                return 0;

            try
            {
                Process.Start(new ProcessStartInfo("steam://open/console"));
            }
            catch (Exception e)
            {
                Logger.Log("Failed to open Steam console because " + e.Message, true);
                return -1;
            }
            return 1;
        }

        private bool MustEnterSteamCommand()
        {
            DateTime ipcLogDate;
            String startupDateString = null;

            if (!File.Exists(AppConfig.Instance.SteamLogPath))
                return true;

            try
            {
                ipcLogDate = File.GetLastWriteTime(AppConfig.Instance.SteamLogPath);

            }
            catch (Exception)
            {
                return true;
            }

            try
            {
                using (FileStream stream = new FileStream(AppConfig.Instance.SteamBootstrapLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var reader = new ReverseTextReader(stream, Encoding.UTF8);
                    var today = DateTime.Today;
                    int dateCheckCountdown = 20;
                    while (!reader.EndOfStream)
                    {
                        String line = reader.ReadLine();
                        if (line.Trim().Length == 0)
                            continue;
                        else if (line.Contains("Startup - updater built"))
                        {
                            int substringStartIndex = line.IndexOf("[") + 1;
                            startupDateString = line.Substring(substringStartIndex, line.IndexOf("]") - substringStartIndex);
                            break;
                        }
                        else
                        {
                            if (--dateCheckCountdown == 0)
                            {
                                int substringStartIndex = line.IndexOf("[") + 1;
                                DateTime lineDate = DateTime.Parse(line.Substring(substringStartIndex, line.IndexOf("]") - substringStartIndex));
                                if (today.Subtract(lineDate).TotalHours > 24)
                                    return true; // let's assume Steam hasn't been running for 24+ hours
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                return true;
            }

            return startupDateString == null || DateTime.Parse(startupDateString) > ipcLogDate;
        }

        private static bool PasteSteamConsoleCommand()
        {
            Logger.DebugLog("Called paste steam console command");
            IDataObject previousClipboard = null;
            bool shouldRestoreClipboard = TryGetClipboardData(out previousClipboard);

            try
            {
                if (!TrySetClipboardText(STEAM_COMMAND))
                {
                    Logger.DebugLog("Failed to set cllipboard text");
                    return false;
                }

                if (!TryActivateSteamWindow())
                {
                    Logger.Log("Failed to activate Steam window before sending keys.", true);
                    return false;
                }

                return SendPasteAndEnter();
            }
            finally
            {
                Task.Delay(ClipboardRestoreDelayMilliseconds).Wait();
                if (shouldRestoreClipboard)
                {
                    TryRestoreClipboardData(previousClipboard);
                }
            }
        }

        private static bool TryGetClipboardData(out IDataObject data)
        {
            data = null;
            ExternalException lastException = null;
            for (int i = 0; i < ClipboardRetryCount; i++)
            {
                try
                {
                    data = Clipboard.GetDataObject();
                    return data != null;
                }
                catch (ExternalException e)
                {
                    lastException = e;
                    Task.Delay(ClipboardRetryDelayMilliseconds).Wait();
                }
            }

            if (lastException != null)
            {
                Logger.Log("Failed to get clipboard data because " + lastException.Message, true);
            }

            return false;
        }

        private static bool TrySetClipboardText(string text)
        {
            ExternalException lastException = null;
            for (int i = 0; i < ClipboardRetryCount; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (ExternalException e)
                {
                    lastException = e;
                    Task.Delay(ClipboardRetryDelayMilliseconds).Wait();
                }
            }

            if (lastException != null)
            {
                Logger.Log("Failed to set clipboard text because " + lastException.Message, true);
            }

            return false;
        }

        private static void TryRestoreClipboardData(IDataObject data)
        {
            try
            {
                Logger.DebugLog("Try restore clipboard data");
                Clipboard.SetDataObject(data, true);
            }
            catch (ExternalException e)
            {
                Logger.Log("Failed to restore clipboard because " + e.Message, true);
            }
        }

        private static bool SendPasteAndEnter()
        {
            try
            {
                Logger.DebugLog("Send Steam console input with SendKeys.");
                Forms.SendKeys.SendWait("^v");
                Task.Delay(ClipboardDelayMilliseconds).Wait();
                Forms.SendKeys.SendWait("{ENTER}");
                return true;
            }
            catch (Exception e)
            {
                Logger.Log("Failed to send Steam console input with SendKeys because " + e.Message, true);
                return false;
            }
        }

        private static bool TryActivateSteamWindow()
        {
            if (IsSteamForegroundWindow())
            {
                return true;
            }

            foreach (string processName in SteamProcessNames)
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        IntPtr windowHandle = process.MainWindowHandle;
                        if (windowHandle == IntPtr.Zero)
                        {
                            continue;
                        }

                        WinApi.ShowWindow(windowHandle, SW_RESTORE);
                        WinApi.SetForegroundWindow(windowHandle);
                        Task.Delay(ClipboardDelayMilliseconds).Wait();
                        if (IsSteamForegroundWindow())
                        {
                            return true;
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log("Failed to activate Steam window because " + e.Message, true);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            return false;
        }

        private static bool IsSteamForegroundWindow()
        {
            IntPtr foregroundWindow = WinApi.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return false;
            }

            uint processId;
            WinApi.GetWindowThreadProcessId(foregroundWindow, out processId);
            if (processId == 0)
            {
                return false;
            }

            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    return SteamProcessNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception e)
            {
                Logger.Log("Failed to inspect foreground window because " + e.Message, true);
                return false;
            }
        }
    }
}
