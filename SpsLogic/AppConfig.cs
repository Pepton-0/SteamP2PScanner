using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace SpsLogic
{
    public class AppConfig : INotifyPropertyChanged
    {
        private const double DefaultPacketPatienceLimitMs = 3000;

        public static AppConfig Instance { get; private set; }

        /// <summary>
        /// Gets the packet loss patience limit used by the default packet scanner constructor.
        /// </summary>
        public static double PacketPatienceLimitMs
        {
            get
            {
                return Instance == null
                    ? DefaultPacketPatienceLimitMs
                    : Instance.PacketPatienceMs;
            }
        }

        private static readonly string path = @"config\\app_config.json";

        #region steam exe and related paths
        [JsonProperty("steam_exe")]
        public string SteamExe
        {
            get { return _steamExe; }
            set
            {
                _steamExe = value ?? string.Empty;
                SteamProcessName = Path.GetFileNameWithoutExtension(_steamExe);
                SteamLogDir = CreateSteamLogDirFromSteamExe(_steamExe);
                Save();
                RaisePropertyChanged();
            }
        }
        private string _steamExe = "C:\\Program Files (x86)\\Steam\\steam.exe";

        [JsonIgnore]
        public string SteamProcessName
        {
            get { return _steamProcessName; }
            private set { _steamProcessName = value; RaisePropertyChanged(); }
        }
        private string _steamProcessName = "steam";


        [JsonProperty("steam_log_dir")]
        public string SteamLogDir
        {
            get { return _steamLogDir; }
            set
            {
                _steamLogDir = value ?? string.Empty;
                string logDir = SteamLogDir;
                if (string.IsNullOrEmpty(logDir))
                {
                    SteamLogPath = string.Empty;
                    SteamBootstrapLogPath = string.Empty;
                }
                else
                {
                    if (!logDir.EndsWith("\\")) { logDir += "\\"; }
                    SteamLogPath = logDir + "ipc_SteamClient.log";
                    SteamBootstrapLogPath = logDir + "bootstrap_log.txt";
                }
                Save();
                RaisePropertyChanged();
            }
        }
        private string _steamLogDir = "C:\\Program Files (x86)\\Steam\\logs\\";

        [JsonIgnore]
        public string SteamLogPath
        {
            get { return _steamLogPath; }
            private set { _steamLogPath = value; RaisePropertyChanged(); }
        }
        private string _steamLogPath = "C:\\Program Files (x86)\\Steam\\logs\\ipc_SteamClient.log";

        [JsonIgnore]
        public string SteamBootstrapLogPath
        {
            get { return _steamBootstrapLogPath; }
            set { _steamBootstrapLogPath = value; RaisePropertyChanged(); }
        }
        private string _steamBootstrapLogPath = "C:\\Program Files (x86)\\Steam\\logs\\bootstrap_log.txt";
        #endregion

        [JsonProperty("show_boxplot")]
        public bool ShowBoxPlot
        {
            get { return _showBoxPlot; }
            set
            {
                _showBoxPlot = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private bool _showBoxPlot = true;

        [JsonProperty("overlay_enabled")]
        public bool OverlayEnabled
        {
            get { return _overlayEnabled; }
            set
            {
                _overlayEnabled = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private bool _overlayEnabled = true;

        [JsonProperty("overlay_show_name")]
        public bool OverlayShowName
        {
            get { return _overlayShowName; }
            set
            {
                _overlayShowName = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private bool _overlayShowName = true;

        [JsonProperty("overlay_show_status")]
        public bool OverlayShowStatus
        {
            get { return _overlayShowStatus; }
            set
            {
                _overlayShowStatus = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private bool _overlayShowStatus = true;

        [JsonProperty("overlay_show_average")]
        public bool OverlayShowAverage
        {
            get { return _overlayShowAverage; }
            set
            {
                _overlayShowAverage = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private bool _overlayShowAverage = true;

        [JsonProperty("overlay_show_loss")]
        public bool OverlayShowLoss
        {
            get { return _overlayShowLoss; }
            set
            {
                _overlayShowLoss = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private bool _overlayShowLoss = true;

        [JsonProperty("overlay_show_chart")]
        public bool OverlayShowChart
        {
            get { return _overlayShowChart; }
            set
            {
                _overlayShowChart = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private bool _overlayShowChart = true;

        [JsonProperty("overlay_offset_x")]
        public double OverlayOffsetX
        {
            get { return _overlayOffsetX; }
            set
            {
                _overlayOffsetX = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private double _overlayOffsetX;

        [JsonProperty("overlay_offset_y")]
        public double OverlayOffsetY
        {
            get { return _overlayOffsetY; }
            set
            {
                _overlayOffsetY = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private double _overlayOffsetY;

        [JsonProperty("core_window_placement_saved")]
        public bool CoreWindowPlacementSaved
        {
            get { return _coreWindowPlacementSaved; }
            set
            {
                _coreWindowPlacementSaved = value;
                RaisePropertyChanged();
            }
        }
        private bool _coreWindowPlacementSaved;

        [JsonProperty("core_window_display_index")]
        public int CoreWindowDisplayIndex
        {
            get { return _coreWindowDisplayIndex; }
            set
            {
                _coreWindowDisplayIndex = value;
                RaisePropertyChanged();
            }
        }
        private int _coreWindowDisplayIndex = -1;

        [JsonProperty("core_window_left")]
        public double CoreWindowLeft
        {
            get { return _coreWindowLeft; }
            set
            {
                _coreWindowLeft = value;
                RaisePropertyChanged();
            }
        }
        private double _coreWindowLeft;

        [JsonProperty("core_window_top")]
        public double CoreWindowTop
        {
            get { return _coreWindowTop; }
            set
            {
                _coreWindowTop = value;
                RaisePropertyChanged();
            }
        }
        private double _coreWindowTop;

        [JsonProperty("core_window_width")]
        public double CoreWindowWidth
        {
            get { return _coreWindowWidth; }
            set
            {
                _coreWindowWidth = value;
                RaisePropertyChanged();
            }
        }
        private double _coreWindowWidth;

        [JsonProperty("core_window_height")]
        public double CoreWindowHeight
        {
            get { return _coreWindowHeight; }
            set
            {
                _coreWindowHeight = value;
                RaisePropertyChanged();
            }
        }
        private double _coreWindowHeight;

        [JsonProperty("dns_ip")]
        public string DnsIp
        {
            get { return _dnsIp; }
            set
            {
                _dnsIp = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private string _dnsIp = "8.8.8.8";

        [JsonProperty("packet_patience_ms")]
        public double PacketPatienceMs
        {
            get { return _packetPatienceMs; }
            set
            {
                if (value <= 0)
                {
                    return;
                }

                _packetPatienceMs = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private double _packetPatienceMs = DefaultPacketPatienceLimitMs;

        [JsonProperty("auto_ipc")]
        public bool AutoIpc
        {
            get { return _autoIpc; }
            set
            {
                _autoIpc = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private bool _autoIpc = true;

        [JsonProperty("ignore_latest")]
        public bool IgnoreLatest
        {
            get { return _ignoreLatest; }
            set
            {
                _ignoreLatest = value;
                Save();
                RaisePropertyChanged();
            }
        }
        private bool _ignoreLatest;

        /// <summary>Whether SpsLauncher starts at logon. Changing it updates the logon task and starts or stops SpsLauncher.</summary>
        [JsonIgnore]
        public bool AutoRun
        {
            get { return _autoRun; }
            set
            {
                if (_autoRun == value)
                {
                    return;
                }

                _autoRun = value;
                Save();
                string spsDirectory;
                if (TryGetManagedSpsDirectory(out spsDirectory))
                {
                    SyncTaskAndLauncher(spsDirectory);
                }

                RaisePropertyChanged();
            }
        }
        // Serialized through the field so that json deserialization does not touch the task or launcher.
        [JsonProperty("auto_run")]
        private bool _autoRun = true;

        /// <summary>Installs SpsLauncher and WinDivert into Program Files and syncs the logon task and SpsLauncher.
        /// Call once at SpsGui startup, before WinDivert is first opened.</summary>
        public void InstallAndSyncStartup(string version)
        {
            string spsDirectory;
            if (!TryGetManagedSpsDirectory(out spsDirectory))
            {
                return;
            }

            TimeSpan start = Logger.GetTimestamp();
            StartupTask.Install(spsDirectory, version);

            SyncTaskAndLauncher(spsDirectory);
            Logger.Log("InstallAndSyncStartup total: " + Logger.GetElapsedMillsec(start) + " ms", true);
        }

        /// <summary>Updates the logon task mode and starts or stops SpsLauncher to match <see cref="AutoRun"/>.</summary>
        private void SyncTaskAndLauncher(string spsDirectory)
        {
            StartupTask.SyncTask(spsDirectory, _autoRun);
            if (_autoRun)
            {
                if (!StartupTask.IsLauncherRunning())
                {
                    // Another SpsLauncher holding the single-instance mutex would make the new one exit at once.
                    StartupTask.StopLaunchers();
                    StartupTask.StartLauncher(spsDirectory);
                }
            }
            else
            {
                StartupTask.StopLaunchers();
            }
        }

        /// <summary>Only SpsGui manages SpsLauncher. SteamMonitor and dev builds also load AppConfig, but get false.</summary>
        private static bool TryGetManagedSpsDirectory(out string spsDirectory)
        {
            spsDirectory = AppDomain.CurrentDomain.BaseDirectory;
            using (Process process = Process.GetCurrentProcess())
            {
                return string.Equals(process.ProcessName, "SpsGui", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(spsDirectory, StartupTask.SpsGuiExeName));
            }
        }

        static AppConfig()
        {
            LoadOrCreate();
        }

        public static bool LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(path);

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(path))
            {
                Instance = new AppConfig();
                Instance.Save();
                return true;
            }
            else
            {
                string json = File.ReadAllText(path);
                Instance = JsonConvert.DeserializeObject<AppConfig>(json);
                return false;
            }
        }

        public void Save()
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        private static string CreateSteamLogDirFromSteamExe(string steamExe)
        {
            try
            {
                string steamDir = Path.GetDirectoryName(steamExe);
                return string.IsNullOrEmpty(steamDir) ? string.Empty : Path.Combine(steamDir, "logs");
            }
            catch
            {
                return string.Empty;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
