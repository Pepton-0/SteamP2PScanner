using SharpPcap;
using SpsLogic.Utils;
using System;
using System.Net;
using System.Threading.Tasks;

namespace SpsLogic
{
    /// <summary>
    /// Abstraction for packet scan operations used by UI/ViewModel layers.
    /// </summary>
    public interface IPacketScan : IDisposable
    {
        bool IsArchiveUsable();
        void Update();
        void Register(ulong netId, string name, ulong id);
        void Unregister(ulong netId);
        void ForEachActiveHistory(Action<string, ulong?, BasePlayerPingHistory> action);
        BasePlayerPingHistory[] TakeUnseenOldHistories();
    }

    public abstract class BasePacketArchive : IDisposable
    {
        public abstract void AddCapture(object capture);
        public abstract Task SaveCaptureAsync(string fileName);
        public abstract void Dispose();
    }

    public abstract class BasePlayerPingHistory : IDisposable
    {
        public abstract BasePacketArchive Archive { get; }
        public abstract ConnectionStats Stats { get; }
        public abstract void ReportSend(ClassicStunTransactionId id, PosixTimeval time);
        public abstract void ReportReceive(ClassicStunTransactionId id, PosixTimeval time);
        public abstract void Update();
        public abstract void Dispose();
    }

    public static class PacketScanUtil
    {
        public static ulong CalcNetId(IPAddress addr, ushort port)
        {
            return (ulong)port << 32 | BitConverter.ToUInt32(addr.GetAddressBytes(), 0);
        }
    }
}
