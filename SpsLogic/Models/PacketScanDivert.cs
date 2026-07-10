using SharpPcap;
using SpsLogic.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SpsLogic
{
    public sealed class PacketArchiveDivert : BasePacketArchive
    {
        public override void AddCapture(object capture)
        {
            // WinDivert archive output is intentionally not implemented yet.
        }

        public override Task SaveCaptureAsync(string fileName)
        {
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
        }
    }

    public sealed class PlayerPingHistoryDivert : BasePlayerPingHistory
    {
        private const double LossDetectPatience = 12.0d;

        private readonly PacketArchiveDivert archive;
        private readonly ConnectionStats stats;
        private readonly double patienceLimitMs;
        private readonly PosixTimeval startTime;
        private readonly Dictionary<ClassicStunTransactionId, PosixTimeval> unmatchedPackets;

        public PlayerPingHistoryDivert(double patienceLimitMs, string name, ulong id)
        {
            archive = new PacketArchiveDivert();
            stats = new ConnectionStats(10, name, id);
            startTime = new PosixTimeval(stats.StartedAt);
            unmatchedPackets = new Dictionary<ClassicStunTransactionId, PosixTimeval>();
            this.patienceLimitMs = patienceLimitMs;
        }

        public override BasePacketArchive Archive
        {
            get { return archive; }
        }

        public override ConnectionStats Stats
        {
            get { return stats; }
        }

        public override void ReportSend(ClassicStunTransactionId id, PosixTimeval time)
        {
            if (!unmatchedPackets.ContainsKey(id))
            {
                unmatchedPackets[id] = time;
            }
        }

        public override void ReportReceive(ClassicStunTransactionId id, PosixTimeval time)
        {
            if (unmatchedPackets.TryGetValue(id, out var begin))
            {
                var intervalMs = (time.Value - begin.Value) * 1000m;
                Stats.PushPing((double)intervalMs);
                unmatchedPackets.Remove(id);
            }
            else
            {
                Logger.Log("Unexpected classicstun response packet received without sending any send classicstun packet. Maybe internet error?", true);
                Logger.Log("   Current packet: " + id.ToString());
                Logger.Log("   Current unmatched packets: " + string.Join(", ", unmatchedPackets.Keys.Select(k => k.ToString())));
            }
        }

        public override void Update()
        {
            var now = new PosixTimeval(DateTime.Now);
            List<ClassicStunTransactionId> removeCandidates = new List<ClassicStunTransactionId>();

            foreach (var pair in unmatchedPackets)
            {
                if ((double)(pair.Value.Value - startTime.Value) * 1.0d < LossDetectPatience)
                {
                    removeCandidates.Add(pair.Key);
                }

                if ((double)((now.Value - pair.Value.Value) * 1000m) > patienceLimitMs)
                {
                    Stats.PushPing(-1);
                    removeCandidates.Add(pair.Key);
                }
            }

            foreach (var key in removeCandidates)
            {
                unmatchedPackets.Remove(key);
            }
        }

        public override void Dispose()
        {
            Archive.Dispose();
        }
    }

    public sealed class PacketScanDivert : IPacketScan
    {
        private const int MaxPacketSize = 0xFFFF;
        private const int WinDivertAddressSize = 80;
        private const string Filter = "udp";
        private const ulong WinDivertFlagSniff = 1;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        private readonly Dictionary<ulong, PlayerPingHistoryDivert> registries;
        private readonly List<PlayerPingHistoryDivert> oldHistories;
        private readonly List<PlayerPingHistoryDivert> unseenOldHistories;
        private readonly object disposeLock = new object();
        private readonly double patienceLimitMs;
        private readonly Task receiveTask;
        private IntPtr handle;
        private bool disposed;

        public PacketScanDivert()
            : this(AppConfig.PacketPatienceLimitMs)
        {
        }

        public PacketScanDivert(double patienceLimitMs)
        {
            this.patienceLimitMs = patienceLimitMs;
            registries = new Dictionary<ulong, PlayerPingHistoryDivert>();
            oldHistories = new List<PlayerPingHistoryDivert>();
            unseenOldHistories = new List<PlayerPingHistoryDivert>();

            Logger.Log("Setting up WinDivert packet listener", true);

            try
            {
                handle = WinDivertOpen(Filter, 0, 0, WinDivertFlagSniff);
            }
            catch (DllNotFoundException)
            {
                Logger.Log("WinDivert.dll was not found. Put WinDivert.dll and WinDivert64.sys next to the executable or install WinDivert.", true);
            }
            catch (BadImageFormatException)
            {
                Logger.Log("WinDivert.dll architecture does not match this process.", true);
            }

            if (handle == IntPtr.Zero || handle == InvalidHandleValue)
            {
                Logger.Log("WinDivertOpen failed: " + CreateLastWin32ErrorMessage(), true);
            }

            receiveTask = Task.Run(() => ReceiveLoop());
        }

        public bool IsArchiveUsable()
        {
            return false;
        }

        public void Update()
        {
            lock (registries)
            {
                foreach (var history in registries.Values)
                {
                    history.Update();
                }
            }
        }

        public void Register(ulong netId, string name, ulong id)
        {
            lock (registries)
            {
                Logger.DebugLog($"Registered new net id to scan by WinDivert: {netId}");
                registries[netId] = new PlayerPingHistoryDivert(patienceLimitMs, name, id);
            }
        }

        public void Unregister(ulong netId)
        {
            lock (registries)
            {
                if (registries.TryGetValue(netId, out var history))
                {
                    oldHistories.Add(history);
                    unseenOldHistories.Add(history);
                    registries.Remove(netId);
                }
            }
        }

        public void ForEachActiveHistory(Action<string, ulong?, BasePlayerPingHistory> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (registries)
            {
                foreach (var pair in registries)
                {
                    action("Active", pair.Key, pair.Value);
                }
            }
        }

        public BasePlayerPingHistory[] TakeUnseenOldHistories()
        {
            lock (registries)
            {
                var histories = unseenOldHistories.Cast<BasePlayerPingHistory>().ToArray();
                unseenOldHistories.Clear();
                return histories;
            }
        }

        public void Dispose()
        {
            lock (disposeLock)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                Logger.Log("Dispose WinDivert packet listener", true);

                if (handle != IntPtr.Zero && handle != InvalidHandleValue)
                {
                    WinDivertClose(handle);
                    handle = IntPtr.Zero;
                }
            }

            try
            {
                receiveTask?.Wait(1000);
            }
            catch
            {
            }

            lock (registries)
            {
                foreach (var history in registries.Values)
                {
                    history.Dispose();
                }
                registries.Clear();

                foreach (var history in oldHistories)
                {
                    history.Dispose();
                }
                oldHistories.Clear();
                unseenOldHistories.Clear();
            }
        }

        private void ReceiveLoop()
        {
            byte[] packetBuffer = new byte[MaxPacketSize];
            byte[] addressBuffer = new byte[WinDivertAddressSize];

            while (!disposed)
            {
                uint readLength;
                bool received = WinDivertRecv(handle, packetBuffer, (uint)packetBuffer.Length, out readLength, addressBuffer);

                if (!received)
                {
                    if (!disposed)
                    {
                        Logger.Log("WinDivertRecv failed: " + CreateLastWin32ErrorMessage(), true);
                        Thread.Sleep(50);
                    }

                    continue;
                }

                ProcessPacket(packetBuffer, (int)readLength);
            }
        }

        private void ProcessPacket(byte[] packet, int length)
        {
            if (!TryReadIpv4UdpPacket(
                packet,
                length,
                out IPAddress sourceAddress,
                out ushort sourcePort,
                out IPAddress destinationAddress,
                out ushort destinationPort,
                out byte[] udpPayload))
            {
                return;
            }

            ulong srcNetId = PacketScan.CalcNetId(sourceAddress, sourcePort);
            ulong destNetId = PacketScan.CalcNetId(destinationAddress, destinationPort);

            PlayerPingHistoryDivert history = null;
            bool found = false;
            bool sendPacketFromMe = false;

            lock (registries)
            {
                if (found = registries.TryGetValue(srcNetId, out history))
                {
                    sendPacketFromMe = false;
                }
                else if (found = registries.TryGetValue(destNetId, out history))
                {
                    sendPacketFromMe = true;
                }

                if (!found)
                {
                    return;
                }

                history.Archive.AddCapture(packet);

                if (ClassicStunPacketUtil.TryGetClassicStunTransactionIdFromUdpPayload(
                    udpPayload,
                    out ClassicStunTransactionId id,
                    out bool isRequestPacket,
                    out bool isResponsePacket))
                {
                    var timestamp = new PosixTimeval(DateTime.Now);
                    if (sendPacketFromMe && isRequestPacket)
                    {
                        Logger.DebugLog("WinDivert packet send detected: " + id.ToString());
                        history.ReportSend(id, timestamp);
                    }
                    else if (!sendPacketFromMe && isResponsePacket)
                    {
                        Logger.DebugLog("WinDivert packet recv detected: " + id.ToString());
                        history.ReportReceive(id, timestamp);
                    }
                }
            }
        }

        private static bool TryReadIpv4UdpPacket(
            byte[] packet,
            int length,
            out IPAddress sourceAddress,
            out ushort sourcePort,
            out IPAddress destinationAddress,
            out ushort destinationPort,
            out byte[] udpPayload)
        {
            sourceAddress = null;
            sourcePort = 0;
            destinationAddress = null;
            destinationPort = 0;
            udpPayload = null;

            if (packet == null || length < 28)
            {
                return false;
            }

            int version = packet[0] >> 4;
            if (version != 4)
            {
                return false;
            }

            int ipHeaderLength = (packet[0] & 0x0F) * 4;
            if (ipHeaderLength < 20 || length < ipHeaderLength + 8)
            {
                return false;
            }

            if (packet[9] != 17)
            {
                return false;
            }

            int udpOffset = ipHeaderLength;
            ushort udpLength = ReadUInt16BigEndian(packet, udpOffset + 4);
            if (udpLength < 8 || length < udpOffset + udpLength)
            {
                return false;
            }

            sourcePort = ReadUInt16BigEndian(packet, udpOffset);
            destinationPort = ReadUInt16BigEndian(packet, udpOffset + 2);
            sourceAddress = new IPAddress(new[] { packet[12], packet[13], packet[14], packet[15] });
            destinationAddress = new IPAddress(new[] { packet[16], packet[17], packet[18], packet[19] });

            int payloadOffset = udpOffset + 8;
            int payloadLength = udpLength - 8;
            udpPayload = new byte[payloadLength];
            Buffer.BlockCopy(packet, payloadOffset, udpPayload, 0, payloadLength);
            return true;
        }

        private static ushort ReadUInt16BigEndian(byte[] bytes, int offset)
        {
            return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
        }

        private static string CreateLastWin32ErrorMessage()
        {
            var exception = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return exception.Message;
        }

        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertRecv(
            IntPtr handle,
            byte[] packet,
            uint packetLen,
            out uint readLen,
            byte[] address);

        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertClose(IntPtr handle);
    }
}
