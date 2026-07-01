    using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpsLogic.Utils;

namespace SpsLogic
{
    public class Tester
    {
        static void Main(string[] args)
        {
            Trace.Listeners.Add(new ConsoleTraceListener());
            TestClassicStunTransactionIdDictionary();
            // TestPacketAnalysis(args);
            // TestDnsPingDispose();
            // TestDnsPing();
        }

        public static void TestClassicStunTransactionIdDictionary()
        {
            Logger.Log("Starting ClassicStunTransactionId dictionary test.");

            byte[] idBytes0 =
            {
                0x10, 0x11, 0x12, 0x13,
                0x20, 0x21, 0x22, 0x23,
                0x30, 0x31, 0x32, 0x33,
                0x40, 0x41, 0x42, 0x43
            };

            byte[] idBytes0Copy = idBytes0.ToArray();
            byte[] idBytes1 =
            {
                0x10, 0x11, 0x12, 0x13,
                0x20, 0x21, 0x22, 0x23,
                0x30, 0x31, 0x32, 0x33,
                0x40, 0x41, 0x42, 0x44
            };

            var id0 = new ClassicStunTransactionId(idBytes0, 0);
            var id0Copy = new ClassicStunTransactionId(idBytes0Copy, 0);
            var id1 = new ClassicStunTransactionId(idBytes1, 0);

            var dictionary = new Dictionary<ClassicStunTransactionId, string>();
            dictionary.Add(id0, "first");

            AssertForTest(id0.Equals(id0Copy), "Equal transaction IDs should compare equal.");
            AssertForTest(id0.GetHashCode() == id0Copy.GetHashCode(), "Equal transaction IDs should have the same hash code.");
            AssertForTest(dictionary.ContainsKey(id0Copy), "Dictionary should find an equivalent transaction ID key.");
            AssertForTest(dictionary[id0Copy] == "first", "Dictionary should return the value using an equivalent key.");
            AssertForTest(!dictionary.ContainsKey(id1), "Dictionary should not find a different transaction ID.");

            dictionary[id0Copy] = "updated";
            AssertForTest(dictionary.Count == 1, "Assigning an equivalent key should not add a new entry.");
            AssertForTest(dictionary[id0] == "updated", "Assigning through an equivalent key should update the original entry.");

            dictionary.Add(id1, "second");
            AssertForTest(dictionary.Count == 2, "Adding a different transaction ID should add a new entry.");
            AssertForTest(dictionary[id1] == "second", "Dictionary should return the value for the different transaction ID.");

            Logger.Log($"id0={id0}");
            Logger.Log($"id1={id1}");
            Logger.Log("ClassicStunTransactionId dictionary test passed.");
        }

        private static void AssertForTest(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static void TestDnsPingDispose()
        {
            Logger.Log("Starting DnsPing dispose tests.");

            var endpoint = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53);

            RunDisposeBeforeStartCase(endpoint);
            RunDisposeAfterStartCase(endpoint);
            RunDisposePublicMethodCase(endpoint);

            Logger.Log("Finished DnsPing dispose tests.");
        }

        private static void RunDisposeBeforeStartCase(IPEndPoint endpoint)
        {
            Logger.Log("Dispose case start: Dispose before Start.");

            DnsPing ping = null;

            try
            {
                ping = new DnsPing(endpoint, "example.com", delayMs: 1000, patienceLimitMs: 3000);
                ping.Dispose();
                Logger.Log("Dispose before Start completed.");

                Thread.Sleep(1500);

                var status = GetReceiveTaskStatusForTest(ping);
                Logger.Log($"Dispose before Start receive task status after wait: {status}");

                ping.Dispose();
                Logger.Log("Dispose before Start second Dispose completed.");
            }
            catch (Exception ex)
            {
                Logger.Log($"Dispose before Start exception: type={ex.GetType().Name}, message={ex.Message}");
            }
            finally
            {
                try
                {
                    ping?.Dispose();
                }
                catch
                {
                }
            }
        }

