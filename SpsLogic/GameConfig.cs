using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SpsLogic
{
    public class GameConfig
    {
        public class GameInfo
        {
            [JsonProperty("process_paths")]
            private string[] processPaths = new string[0];

            [JsonProperty("steam_app_ids")]
            private string[] steamAppIds = new string[0];

            /// <summary>
            /// Get corresponding steam app id by process path. If not found, return null.
            /// </summary>
            /// <param name="processPath">full path to exe</param>
            /// <returns></returns>
            public string this[string processPath]
            {
                get
                {
                    EnsureArrays();
                    var idx = Array.IndexOf(processPaths, processPath);
                    if(idx < 0 || idx >= steamAppIds.Length)
                    {
                        return null;
                    }
                    else
                    {
                        return steamAppIds[idx];
                    }
                }

                set
                {
                    EnsureArrays();
                    var idx = Array.IndexOf(processPaths, processPath);
                    if (idx < 0)
                    {
                        Array.Resize(ref processPaths, processPaths.Length + 1);
                        Array.Resize(ref steamAppIds, steamAppIds.Length + 1);
                        idx = processPaths.Length - 1;
                        processPaths[idx] = processPath;
                    }
                    steamAppIds[idx] = value;
                    Instance.Save();
                    Instance.RaisePropertyChanged(nameof(RegisteredGames));
                }
            }

            private void EnsureArrays()
            {
                if (processPaths == null)
                {
                    processPaths = new string[0];
                }

                if (steamAppIds == null)
                {
                    steamAppIds = new string[0];
                }
            }
        }

        public static GameConfig Instance { get; private set; }
        private static readonly string path = @"config\\game_config.json";

        /// <summary>
        /// process path to steam app id
        /// </summary>
        [JsonProperty("registered_games")]
        public GameInfo RegisteredGames { 
            get => _registeredGames; 
            private set => _registeredGames = value;
        }
        private GameInfo _registeredGames = new GameInfo();

        static GameConfig()
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
                Instance = new GameConfig();
                Instance.Save();
                return true;
            }
            else
            {
                string json = File.ReadAllText(path);
                Instance = JsonConvert.DeserializeObject<GameConfig>(json);
                return false;
            }
        }

        public void Save()
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
