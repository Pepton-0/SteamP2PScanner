using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpsLogic
{
    /// <summary>
    /// What others set:
    ///     DNS service:
    ///     Target qName
    /// What others see:
    ///     Sucess count
    ///     Fail count
    ///     Latest ping results: -1 for loss, others are normal ping ( you should use min(0,x)
    /// constructor:
    ///     setup settings
    /// start()
    /// dispose()
    /// ~destructor:
    /// </summary>
    public class DnsPing : IDisposable
    {
        class PingParallelResources
        {
            /// <summary>
            /// How many times does the dns request sucess in the whole time
            /// </summary>
            public int SuccessCount = 0;

            /// <summary>
            /// How many times does the dns quest fail in the whole time 
            /// </summary>
            public int FailCount = 0;

            /// <summary>
            /// Non reported latest pings
            /// </summary>
            public readonly Queue<double> stockedPings = new Queue<double>();

            /// <summary>
            /// You don't have to take care of duplication of transaction ids because of deletion by PatienceLimitMs.
            /// </summary>
            public int nextTransactionId = 0;
        }

        private bool isDisposed = false;
        private bool Started = false;

        // Threading resources
        private readonly UdpClient Client;
        private readonly string QueryName;
        private readonly PingParallelResources Resources;
        private readonly CancellationTokenSource CancelToken;
        private readonly Timer _sendTask;
        private readonly Task _receiveTask;
        private readonly Stopwatch Stopwatch;
        private readonly int DelayMs;
        private readonly int PatienceLimitMs;

        public readonly ConcurrentDictionary<ushort, TimeSpan> Id2beginTimestamp = new ConcurrentDictionary<ushort, TimeSpan>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dnsService">Target DNS Service IP like 8.8.8.8.<br/>
        /// Maybe using serveral services is better not to be DNS attack?<br/>
        ///     Target DNS Service prot is usually 53</param>
        /// <param name="queryName">The query name to be converted into IP.</param>
        /// <param name="delayMs">Interval between each DNS Request in milliseconds</param>
        /// <param name="patienceLimitMs">How long we wait for DNS response</param>
        public DnsPing(IPEndPoint dnsService, string queryName, int delayMs, int patienceLimitMs)
        {
            if(string.IsNullOrEmpty(queryName))
                throw new ArgumentException($"{nameof(queryName)} is null or empty.");
            if (delayMs < 1000)
                throw new ArgumentException($"{nameof(delayMs)} should be same or greater than 1000 to prevent DNS attack.");
            if (patienceLimitMs < 1000)
                throw new ArgumentException($"{nameof(patienceLimitMs)} should be same or greater than 1000 to wait packets");

            this.QueryName = queryName;
            this.Client = new UdpClient();
            Client.Connect(dnsService);
            this.Resources = new PingParallelResources();
            this.CancelToken = new CancellationTokenSource();
            this.Stopwatch = new Stopwatch();
            Stopwatch.Start();

            this.DelayMs = delayMs;
            this.PatienceLimitMs = patienceLimitMs;

            // Receive DNS response.
            this._receiveTask = Task.Run(async () =>
            {
                while (!CancelToken.IsCancellationRequested)
                {
                    UdpReceiveResult result;

                    try
                    {
                        result = await Client.ReceiveAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        break; // Stop receive task.
                    }
                    catch (SocketException e)
                    {
                        Logger.DebugLog($"Failed to receive DNS response: {e.SocketErrorCode}");
                        continue;
                    }

                    var endTimestamp = Stopwatch.Elapsed;
                    var (isResponse, transaction) = GetDnsResponseTransactionId(result.Buffer);
                    if (isResponse && Id2beginTimestamp.TryRemove(transaction, out var beginTimestamp))
                    {
                        var ping = (endTimestamp - beginTimestamp).TotalMilliseconds;
                        lock (Resources)
                        {
                            Resources.stockedPings.Enqueue(ping);
                            Resources.SuccessCount++;
                        }
                    }
                }

                // Receive task stops.
                Logger.DebugLog("Stopped DNS response receiver.");
            });

            this._sendTask = new Timer((_) => {
                if (CancelToken.IsCancellationRequested)
                {
                    _sendTask.Change(Timeout.Infinite, Timeout.Infinite);
                    _sendTask.Dispose();
                    return;
                }

                // Increment uses the last 16 bits so recent transaction ids can't be the same.
                ushort transactionId = (ushort)Interlocked.Increment(ref Resources.nextTransactionId);
                var packet = BuildDnsQueryPacket(transactionId, QueryName);
                Id2beginTimestamp.GetOrAdd(transactionId, Stopwatch.Elapsed);

                try
                {
                    Client.Send(packet, packet.Length);
                }
                catch (ObjectDisposedException)
                {
                    Logger.DebugLog("DNS ping client was already disposed.");
                }
                catch(SocketException e)
                {
                    Logger.DebugLog($"Failed to send DNS query: {e.SocketErrorCode}");
                    if(Id2beginTimestamp.TryRemove(transactionId, out var _))
                    {
                        lock (Resources)
                        {
                            Resources.stockedPings.Enqueue(-1);
                            Resources.FailCount++;
                        }
                    }
                }

                // Remove expired transaction
                var endTimestamp = Stopwatch.Elapsed;
                foreach(var pair in Id2beginTimestamp)
                {
                    if((endTimestamp - pair.Value ).TotalMilliseconds > PatienceLimitMs)
                    {
                        if (Id2beginTimestamp.TryRemove(pair.Key, out var _))
                        {
                            lock (Resources)
                            {
                                Resources.stockedPings.Enqueue(-1);
                                Resources.FailCount++;
                            }
                        }
                    }
                }
            }, null, Timeout.Infinite, Timeout.Infinite);
        }

        ~DnsPing()
        {
            if (isDisposed)
            {
                return;
            }

            Logger.Log("DnsPing destructor is called. You should dispose the instance explicitly.");
            Dispose();
        }

        public void Start()
        {
            if (Started)
                throw new Exception($"Called {nameof(Start)} again");
            Started = true;

            if (isDisposed)
                throw new Exception($"Called {nameof(Start)} after dispose");

            _sendTask.Change(0, DelayMs);
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;

            CancelToken.Cancel();
            _sendTask.Dispose();
            Client.Dispose();
            CancelToken.Dispose();
            GC.SuppressFinalize(this);
        }

        private static byte[] BuildDnsQueryPacket(ushort transactionId, string queryName = "example.com")
        {
            // https://www.rfc-editor.org/info/rfc1035/#section-4.1
            // Writes the DNS query packet to MemoryStream

            var queryNameBytes = EncodeDnsQueryName(queryName);

            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms))
                {
                    I16ToBigEndian(bw, transactionId);

                    // QR: 0 (query)
                    // OPCODE: 0000 (standard query)
                    // AA: 0 (no authoritative answer)
                    // TC: 0 (not truncated)
                    // RD: 1 (recursion desired. non recursion might cause some unexpected return w/o cached return)
                    // RA: 0 (recursion not available only used by dns server side)
                    // Z: 000 (reserved)
                    // RCODE: 0000 (response only used by dns server side)
                    I16ToBigEndian(bw, 0b0_0000_0_1_0_0_000_0000);

                    I16ToBigEndian(bw, 1); // QDCOUNT
                    I16ToBigEndian(bw, 0); // ANCOUNT
                    I16ToBigEndian(bw, 0); // NSCOUNT
                    I16ToBigEndian(bw, 0); // ARCOUNT

                    bw.Write(queryNameBytes); // QNAME. Called Write 2x so its big endian.

                    I16ToBigEndian(bw, 0x01); // QTYPE: A
                    I16ToBigEndian(bw, 0x01); // QCLASS: IN

                    return ms.ToArray();
                }
            }

            // Original bw.Write uses little-endian, so we need to convert ushort to big-endian manually
            void I16ToBigEndian(BinaryWriter bw, ushort value)
            {
                bw.Write((byte)(value >> 8));
                bw.Write((byte)(value & 0xFF));
            }
        }

        /// <summary>
        /// Encode the query name into DNS format
        /// </summary>
        /// <param name="queryName">The domain name to encode</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">occures when the dns label is not valid</exception>
        private static byte[] EncodeDnsQueryName(string queryName)
        {
            if (string.IsNullOrWhiteSpace(queryName))
            {
                throw new ArgumentException("Query name cannot be null or whitespace.", nameof(queryName));
            }

            string[] labels = queryName.TrimEnd('.').Split('.');
            using (var ms = new MemoryStream(8 * 3))
            {
                using (var bw = new BinaryWriter(ms))
                {
                    foreach (var label in labels)
                    {
                        byte length = (byte)label.Length;
                        if (length < 1 || length > 63)
                        {
                            throw new ArgumentException("Each DNS Label must be between 1 and 63 bytes.", nameof(queryName));
                        }
                        bw.Write(length);
                        bw.Write(Encoding.ASCII.GetBytes(label));
                    }
                    bw.Write((byte)0); // null terminator for the domain name
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Try extract DNS Query Transaction ID from received udp packet
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        private static (bool, ushort) GetDnsResponseTransactionId(byte[] packet)
        {
            if (packet == null || packet.Length < 12)
            {
                return (false, 0);
            }

            ushort responseTransactionId = (ushort)((packet[0] << 8) | packet[1]);
            bool isResponse = (packet[2] & 0x80) != 0;

            if (isResponse)
            {
                return (true, responseTransactionId);
            }
            else
            {
                return (false, 0);
            }
        }

        /// <summary>
        /// Return stocked ping statistics.
        /// Cleares the stocked pings after this.
        /// </summary>
        /// <returns></returns>
        public double[] ExtractQueuedPings()
        {
            lock (Resources)
            {
                var arr = Resources.stockedPings.ToArray();
                Resources.stockedPings.Clear();
                return arr;
            }
        }

        /// <summary>
        /// Return packet sucess and packet loss counts.
        /// </summary>
        /// <returns></returns>
        public (int Sucess, int Failure) GetSuccessFailureCounts()
        {
            lock (Resources)
            {
                return (Resources.SuccessCount, Resources.FailCount);
            }
        }
    }
}
