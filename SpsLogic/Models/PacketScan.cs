using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using SpsLogic.Utils;

namespace SpsLogic
{
    /// <summary>
    /// Abstraction for packet scan operations used by UI/ViewModel layers.
    /// </summary>
    public interface IPacketScan : IDisposable
    {
        void Update();
        void Register(ulong netId, string name, ulong id);
        void Unregister(ulong netId);
        void ForEachActiveHistory(Action<string, ulong?, BasePlayerPingHistory> action);
        BasePlayerPingHistory[] TakeUnseenOldHistories();
    }

    /// <summary>
    /// Setup pcap
    /// only filter a steam udp port
    /// 
    /// register:
    ///     my ip&port, opponent ip&port
    /// 
    /// on received:
    ///     detect classicstun protocol send/receive, transaction id, timing
    ///     
    /// for a ip&port -> each PacketArchive
    /// 
    /// save filtered packet lists
    /// </summary>
    public class PacketScan : IPacketScan
    {
        /// <summary>
        /// Stocks captures and write to pcap 
        /// you can execute save task only once
        /// </summary>
        public class PacketArchive : BasePacketArchive
        {
            private static readonly string ArchiveDir = "archives";
            private readonly BlockingCollection<RawCapture> Captures;
            private Task SaveTask;
            private readonly object saveTaskLock = new object();

            public PacketArchive()
            {
                // maybe better to allocate a bit before? idk.
                Captures = new BlockingCollection<RawCapture>();
            }

            public override void AddCapture(object capture)
            {
                if (!(capture is RawCapture rawCapture))
                {
                    throw new ArgumentException("Capture must be a SharpPcap RawCapture.", nameof(capture));
                }

                AddCapture(rawCapture);
            }

            internal void AddCapture(RawCapture capture)
            {
                if (capture == null)
                {
                    throw new ArgumentNullException(nameof(capture));
                }

                Captures.Add(capture);
            }

            public override void Dispose()
            {
                Captures.CompleteAdding();

                if (SaveTask?.IsCompleted == false)
                {
                    SaveTask.ContinueWith((a) => { Captures.Dispose(); }, TaskContinuationOptions.ExecuteSynchronously);
                }
                else
                {
                    Captures.Dispose();
                }
            }

            /// <summary>
            /// Write the archive to a file with a file name async.
            /// </summary>
            /// <param name="fileName">This is the space for steam id and date</param>
            /// <returns></returns>
            /// <exception cref="ArgumentException"></exception>
            /// <exception cref="InvalidOperationException"></exception>
            /// <exception cref="Exception"></exception>
            public override Task SaveCaptureAsync(string fileName)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    throw new ArgumentException("File name must not be empty.", nameof(fileName));
                }