        private static void RunDisposeAfterStartCase(IPEndPoint endpoint)
        {
            Logger.Log("Dispose case start: Dispose after Start.");

            DnsPing ping = null;

            try
            {
                ping = new DnsPing(endpoint, "example.com", delayMs: 1000, patienceLimitMs: 3000);
                ping.Start();
                Thread.Sleep(2500);

                var beforeDisposeCounts = ping.GetSuccessFailureCounts();
                var beforeDisposePings = ping.ExtractQueuedPings();
                var beforeDisposeLosses = beforeDisposePings.Count(value => value < 0);
                var beforeDisposeValidPings = beforeDisposePings.Where(value => value >= 0).ToArray();
                Logger.Log(
                    $"Before Dispose: success={beforeDisposeCounts.Sucess}, failure={beforeDisposeCounts.Failure}, queued={beforeDisposePings.Length}, loss={beforeDisposeLosses}, pings=[{string.Join(",", beforeDisposeValidPings)}]");

                ping.Dispose();
                Logger.Log("Dispose after Start completed.");

                Thread.Sleep(3500);

                var afterDisposeCounts = ping.GetSuccessFailureCounts();
                var afterDisposePings = ping.ExtractQueuedPings();
                var afterDisposeLosses = afterDisposePings.Count(value => value < 0);
                var afterDisposeValidPings = afterDisposePings.Where(value => value >= 0).ToArray();
                var status = GetReceiveTaskStatusForTest(ping);

                Logger.Log(
                    $"After Dispose wait: success={afterDisposeCounts.Sucess}, failure={afterDisposeCounts.Failure}, queued={afterDisposePings.Length}, loss={afterDisposeLosses}, pings=[{string.Join(",", afterDisposeValidPings)}], receiveTask={status}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Dispose after Start exception: type={ex.GetType().Name}, message={ex.Message}");
            }
            finally
            {
                try
                {
                    ping?.Dispose();
                }
                catch
                {
                }
            }
        }

        private static void RunDisposePublicMethodCase(IPEndPoint endpoint)
        {
            Logger.Log("Dispose case start: public methods after Dispose.");

            DnsPing ping = null;

            try
            {
                ping = new DnsPing(endpoint, "example.com", delayMs: 1000, patienceLimitMs: 3000);
                ping.Start();
                Thread.Sleep(1200);
                ping.Dispose();

                try
                {
                    var counts = ping.GetSuccessFailureCounts();
                    Logger.Log($"GetSuccessFailureCounts after Dispose: success={counts.Sucess}, failure={counts.Failure}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"GetSuccessFailureCounts after Dispose exception: type={ex.GetType().Name}, message={ex.Message}");
                }

                try
                {
                    var pings = ping.ExtractQueuedPings();
                    var losses = pings.Count(value => value < 0);
                    var validPings = pings.Where(value => value >= 0).ToArray();
                    Logger.Log($"ExtractQueuedPings after Dispose: queued={pings.Length}, loss={losses}, pings=[{string.Join(",", validPings)}]");
                }
                catch (Exception ex)
                {
                    Logger.Log($"ExtractQueuedPings after Dispose exception: type={ex.GetType().Name}, message={ex.Message}");
                }

                try
                {
                    ping.Start();
                    Logger.Log("Start after Dispose did not throw.");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Start after Dispose exception: type={ex.GetType().Name}, message={ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Public methods after Dispose case exception: type={ex.GetType().Name}, message={ex.Message}");
            }
            finally
            {
                try
                {
                    ping?.Dispose();
                }
                catch
                {
                }
            }
        }

        private static string GetReceiveTaskStatusForTest(DnsPing ping)
        {
            if (ping == null)
            {
                return "null";
            }

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

            try
            {
                var task = typeof(DnsPing).GetField("_receiveTask", Flags)?.GetValue(ping) as Task;
                if (task == null)
                {
                    return "missing";
                }

                if (task.IsFaulted)
                {
                    return $"Faulted: {task.Exception?.GetBaseException().GetType().Name}: {task.Exception?.GetBaseException().Message}";
                }

                return task.Status.ToString();
            }
            catch (Exception ex)
            {
                return $"inspect failed: {ex.GetType().Name}: {ex.Message}";
            }
        }

        public static void TestDnsPing()
        {
            var services = new[]
            {
                new { Name = "Cloudflare", EndPoint = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53) },
                new { Name = "Google", EndPoint = new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53) },
                new { Name = "Quad9", EndPoint = new IPEndPoint(IPAddress.Parse("9.9.9.9"), 53) },
            };

            var queryNames = new[]
            {
                "example.com",
                "cloudflare.com",
                "google.com",
                "quad9.net",
            };

            Logger.Log("Starting DnsPing smoke tests.");

            foreach (var service in services)
            {
                foreach (var queryName in queryNames)
                {
                    RunDnsPingCase(
                        service.Name,
                        service.EndPoint,
                        queryName,
                        delayMs: 1000,
                        patienceLimitMs: 3000,
                        runMilliseconds: 3500);
                }
            }

            RunDnsPingConstructorCase(
                "Invalid query: empty string",
                new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53),
                string.Empty,
                delayMs: 1000,
                patienceLimitMs: 3000);

            RunDnsPingConstructorCase(
                "Invalid delay: zero",
                new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53),
                "example.com",
                delayMs: 0,
                patienceLimitMs: 3000);

            RunDnsPingPublicOrderCase();

            Logger.Log("Finished DnsPing smoke tests.");
        }

