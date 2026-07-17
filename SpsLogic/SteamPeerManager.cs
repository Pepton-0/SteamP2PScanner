using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Steamworks;

namespace SpsLogic
{
    /// <summary>
    /// Singleton model.
    /// Manage a list of active Steam P2P peers. The peers must be in a steam lobby with the current user to be detected.
    /// They will automatically be removed from the list if no packet was sent/recieved for a set amount of time.
    /// </summary>
    public class SteamPeerManager : IDisposable
    {
        private readonly ISteamPeerInterpreter Interpreter;
        private readonly string GameProcessName;
        private readonly Stopwatch sw;
        private FileStream fs = null;
        private StreamReader sr = null;
        private readonly FileSystemWatcher fsWatcher;
        private bool mustReopenLog = true;
        private long? lastPosInLog = null;

        private static readonly Regex STEAMID3_REGEX = new Regex(@"\[U:1:(?<id>\d+)\]", RegexOptions.Compiled);
        private const long STEAMID64_BASE = 0x0110_0001_0000_0000;
        private static bool SteamApiInitialized = false;

        private static readonly Func<CSteamID, ISteamPeerInterpreter, SteamPeerBase>[] PEER_FACTORIES =
            Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t.IsSubclassOf(typeof(SteamPeerBase)))
                .Select(t => new Func<CSteamID, ISteamPeerInterpreter, SteamPeerBase>((CSteamID sid, ISteamPeerInterpreter interpreter) => Activator.CreateInstance(t, sid, interpreter) as SteamPeerBase))
                .ToArray();

        /// <summary>
        /// List of peers mapped by Steam ID.
        /// </summary>
        private readonly Dictionary<CSteamID, SteamPeerInfo> mPeers = new Dictionary<CSteamID, SteamPeerInfo>();

        public SteamPeerManager(ISteamPeerInterpreter interpreter, string gameProcessName)
        {
            if (!SteamApiInitialized)
            {
                throw new InvalidOperationException("Call InitializeSteamApi() first");
            }

            this.Interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
            this.GameProcessName = gameProcessName;
            sw = new Stopwatch();
            sw.Reset();
            fsWatcher = new FileSystemWatcher(Path.GetDirectoryName(AppConfig.Instance.SteamLogPath));
            fsWatcher.Filter = Path.GetFileName(AppConfig.Instance.SteamLogPath);
            fsWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
            fsWatcher.Changed += (e, s) => mustReopenLog = true;
            fsWatcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// Initializes Steamworks.NET once for Steam peer inspection.
        /// </summary>
        public static bool InitializeSteamApi(SteamAppInfo info)
        {
            if (info == null)
            {
                Logger.Log("Failed to initialize steam api because SteamAppInfo is null.");
                return false;
            }

            if (SteamApiInitialized)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(info.SteamAppId))
            {
                Logger.Log("Failed to initialize steam api because SteamAppId is empty.");
                return false;
            }

            Environment.SetEnvironmentVariable("SteamAppId", info.SteamAppId);

            if (!SteamAPI.IsSteamRunning())
            {
                Logger.Log("Failed to initialize steam api because Steam is not running.");
                return false;
            }

            if (!SteamAPI.Init())
            {
                Logger.Log("Failed to initialize steam api because SteamAPI.Init returned false.");
                return false;
            }

            Logger.Log("Initialized steam api");

            SteamApiInitialized = true;
            return true;
        }

        /// <summary>
        /// Shuts down Steamworks.NET if this manager initialized it.
        /// </summary>
        public static void ShutdownSteamApi()
        {
            if (!SteamApiInitialized)
            {
                return;
            }

            SteamAPI.Shutdown();
            SteamApiInitialized = false;
            Logger.Log("Shutdown steam api");
        }