                lock (saveTaskLock)
                {
                    if (SaveTask != null)
                    {
                        throw new InvalidOperationException("Save already started.");
                    }

                    Captures.CompleteAdding();

                    SaveTask = Task.Run(() =>
                    {
                        var filePath = Path.Combine(ArchiveDir, fileName);
                        if (!Directory.Exists(ArchiveDir))
                        {
                            Directory.CreateDirectory(ArchiveDir);
                        }
                        if (File.Exists(filePath))
                        {
                            throw new Exception($"{filePath} is already exists");
                        }
                        else
                        {
                            using (var writer = new CaptureFileWriterDevice(filePath))
                            {
                                writer.Open();
                                foreach (var cap in Captures)
                                {
                                    writer.Write(cap);
                                }
                                writer.Close();
                            }
                        }
                    });

                    return SaveTask;
                }
            }
        }

        public class PlayerPingHistory : BasePlayerPingHistory
        {
            /// <summary>
            /// ignore packet loss in a few seconds since the scan begins
            /// </summary>
            private const double LossDetectPatience = 9.0d;

            private readonly PacketArchive archive;
            private readonly ConnectionStats stats;

            public override BasePacketArchive Archive
            {
                get { return archive; }
            }

            public override ConnectionStats Stats
            {
                get { return stats; }
            }

            private readonly double PatienceLimitMs;
            private readonly PosixTimeval StartTime;

            // register a pair when detecting a send classicstun from my computer to the opponent computer
            // because it can't be lossed
            // remove the pair when receiving the response classicstun from the opponent computer to my computer
            // or time elapse since registering
            private Dictionary<ClassicStunTransactionId, PosixTimeval> UnmatchedPackets;

            public PlayerPingHistory(double patienceLimitMs, string name, ulong id)
            {
                archive = new PacketArchive();
                stats = new ConnectionStats(10, name, id);
                this.StartTime = new PosixTimeval(Stats.StartedAt);
                UnmatchedPackets = new Dictionary<ClassicStunTransactionId, PosixTimeval>();
                this.PatienceLimitMs = patienceLimitMs;
            }

            /// <summary>
            /// Report a classicstun request packet from my computer to the opponent computer.
            /// In some network environment, duplication occures so we respect the first packet.
            /// </summary>
            /// <param name="id"></param>
            /// <param name="time"></param>
            public override void ReportSend(ClassicStunTransactionId id, PosixTimeval time)
            {
                if (!UnmatchedPackets.ContainsKey(id))
                {
                    UnmatchedPackets[id] = time;
                }
            }

            /// <summary>
            /// Report a classicstun response packet from the opponent computer to my computer.
            /// </summary>
            /// <param name="id"></param>
            /// <param name="time"></param>
            public override void ReportReceive(ClassicStunTransactionId id, PosixTimeval time)
            {
                if (UnmatchedPackets.TryGetValue(id, out var begin))
                {
                    var intervalMs = (time.Value - begin.Value) * 1000m;
                    Stats.PushPing((double)intervalMs);
                    UnmatchedPackets.Remove(id);
                }
                else
                {
                    Logger.Log("Unexpected classicstun response packet received without sending any send classicstun packet. Maybe internet error?", true);
                    Logger.Log("   Current packet: " + id.ToString());
                    Logger.Log("   Current unmatched packets: " + string.Join(", ", UnmatchedPackets.Keys.Select(k => k.ToString())));
                }
            }

            /// <summary>
            /// Called this repeatedly like each 1s.
            /// </summary>
            public override void Update()
            {
                var now = new PosixTimeval(DateTime.Now);
                List<ClassicStunTransactionId> removeCandidates = new List<ClassicStunTransactionId>();
                foreach(var pair in UnmatchedPackets)
                {
                    if((double)(pair.Value.Value - StartTime.Value) * 1.0d < LossDetectPatience)
                    {
                        // The packet within a few secs since packet scan is ignored because its packet loss is common.
                        removeCandidates.Add(pair.Key);
                    }

                    if((double)((now.Value - pair.Value.Value) * 1000m) > PatienceLimitMs)
                    {
                        // Take the packet as packet loss.
                        Stats.PushPing(-1);
                        removeCandidates.Add(pair.Key);
                    }
                }
                foreach(var key in removeCandidates)
                {
                    UnmatchedPackets.Remove(key);
                }
            }

            public override void Dispose()
            {
                Archive.Dispose();
            }
        }

        private readonly LibPcapLiveDevice[] _devices;

        /// <summary>
        /// Accessible from varaious threads.
        /// </summary>
        private readonly Dictionary<ulong, PlayerPingHistory> Registries;
        private readonly List<PlayerPingHistory> OldHistories;
        private readonly List<PlayerPingHistory> UnseenOldHistories;
        private readonly Stopwatch Stopwatch;
        private readonly double PatienceLimitMs;

        /// <summary>
        /// Creates a packet scanner using the patience limit configured by <see cref="AppConfig"/>.
        /// </summary>
        /// <exception cref="Exception">Thrown when packet capture devices cannot be opened.</exception>
        public PacketScan()
            : this(AppConfig.PacketPatienceLimitMs)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="patienceLimitMs">After this time passed 
        /// since the classicstun send packet without receing a response packet,
        /// we take it as packet loss.</param>
        /// <exception cref="Exception"></exception>
        public PacketScan(double patienceLimitMs)
        {
            this.PatienceLimitMs = patienceLimitMs;
            Registries = new Dictionary<ulong, PlayerPingHistory>();
            OldHistories = new List<PlayerPingHistory>();
            UnseenOldHistories = new List<PlayerPingHistory>();
            Stopwatch = new Stopwatch();
            Stopwatch.Start();
            Logger.Log("Setting up network device listener", true);

            var devCandis = LibPcapLiveDeviceList.Instance;
            if(devCandis.Count <= 0)
            {
                Logger.Log("No capture devices found.", true);
                throw new Exception("No capture devices found");
            }

            _devices = new LibPcapLiveDevice[devCandis.Count];
            for (int i = 0; i < devCandis.Count; i++)
            {
                var device = _devices[i] = devCandis[i];
                // We filter classicstun both here and in csharp but its intentional
                // because the device filter might be just a udp filter in latter version.
                device.OnPacketArrival += OnPacketArrival;
                device.Open();
                device.Filter = "udp";
                device.StartCapture();
            }
        }

        private void OnPacketArrival(object sender, PacketCapture e)
        {
            var raw = e.GetPacket();
            Packet packet;

            try
            {
                packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            }
            catch
            {
                return;
            }

            var ip = packet.Extract<IPPacket>();
            var udp = packet.Extract<UdpPacket>();

            if (ip == null || udp == null)
            {
                return;
            }

            var srcNetId = CalcNetId(ip.SourceAddress, udp.SourcePort);
            var destNetId = CalcNetId(ip.DestinationAddress, udp.DestinationPort);

            PlayerPingHistory history = null;
            ulong opponentNetId = 0;
            var found = false;
            var sendPacketFromMe = false;

            lock (Registries)
            {
                if (found = Registries.TryGetValue(srcNetId, out history))
                {
                    opponentNetId = srcNetId;
                    // sendPacketFromMe = false; default
                }
                else if (found = Registries.TryGetValue(destNetId, out history))
                {
                    opponentNetId = destNetId;
                    sendPacketFromMe = true;
                }

                if (found)
                {
                    history.Archive.AddCapture(raw);

                    if (ClassicStunPacketUtil.TryGetClassicStunTransactionId(
                        packet,
                        out ClassicStunTransactionId id,
                        out bool isRequestPacket,
                        out bool isResponsePacket))
                    {
                        if (sendPacketFromMe && isRequestPacket)
                        {
                            Logger.DebugLog("Packet send detected: " + id.ToString());
                            history.ReportSend(id, raw.Timeval);
                        }
                        else if(!sendPacketFromMe && isResponsePacket)
                        {
                            Logger.DebugLog("Packet recv detected: " + id.ToString());
                            history.ReportReceive(id, raw.Timeval);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Check packet loss for each time like 1s
        /// TODO call this event
        /// </summary>
        public void Update()
        {
            lock (Registries)
            {
                foreach(var history in Registries.Values)
                {
                    history.Update();
                }
            }
        }

        /// <summary>
        /// Start profiling of the classicstun communication with the net id.
        /// The name is stored in <see cref="ConnectionStats"/> for UI display.
        /// </summary>
        /// <param name="netId"></param>
        /// <param name="name"></param>
        /// <param name="id">CSteamID</param>
        public void Register(ulong netId, string name, ulong id)
        {
            lock (Registries)
            {
                Logger.DebugLog($"Registered new net id to scan: {netId}");
                Registries[netId] = new PlayerPingHistory(PatienceLimitMs, name, id);
            }
        }

        /// <summary>
        /// Stop profiling of the classicstun communication with the net id
        /// Use <c>PacketScan.CalcNetId(...)</c> to get net id
        /// </summary>
        /// <param name="netId"></param>
        public void Unregister(ulong netId)
        {
            lock (Registries)
            {
                if (Registries.TryGetValue(netId, out var history))
                {
                    OldHistories.Add(history);
                    UnseenOldHistories.Add(history);
                    Registries.Remove(netId);
                }
            }
        }

        public void ForEachActiveHistory(Action<string, ulong?, BasePlayerPingHistory> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (Registries)
            {
                foreach (var pair in Registries)
                {
                    action("Active", pair.Key, pair.Value);
                }
            }
        }

        public BasePlayerPingHistory[] TakeUnseenOldHistories()
        {
            lock (Registries)
            {
                var histories = UnseenOldHistories.ToArray();
                UnseenOldHistories.Clear();
                return histories.Cast<BasePlayerPingHistory>().ToArray();
            }
        }

        public void Dispose()
        {
            Logger.Log("Dispose network device listener", true);
            lock (Registries)
            {
                foreach (var device in _devices)
                {
                    device.Dispose();
                }

                foreach (var history in Registries.Values)
                {
                    history.Dispose();
                }
                Registries.Clear();

                foreach (var history in OldHistories)
                {
                    history.Dispose();
                }
                OldHistories.Clear();
                UnseenOldHistories.Clear();
            }
        }

        public static ulong CalcNetId(IPAddress addr, ushort port)
        {
            return (ulong)port << 32 | BitConverter.ToUInt32(addr.GetAddressBytes(), 0);
        }
    }
}
