using System;
using System.IO;
using Newtonsoft.Json;
using SpsLogic;

namespace SteamMonitor
{
    internal sealed class SteamMonitorInterpreter : ISteamPeerInterpreter
    {
        private readonly TextWriter output;
        private readonly object outputLock = new object();

        public SteamMonitorInterpreter(TextWriter output)
        {
            this.output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public void Register(ulong netId, string name, ulong id)
        {
            Write(new SteamMonitorMessage
            {
                Type = SteamMonitorMessageType.Register,
                NetId = netId,
                Name = name,
                Id = id
            });
        }

        public void Unregister(ulong netId)
        {
            Write(new SteamMonitorMessage
            {
                Type = SteamMonitorMessageType.Unregister,
                NetId = netId
            });
        }

        public void Ack(string requestId)
        {
            Write(new SteamMonitorMessage
            {
                Type = SteamMonitorMessageType.Ack,
                RequestId = requestId
            });
        }

        public void Log(string message, bool leaveToFile)
        {
            Write(new SteamMonitorMessage
            {
                Type = SteamMonitorMessageType.Log,
                Message = message,
                LeaveToFile = leaveToFile
            });
        }

        public void Error(string message)
        {
            Write(new SteamMonitorMessage
            {
                Type = SteamMonitorMessageType.Error,
                Message = message
            });
        }

        public void Exited(string reason, int exitCode)
        {
            Write(new SteamMonitorMessage
            {
                Type = SteamMonitorMessageType.Exited,
                Reason = reason,
                ExitCode = exitCode
            });
        }

        private void Write(SteamMonitorMessage message)
        {
            string json = JsonConvert.SerializeObject(message);
            lock (outputLock)
            {
                output.WriteLine(json);
                output.Flush();
            }
        }
    }
}
