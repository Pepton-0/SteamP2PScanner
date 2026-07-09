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
    public class PacketScan
    {
        public static ulong CalcNetId(IPAddress addr, ushort port)
        {
            return (ulong)port << 32 | BitConverter.ToUInt32(addr.GetAddressBytes(), 0);
        }
    }
}
