using System;

namespace SpsLogic
{
    /// <summary>
    /// Receives Steam peer side effects without coupling Steam peer models to packet scanners.
    /// </summary>
    public interface ISteamPeerInterpreter
    {
        void Register(ulong netId, string name, ulong id);
        void Unregister(ulong netId);
    }

    /// <summary>
    /// Public control surface used by Sps to talk to a Steam monitor implementation.
    /// </summary>
    public interface ISteamMonitorInterpreter : IDisposable
    {
        void UpdatePeerList();
        void UpdatePeerStats();
    }

    /// <summary>
    /// Local adapter used by tests or in-process monitor implementations.
    /// </summary>
    public sealed class PacketScanSteamPeerInterpreter : ISteamPeerInterpreter
    {
        private readonly IPacketScan packetScan;

        public PacketScanSteamPeerInterpreter(IPacketScan packetScan)
        {
            this.packetScan = packetScan ?? throw new ArgumentNullException(nameof(packetScan));
        }

        public void Register(ulong netId, string name, ulong id)
        {
            packetScan.Register(netId, name, id);
        }

        public void Unregister(ulong netId)
        {
            packetScan.Unregister(netId);
        }
    }

    public static class SteamMonitorMessageType
    {
        public const string Initialize = "initialize";
        public const string UpdatePeerList = "updatePeerList";
        public const string UpdatePeerStats = "updatePeerStats";
        public const string Shutdown = "shutdown";
        public const string Ack = "ack";
        public const string Register = "register";
        public const string Unregister = "unregister";
        public const string Log = "log";
        public const string Error = "error";
        public const string Exited = "exited";
    }

    /// <summary>
    /// JSON-line message shared by Sps and SteamMonitor.
    /// </summary>
    public sealed class SteamMonitorMessage
    {
        public string Type { get; set; }
        public string RequestId { get; set; }
        public string SteamAppId { get; set; }
        public string GameProcessName { get; set; }
        public int ParentPid { get; set; }
        public int TargetPid { get; set; }
        public ulong NetId { get; set; }
        public string Name { get; set; }
        public ulong Id { get; set; }
        public string Message { get; set; }
        public string Reason { get; set; }
        public bool LeaveToFile { get; set; }
        public int ExitCode { get; set; }
    }
}
