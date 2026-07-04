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

namespace SpsGui.Models
{
    public interface IConductor
    {
        /// <summary>
        /// Gets the packet scanner shared with the current Steam peer manager.
        /// </summary>
        IPacketScan PacketScan { get; }

        /// <summary>
        /// Creates a Steam peer manager for the selected application.
        /// </summary>
        /// <param name="info">Selected Steam application information. Must not be null.</param>
        /// <returns>A manager initialized for the selected application and shared packet scanner.</returns>
        SteamPeerManager CreateSteamPeerManager(SteamAppInfo info);

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
        private const int ClipboardRetryCount = 10;
        private const int ClipboardRetryDelayMilliseconds = 50;
        private const uint INPUT_KEYBOARD = 1;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_V = 0x56;
        private const ushort VK_RETURN = 0x0D;
        private const uint KEYEVENTF_KEYUP = 0x0002;
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
                    Source = new Uri(@"Resources/StringResource." + local + @".xaml", UriKind.Relative)
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
        /// Creates a Steam peer manager and prepares the packet scanner for profiling.
        /// </summary>
        /// <param name="info">Selected Steam application information. Must not be null.</param>
        /// <returns>A Steam peer manager using the conductor-owned packet scanner.</returns>
        public SteamPeerManager CreateSteamPeerManager(SteamAppInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            if (!SteamPeerManager.InitializeSteamApi(info))
            {
                return null;
            }

            return new SteamPeerManager(packetScan, info.Info.ProcessName);
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
                PasteSteamConsoleCommand();
                return true;
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

        private static void PasteSteamConsoleCommand()
        {
            IDataObject previousClipboard = null;
            bool shouldRestoreClipboard = TryGetClipboardData(out previousClipboard);

            try
            {
                SetClipboardText(STEAM_COMMAND);
                SendPasteAndEnter();
            }
            finally
            {
                Task.Delay(ClipboardDelayMilliseconds).Wait();
                if (shouldRestoreClipboard)
                {
                    TryRestoreClipboardData(previousClipboard);
                }
            }
        }

        private static bool TryGetClipboardData(out IDataObject data)
        {
            data = null;
            for (int i = 0; i < ClipboardRetryCount; i++)
            {
                try
                {
                    data = Clipboard.GetDataObject();
                    return data != null;
                }
                catch (ExternalException)
                {
                    Task.Delay(ClipboardRetryDelayMilliseconds).Wait();
                }
            }

            return false;
        }

        private static void SetClipboardText(string text)
        {
            for (int i = 0; i < ClipboardRetryCount; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return;
                }
                catch (ExternalException)
                {
                    Task.Delay(ClipboardRetryDelayMilliseconds).Wait();
                }
            }

            Clipboard.SetText(text);
        }

        private static void TryRestoreClipboardData(IDataObject data)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
            }
            catch (ExternalException e)
            {
                Logger.Log("Failed to restore clipboard because " + e.Message, true);
            }
        }

        private static void SendPasteAndEnter()
        {
            INPUT[] inputs =
            {
                CreateKeyInput(VK_CONTROL, false),
                CreateKeyInput(VK_V, false),
                CreateKeyInput(VK_V, true),
                CreateKeyInput(VK_CONTROL, true),
                CreateKeyInput(VK_RETURN, false),
                CreateKeyInput(VK_RETURN, true)
            };

            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent != inputs.Length)
            {
                Logger.Log("Failed to send Steam console input. SendInput sent " + sent + " of " + inputs.Length + " events.", true);
            }
        }

        private static INPUT CreateKeyInput(ushort virtualKey, bool keyUp)
        {
            return new INPUT
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = virtualKey,
                        Flags = keyUp ? KEYEVENTF_KEYUP : 0
                    }
                }
            };
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

                        ShowWindow(windowHandle, SW_RESTORE);
                        SetForegroundWindow(windowHandle);
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
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return false;
            }

            uint processId;
            GetWindowThreadProcessId(foregroundWindow, out processId);
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public KEYBDINPUT Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }
    }
}