        /// <summary>
        /// extract SteamID from log
        /// </summary>
        /// <param name="str">string read from log</param>
        /// <returns></returns>
        private CSteamID ExtractUser(string str)
        {
            Match m = STEAMID3_REGEX.Match(str);
            if (m.Success)
            {
                return new CSteamID(ulong.Parse(m.Groups["id"].Value) + STEAMID64_BASE);
            }
            else
            {
                return new CSteamID(0);
            }
        }

        /// <summary>
        /// create new steam peer instance
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        private SteamPeerBase GetPeer(CSteamID player)
        {
            SteamPeerBase peer = null;
            foreach (var factory in PEER_FACTORIES)
            {
                try
                {
                    peer = factory(player, Interpreter);
                    if (peer.UpdatePeerInfo())
                    {
                        Logger.Log($"[PEER CONNECT] \"{peer.Name}\" (https://steamcommunity.com/profiles/{(ulong)peer.SteamID}) has connected via {peer.ConnectionTypeName}",
                            true);

                        return peer;
                    }
                }
                catch (Exception)
                {
                    peer?.Dispose();
                }
            }
            return null;
        }

        public void UpdatePeerList()
        {
            // Make sure we're constantly writing to the IPC log to force Steam to eventually flush
            // This call was chosen because it's not something a game will call often
            // Thus we avoid blowing up the IPC log with dummy calls
            SteamFriends.SendClanChatMessage(new CSteamID(0), "");

            if (mustReopenLog)
            {
                Logger.DebugLog("Reopened ipc log file");
                sr?.Dispose();
                fs?.Close();
                fs?.Dispose();

                try
                {
                    fs = new FileStream(AppConfig.Instance.SteamLogPath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
                    sr = new StreamReader(fs);
                    // If the file had to be reopened, read from the last position we were at before
                    if (lastPosInLog is null)
                        fs.Seek(0, SeekOrigin.End);
                    else
                        fs.Seek((long)lastPosInLog, SeekOrigin.Begin);
                    mustReopenLog = false;
                }
                catch (DirectoryNotFoundException)
                {
                    // TODO create message system to match with mvvm.
                    //MessageBox.Show("Steam IPC log file directory does not exist", "Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    Logger.Log("[ERROR] Steam IPC log file directory does not exist");
                }
            }

            while (!mustReopenLog)
            {
                string line = null;
                try
                {
                    line = sr.ReadLine();

                    if (!string.IsNullOrEmpty(line))
                        Logger.DebugLog("new line; " + line);

                    // ログの終端に到達したら終了
                    if (line == null)
                    {
                        lastPosInLog = fs.Position;
                        break;
                    }
                }
                catch(Exception e)
                {
                    Logger.Log("Failed to load a new line because " + e.Message);
                    continue;
                }

                // 対象のプロセスに関するログでなければスキップ
                if (!line.Contains(GameProcessName))
                    continue;

                bool begin;
                if (line.Contains("BeginAuthSession"))
                {
                    Trace.WriteLine("[DEBUG] BeginAuthSession detected");
                    begin = true;
                }
                else if (line.Contains("EndAuthSession"))
                {
                    Trace.WriteLine("[DEBUG] EndAuthSession detected");
                    begin = false;
                }
                else if (line.Contains("LeaveLobby"))
                {
                    foreach (var sid in mPeers.Keys.ToArray())
                    {
                        var peer = mPeers[sid].peer;
                        RemovePeer(sid);
                        LogDisconnect(peer, sid, "Player left Steam lobby");
                    }
                    continue;
                }
                else continue;

                CSteamID steamID = ExtractUser(line);

                if (steamID.m_SteamID != 0)
                {
                    if (steamID.BIndividualAccount())
                    {
                        if (begin)
                        {
                            if (!mPeers.TryGetValue(steamID, out SteamPeerInfo pInfo))
                            {
                                var newPeerInfo = new SteamPeerInfo(GetPeer(steamID));
                                if (newPeerInfo.peer is null)
                                {
                                    Logger.Log($"[PEER CONNECT] Player \"{steamID}\" was detected, but we don't have a P2P connection to them yet", true);
                                    newPeerInfo.lastDisconnectTimeMS = sw.ElapsedMilliseconds;
                                }
                                mPeers.Add(steamID, newPeerInfo);
                            }
                            else
                            {
                                pInfo.lastDisconnectTimeMS = 0;
                                if (pInfo.peer is null)
                                {
                                    pInfo.peer = GetPeer(steamID);
                                }
                            }
                        }
                        else
                        {
                            if (mPeers.TryGetValue(steamID, out SteamPeerInfo pInfo))
                            {
                                SteamPeerBase peer = pInfo.peer;
                                RemovePeer(steamID);
                                LogDisconnect(peer, steamID, "Auth session ended");
                            }
                        }
                    }
                    else
                    {
                        Logger.Log($"[PARSE ERROR] \"{steamID}\" was not a valid steam user", true);
                    }
                }
            }
            UpdateAllPeerInfo();
            CleanUpOldPeers();
        }

        public void UpdatePeerStats()
        {
            UpdateAllPeerInfo();
        }

        /// <summary>
        /// log disconnected peer and reason
        /// </summary>
        /// <param name="peer"></param>
        /// <param name="sid"></param>
        /// <param name="reason"></param>
        private void LogDisconnect(SteamPeerBase peer, CSteamID sid, string reason)
        {
            if (peer is null)
            {
                Logger.Log($"[PEER DISCONNECT] (https://steamcommunity.com/profiles/{(ulong)sid}): {reason}", true);
            }
            else
            {
                Logger.Log($"[PEER DISCONNECT] \"{peer.Name}\" (https://steamcommunity.com/profiles/{(ulong)sid}): {reason}", true);
            }
        }

        private void UpdateAllPeerInfo()
        {
            foreach (var item in mPeers.ToArray())
            {
                if (!mPeers.TryGetValue(item.Key, out SteamPeerInfo pInfo))
                    continue;

                bool isP2PConnected;
                if (pInfo.peer is null)
                {
                    pInfo.peer = GetPeer(item.Key);
                    isP2PConnected = pInfo.peer != null;
                }
                else
                {
                    isP2PConnected = pInfo.peer.UpdatePeerInfo();
                }

                if (isP2PConnected)
                {
                    pInfo.isConnected = true;
                    pInfo.lastDisconnectTimeMS = 0;
                }
                else
                {
                    if (pInfo.peer != null)
                    {
                        SteamPeerBase peer = pInfo.peer;
                        RemovePeer(item.Key);
                        LogDisconnect(peer, item.Key, "P2P connection ended");
                    }
                    else
                    {
                        if (pInfo.isConnected)
                        {
                            pInfo.lastDisconnectTimeMS = sw.ElapsedMilliseconds;
                        }
                        pInfo.isConnected = false;
                    }
                }
            }
        }
        private void CleanUpOldPeers()
        {
            // Keep disconnected peers while the Steam lobby remains so custom rooms
            // can continue showing the last known ping between rounds.
        }

        private bool RemovePeer(CSteamID sid)
        {
            SteamPeerInfo pInfo;
            bool exists = mPeers.TryGetValue(sid, out pInfo);
            if (!exists) { return false; }
            if (pInfo.peer is null)
            {
                mPeers.Remove(sid);
                return true;
            }

            // Unregistring and moving to history is called from peer.Dispose automatically
            //SessionHistory.Add(new SessionHistoryItem(pInfo.peer));
            pInfo.peer.Dispose();
            mPeers.Remove(sid);

            return true;
        }

        public IEnumerable<SteamPeerBase> GetPeers()
        {
            return mPeers.Values.Where(info => info.peer != null).Select(info => info.peer);
        }

        public void Dispose()
        {
            foreach (var sid in mPeers.Keys.ToArray())
            {
                RemovePeer(sid);
            }

            fsWatcher.Dispose();
            sr?.Dispose();
            fs?.Dispose();
        }
    }
}
