// test flag to check the util with classicstun services other than steam p2p
#define STUN_STEAM

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PacketDotNet;

namespace SpsLogic.Utils
{
    public static class ClassicStunPacketUtil
    {
        private const int HeaderLength = 20;
        private const int TransactionIdOffset = 4;

        public static bool TryGetClassicStunTransactionId(
            Packet packet,
            out ClassicStunTransactionId transactionId,
            out bool isRequestPacket,
            out bool isResponsePacket)
        {
            transactionId = default;
            isRequestPacket = false;
            isResponsePacket = false;

            UdpPacket udp = packet.Extract<UdpPacket>();
            if (udp == null)
            {
                return false;
            }

            byte[] payload = udp.PayloadData;
            if (payload == null || payload.Length < HeaderLength)
            {
                return false;
            }

            ushort messageType = ReadUInt16BigEndian(payload, 0);
            ushort messageLength = ReadUInt16BigEndian(payload, 2);

            if (messageLength != payload.Length - HeaderLength)
            {
                return false;
            }

#if STUN_STEAM
            bool isRequest = payload.Length == 56;
            bool isResponse = payload.Length == 68;

            isRequestPacket = isRequest;
            isResponsePacket = isResponse;

            if (!isRequest && !isResponse)
            {
                return false;
            }

            if (isRequest && messageType != 0x0001)
            {
                return false;
            }

            if (isResponse && messageType != 0x0101)
            {
                return false;
            }
#else
            isRequestPacket = messageType == 0x0000;
            isResponsePacket = messageType == 0x0100;

            if (!IsDebugClassicStunMessageType(messageType))
            {
                return false;
            }
#endif

            transactionId = new ClassicStunTransactionId(payload, TransactionIdOffset);
            return true;
        }

#if DEBUG
        private static bool IsDebugClassicStunMessageType(ushort messageType)
        {
            switch (messageType)
            {
                // Binding Request
                case 0x0001:
                // Binding Success Response
                case 0x0101:
                // Binding Error Response
                case 0x0111:
                    return true;

                default:
                    return false;
            }
        }
#endif

        private static ushort ReadUInt16BigEndian(byte[] bytes, int offset)
        {
            return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
        }
    }

    public readonly struct ClassicStunTransactionId : IEquatable<ClassicStunTransactionId>
    {
        private readonly ulong high;
        private readonly ulong low;

        public ClassicStunTransactionId(byte[] bytes, int offset)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (offset < 0 || bytes.Length - offset < 16)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            high = ReadUInt64BigEndian(bytes, offset);
            low = ReadUInt64BigEndian(bytes, offset + 8);
        }

        public bool Equals(ClassicStunTransactionId other)
        {
            return high == other.high && low == other.low;
        }

        public override bool Equals(object obj)
        {
            return obj is ClassicStunTransactionId other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)high * 397) ^ (int)(high >> 32) ^ ((int)low * 397) ^ (int)(low >> 32);
            }
        }

        public override string ToString()
        {
            return high.ToString("X16") + low.ToString("X16");
        }

        private static ulong ReadUInt64BigEndian(byte[] bytes, int offset)
        {
            return
                ((ulong)bytes[offset] << 56) |
                ((ulong)bytes[offset + 1] << 48) |
                ((ulong)bytes[offset + 2] << 40) |
                ((ulong)bytes[offset + 3] << 32) |
                ((ulong)bytes[offset + 4] << 24) |
                ((ulong)bytes[offset + 5] << 16) |
                ((ulong)bytes[offset + 6] << 8) |
                bytes[offset + 7];
        }
    }
}