        private static void RunDnsPingCase(
            string serviceName,
            IPEndPoint endPoint,
            string queryName,
            int delayMs,
            int patienceLimitMs,
            int runMilliseconds)
        {
            Logger.Log(
                $"DnsPing case start: service={serviceName}, endpoint={endPoint}, query={queryName}, delay={delayMs}ms, patience={patienceLimitMs}ms");

            DnsPing ping = null;

            try
            {
                ping = new DnsPing(endPoint, queryName, delayMs, patienceLimitMs);

                var initialCounts = ping.GetSuccessFailureCounts();
                var initialPings = ping.ExtractQueuedPings();
                Logger.Log(
                    $"Before Start: success={initialCounts.Sucess}, failure={initialCounts.Failure}, queued={initialPings.Length}");

                ping.Start();
                Thread.Sleep(runMilliseconds);

                var pings = ping.ExtractQueuedPings();
                var counts = ping.GetSuccessFailureCounts();
                var losses = pings.Count(value => value < 0);
                var validPings = pings.Where(value => value >= 0).ToArray();

                Logger.Log(
                    $"DnsPing case result: service={serviceName}, query={queryName}, success={counts.Sucess}, failure={counts.Failure}, queued={pings.Length}, loss={losses}, pings=[{string.Join(",", validPings)}]");
            }
            catch (Exception ex)
            {
                Logger.Log(
                    $"DnsPing case exception: service={serviceName}, query={queryName}, type={ex.GetType().Name}, message={ex.Message}");
            }
            finally
            {
                if (ping != null)
                {
                    try
                    {
                        StopDnsPingInternalsForTest(ping);
                        ping.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Dispose exception: type={ex.GetType().Name}, message={ex.Message}");
                    }
                }
            }
        }

