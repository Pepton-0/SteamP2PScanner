using Newtonsoft.Json;
using System.ComponentModel;
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
