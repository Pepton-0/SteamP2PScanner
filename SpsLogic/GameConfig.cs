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

            /// <summary>
            /// Copies registered pairs into a case-insensitive dictionary. Entries without a path or app id are skipped.
            /// </summary>
            internal Dictionary<string, string> CreateSnapshot()
            {
                EnsureArrays();
                var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int count = Math.Min(processPaths.Length, steamAppIds.Length);
                for (int i = 0; i < count; i++)
                {
                    if (string.IsNullOrWhiteSpace(processPaths[i]) || string.IsNullOrWhiteSpace(steamAppIds[i]))
                    {
                        continue;
                    }

                    snapshot[processPaths[i]] = steamAppIds[i];
                }

                return snapshot;
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

        /// <summary>
        /// Relative path of the production config file. It is resolved against the current directory.
        /// </summary>
        public const string RelativePath = @"config\game_config.json";

        private static readonly string path = RelativePath;
        private static readonly object instanceLock = new object();
        private static GameConfig instance;

        /// <summary>Production config. Loaded or created on first access and written back on change.</summary>
        public static GameConfig Instance
        {
            get
            {
                lock (instanceLock)
                {
                    if (instance == null)
                    {
                        LoadOrCreate();
                    }

                    return instance;
                }
            }
            private set
            {
                lock (instanceLock)
                {
                    instance = value;
                }
            }
        }

        /// <summary>
        /// process path to steam app id
        /// </summary>
        [JsonProperty("registered_games")]
        public GameInfo RegisteredGames { 
            get => _registeredGames; 
            private set => _registeredGames = value;
        }
        private GameInfo _registeredGames = new GameInfo();

        public static bool LoadOrCreate()
        {
            lock (instanceLock)
            {
                var dir = Path.GetDirectoryName(path);

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (!File.Exists(path))
                {
                    instance = new GameConfig();
                    instance.Save();
                    return true;
                }
                else
                {
                    string json = File.ReadAllText(path);
                    instance = JsonConvert.DeserializeObject<GameConfig>(json);
                    return false;
                }
            }
        }

        /// <summary>Reads a detached snapshot. Never creates or writes files, and never touches <see cref="Instance"/>.</summary>
        /// <returns>An empty snapshot when the file does not exist.</returns>
        /// <exception cref="JsonException">The file is invalid, e.g. SpsGui is rewriting it right now.</exception>
        public static ReadOnlyGameConfig LoadReadOnly(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException("Config path must not be empty.", nameof(configPath));
            }

            if (!File.Exists(configPath))
            {
                return ReadOnlyGameConfig.Empty;
            }

            string json;
            // Share everything so that SpsGui can keep writing while this process reads.
            using (var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                json = reader.ReadToEnd();
            }

            // Deserialize into a detached object. Never call its indexer setter: it saves through Instance.
            var detached = JsonConvert.DeserializeObject<GameConfig>(json);
            if (detached == null || detached.RegisteredGames == null)
            {
                return ReadOnlyGameConfig.Empty;
            }

            return new ReadOnlyGameConfig(detached.RegisteredGames.CreateSnapshot());
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

    /// <summary>Immutable snapshot of game_config.json created by <see cref="GameConfig.LoadReadOnly"/>.</summary>
    public sealed class ReadOnlyGameConfig
    {
        public static readonly ReadOnlyGameConfig Empty =
            new ReadOnlyGameConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        private readonly Dictionary<string, string> processPathToSteamAppId;

        internal ReadOnlyGameConfig(Dictionary<string, string> processPathToSteamAppId)
        {
            this.processPathToSteamAppId = processPathToSteamAppId;
        }

        /// <summary>
        /// Gets registered process paths.
        /// </summary>
        public IEnumerable<string> ProcessPaths
        {
            get { return processPathToSteamAppId.Keys; }
        }

        /// <summary>
        /// Gets the number of registered games.
        /// </summary>
        public int Count
        {
            get { return processPathToSteamAppId.Count; }
        }

        /// <summary>Finds the steam app id registered for the full exe path, ignoring case.</summary>
        public bool TryGetSteamAppId(string processPath, out string steamAppId)
        {
            steamAppId = null;
            return processPath != null && processPathToSteamAppId.TryGetValue(processPath, out steamAppId);
        }
    }
}
