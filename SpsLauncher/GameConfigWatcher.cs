using SpsLogic;
using System;
using System.IO;

namespace SpsLauncher
{
    /// <summary>
    /// Keeps a read-only snapshot of SpsGui's game_config.json and reloads it when the file changes.
    /// It never writes the file and never touches <see cref="GameConfig.Instance"/>.
    /// </summary>
    internal sealed class GameConfigWatcher
    {
        private readonly string configPath;
        private readonly object syncRoot = new object();
        private ReadOnlyGameConfig current = ReadOnlyGameConfig.Empty;
        private DateTime lastWriteTimeUtc = DateTime.MinValue;
        private long lastLength = -1;

        /// <param name="configPath">Absolute path to game_config.json.</param>
        public GameConfigWatcher(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException("Config path must not be empty.", nameof(configPath));
            }

            this.configPath = configPath;
        }

        public string ConfigPath
        {
            get { return configPath; }
        }

        /// <summary>
        /// Gets the latest successfully loaded snapshot.
        /// </summary>
        public ReadOnlyGameConfig Current
        {
            get
            {
                lock (syncRoot)
                {
                    return current;
                }
            }
        }

        /// <summary>
        /// Reloads the snapshot if the file was changed since the last successful load.
        /// On failure the previous snapshot is kept and the next call retries.
        /// </summary>
        /// <param name="force">Reload even if the file looks unchanged.</param>
        public void Refresh(bool force = false)
        {
            lock (syncRoot)
            {
                try
                {
                    var info = new FileInfo(configPath);
                    if (!info.Exists)
                    {
                        if (lastLength != -1 || force)
                        {
                            LauncherLog.Write("game_config.json not found: " + configPath);
                        }

                        current = ReadOnlyGameConfig.Empty;
                        lastWriteTimeUtc = DateTime.MinValue;
                        lastLength = -1;
                        return;
                    }

                    if (!force && info.LastWriteTimeUtc == lastWriteTimeUtc && info.Length == lastLength)
                    {
                        return;
                    }

                    ReadOnlyGameConfig loaded = GameConfig.LoadReadOnly(configPath);
                    current = loaded;
                    lastWriteTimeUtc = info.LastWriteTimeUtc;
                    lastLength = info.Length;
                    LauncherLog.Write("Loaded game_config.json (read-only): registered=" + loaded.Count);
                }
                catch (Exception ex)
                {
                    // SpsGui may be rewriting the file right now. Keep the previous snapshot.
                    LauncherLog.Write("Failed to load game_config.json, keep previous snapshot: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
    }
}
