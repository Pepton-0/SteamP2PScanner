using Newtonsoft.Json;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace SpsLogic
{
    public class AppConfig : INotifyPropertyChanged
    {
        public static AppConfig Instance { get; private set; }
        private static readonly string path = @"config\\app_config.json";

        [JsonProperty("steam_exe")]
        public string SteamExe
        {
            get { return _steamExe; }
            set
            {
                if (File.Exists(value))
                {
                    _steamExe = value;
                    Save();
                    SteamProcessName = Path.GetFileNameWithoutExtension(_steamExe);
                    RaisePropertyChanged();
                }
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
                _steamLogDir = value;
                string logDir = SteamLogDir;
                if (!logDir.EndsWith("\\")) { logDir += "\\"; }
                SteamLogPath = logDir + "ipc_SteamClient.log";
                SteamBootstrapLogPath = logDir + "bootstrap_log.txt";
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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