        private static void RunDnsPingConstructorCase(
            string caseName,
            IPEndPoint endPoint,
            string queryName,
            int delayMs,
            int patienceLimitMs)
        {
            Logger.Log($"DnsPing constructor case start: {caseName}");

            try
            {
                var ping = new DnsPing(endPoint, queryName, delayMs, patienceLimitMs);
                try
                {
                    var counts = ping.GetSuccessFailureCounts();
                    Logger.Log(
                        $"DnsPing constructor case created: {caseName}, success={counts.Sucess}, failure={counts.Failure}");
                }
                finally
                {
                    StopDnsPingInternalsForTest(ping);
                    ping.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Log(
                    $"DnsPing constructor case exception: {caseName}, type={ex.GetType().Name}, message={ex.Message}");
            }
        }

        private static void RunDnsPingPublicOrderCase()
        {
            Logger.Log("DnsPing public order case start.");

            DnsPing ping = null;

            try
            {
                ping = new DnsPing(
                    new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53),
                    "example.com",
                    delayMs: 1000,
                    patienceLimitMs: 3000);

                var counts0 = ping.GetSuccessFailureCounts();
                Logger.Log($"Order case counts before Start: success={counts0.Sucess}, failure={counts0.Failure}");

                ping.Start();

                try
                {
                    ping.Start();
                    Logger.Log("Order case second Start did not throw.");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Order case second Start exception: type={ex.GetType().Name}, message={ex.Message}");
                }

                Thread.Sleep(2500);

                var pings0 = ping.ExtractQueuedPings();
                var pings1 = ping.ExtractQueuedPings();
                var losses0 = pings0.Count(value => value < 0);
                var losses1 = pings1.Count(value => value < 0);
                Logger.Log($"Order case queued first={pings0.Length}, firstLoss={losses0}, second={pings1.Length}, secondLoss={losses1}");

                StopDnsPingInternalsForTest(ping);
                ping.Dispose();
                ping.Dispose();
                Logger.Log("Order case double Dispose completed.");

                try
                {
                    ping.Start();
                    Logger.Log("Order case Start after Dispose did not throw.");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Order case Start after Dispose exception: type={ex.GetType().Name}, message={ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"DnsPing public order case exception: type={ex.GetType().Name}, message={ex.Message}");
            }
            finally
            {
                if (ping != null)
                {
                    try
                    {
                        ping.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void StopDnsPingInternalsForTest(DnsPing ping)
        {
            if (ping == null)
            {
                return;
            }

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

            try
            {
                var timer = typeof(DnsPing).GetField("_sendTask", Flags)?.GetValue(ping) as Timer;
                if (timer != null)
                {
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                    timer.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Test cleanup timer exception: type={ex.GetType().Name}, message={ex.Message}");
            }

            try
            {
                var client = typeof(DnsPing).GetField("Client", Flags)?.GetValue(ping) as UdpClient;
                if (client != null)
                {
                    client.Close();
                    client.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Test cleanup client exception: type={ex.GetType().Name}, message={ex.Message}");
            }
        }

        public static void TestPacketAnalysis(string[] args = null)
        {
            var l0 = Logger.GetTimestamp();
            var devices = LibPcapLiveDeviceList.Instance;

            if (devices.Count == 0)
            {
                Console.WriteLine("No capture devices found.");
                return;
            }

            for (int i = 0; i < devices.Count; i++)
            {
                Console.WriteLine($"{i}: {devices[i].Name} - {devices[i].Description}");
            }

            Logger.LogWithTimestamp("Capture preparation time", l0);

            int index;
            if (!TryGetCaptureDeviceIndex(args, devices.Count, out index))
            {
                Console.WriteLine("Invalid index.");
                return;
            }

            int captureMilliseconds = 5000;
            if (args != null &&
                args.Length >= 2 &&
                int.TryParse(args[1], out int parsedCaptureMilliseconds) &&
                parsedCaptureMilliseconds > 0)
            {
                captureMilliseconds = parsedCaptureMilliseconds;
            }

            bool nonInteractiveMode = args != null && args.Length >= 1;

            Console.WriteLine($"Capturing UDP packets on device {index} for {captureMilliseconds}ms.");
            if (!nonInteractiveMode)
            {
                Console.WriteLine("Press Enter to stop early.");
            }

            var fileName = $"packet_archive_{DateTime.Now:yyyyMMdd_HHmmss}.pcap";
            long capturedCount = 0;
            long archivedCount = 0;
            long droppedByArchive = 0;

            using (var archive = new PacketScan.PacketArchive())
            using (var device = devices[index])
            {
                device.OnPacketArrival += (sender, e) =>
                {
                    var raw = e.GetPacket();
                    var current = Interlocked.Increment(ref capturedCount);

                    try
                    {
                        archive.AddCapture(raw);
                        Interlocked.Increment(ref archivedCount);
                    }
                    catch (InvalidOperationException)
                    {
                        Interlocked.Increment(ref droppedByArchive);
                    }

                    if (current <= 25)
                    {
                        PrintPacketSummary(raw, current);
                    }
                };

                try
                {
                    device.Open();
                    device.Filter = "udp"; // can use this after open
                    device.StartCapture();

                    if (nonInteractiveMode)
                    {
                        Thread.Sleep(captureMilliseconds);
                    }
                    else
                    {
                        var stopTask = Task.Run(() => Console.ReadLine());
                        Task.WaitAny(stopTask, Task.Delay(captureMilliseconds));
                    }
                }
                finally
                {
                    try
                    {
                        device.StopCapture();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"StopCapture exception: type={ex.GetType().Name}, message={ex.Message}");
                    }

                    try
                    {
                        device.Close();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Device close exception: type={ex.GetType().Name}, message={ex.Message}");
                    }
                }

                Logger.Log(
                    $"Capture stopped: captured={capturedCount}, archived={archivedCount}, droppedByArchive={droppedByArchive}");

                try
                {
                    var saveTask = archive.SaveCaptureAsync(fileName);
                    saveTask.Wait();
                    var savedPath = Path.GetFullPath(Path.Combine("archives", fileName));
                    Logger.Log($"Packet archive saved: {savedPath}");
                }
                catch (Exception ex)
                {
                    var baseException = ex is AggregateException aggregateException
                        ? aggregateException.GetBaseException()
                        : ex;

                    Logger.Log($"Packet archive save exception: type={baseException.GetType().Name}, message={baseException.Message}");
                }
            }
        }

        private static bool TryGetCaptureDeviceIndex(string[] args, int deviceCount, out int index)
        {
            if (args != null &&
                args.Length >= 1 &&
                int.TryParse(args[0], out index) &&
                index >= 0 &&
                index < deviceCount)
            {
                return true;
            }

            Console.WriteLine("Select device index:");
            return int.TryParse(Console.ReadLine(), out index) &&
                index >= 0 &&
                index < deviceCount;
        }

        private static void PrintPacketSummary(RawCapture raw, long packetNumber)
        {
            Packet packet;

            try
            {
                packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            }
            catch
            {
                Console.WriteLine($"{packetNumber}: parse failed, bytes={raw.Data?.Length ?? 0}");
                return;
            }

            var ip = packet.Extract<IPPacket>();
            var udp = packet.Extract<UdpPacket>();

            if (ip == null || udp == null)
            {
                Console.WriteLine($"{packetNumber}: non-ip/udp, bytes={raw.Data?.Length ?? 0}");
                return;
            }

            byte[] payload = udp.PayloadData ?? Array.Empty<byte>();

            Console.WriteLine(
                $"{packetNumber}: {ip.SourceAddress}:{udp.SourcePort} -> " +
                $"{ip.DestinationAddress}:{udp.DestinationPort} " +
                $"payload={payload.Length} bytes"
            );
        }
    }
}
