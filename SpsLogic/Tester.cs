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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpsLogic.Utils;

namespace SpsLogic
{
    public class Tester
    {
        private const string SteamPathResultFileName = "result.txt";

        static void Main(string[] args)
        {
            Trace.Listeners.Add(new ConsoleTraceListener());

            string command = args != null && args.Length >= 1 ? args[0] : "steam-path";
            if (string.Equals(command, "steam-path", StringComparison.OrdinalIgnoreCase))
            {
                TestFindSteamExeService();
                return;
            }

            if (string.Equals(command, "steam-path-powershell", StringComparison.OrdinalIgnoreCase))
            {
                TestRunningSteamExePathByPowerShell();
                return;
            }

            if (string.Equals(command, "packet-divert", StringComparison.OrdinalIgnoreCase))
            {
                TestPacketScanDivert(args.Skip(1).ToArray());
                return;
            }

            // TestClassicStunTransactionIdDictionary();
            // TestPacketAnalysis(args);
            // TestPacketScanDivert(args);
            // TestDnsPingDispose();
            // TestDnsPing();
            // TestWinPcap();
        }

        /// <summary>
        /// Tests steam.exe discovery through the C# service implementation.
        /// </summary>
        public static void TestFindSteamExeService()
        {
            Logger.Log("Starting steam.exe path lookup by FindSteamExeService.", true);

            try
            {
                var service = new FindSteamExeService();
                FindSteamExeResult result = service.Find();
                string resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SteamPathResultFileName);
                WriteSteamPathServiceResultForTest(resultPath, result);

                if (result.Candidates.Length == 0)
                {
                    Logger.Log("No steam.exe candidate was found.", true);
                    Logger.Log("Steam path result was written to: " + resultPath, true);
                    return;
                }

                for (int i = 0; i < result.Candidates.Length; i++)
                {
                    SteamExeCandidate candidate = result.Candidates[i];
                    Logger.Log(
                        "steam.exe candidate[" + i + "]: " +
                        candidate.Path +
                        ", source=" +
                        candidate.Source +
                        ", pid=" +
                        (candidate.ProcessId.HasValue ? candidate.ProcessId.Value.ToString() : "-"),
                        true);
                }

                Logger.Log("Best steam.exe path: " + result.BestPath, true);
                Logger.Log("Steam path result was written to: " + resultPath, true);
            }
            catch (Exception ex)
            {
                Logger.Log("FindSteamExeService lookup failed: " + ex.GetType().Name + ": " + ex.Message, true);
            }
        }

        /// <summary>
        /// Writes FindSteamExeService output to a result file for manual verification.
        /// </summary>
        /// <param name="resultPath">Destination file path. Existing files are replaced.</param>
        /// <param name="result">Service result to write. It must not be null.</param>
        private static void WriteSteamPathServiceResultForTest(string resultPath, FindSteamExeResult result)
        {
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }

            var lines = new List<string>
            {
                "FindSteamExeService result",
                "Best path: " + (string.IsNullOrWhiteSpace(result.BestPath) ? "-" : result.BestPath),
                "Candidates:"
            };

            if (result.Candidates.Length == 0)
            {
                lines.Add("No steam.exe candidate was found.");
            }
            else
            {
                foreach (SteamExeCandidate candidate in result.Candidates)
                {
                    lines.Add(
                        candidate.Path +
                        " | source=" +
                        candidate.Source +
                        " | pid=" +
                        (candidate.ProcessId.HasValue ? candidate.ProcessId.Value.ToString() : "-"));
                }
            }

            lines.Add("Messages:");
            lines.AddRange(result.Messages);

            File.WriteAllLines(resultPath, lines, Encoding.UTF8);
        }

        /// <summary>
        /// Tests steam.exe discovery through the legacy PowerShell command implementation.
        /// </summary>
        public static void TestRunningSteamExePathByPowerShell()
        {
            Logger.Log("Starting running steam.exe path lookup by PowerShell.", true);

            try
            {
                string output;
                string error;
                int exitCode = RunPowerShellForTest(
                    "Get-Process -Name steam -ErrorAction SilentlyContinue | Where-Object { $_.Path } | Select-Object -ExpandProperty Path",
                    out output,
                    out error);

                Logger.Log("PowerShell exit code: " + exitCode, true);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Logger.Log("PowerShell stderr: " + error.Trim(), true);
                }

                string[] paths = output
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                string resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SteamPathResultFileName);
                WriteSteamPathResultForTest(resultPath, paths, exitCode, error);

                if (paths.Length == 0)
                {
                    Logger.Log("No running steam.exe process was found.", true);
                    Logger.Log("Steam path result was written to: " + resultPath, true);
                    return;
                }

                for (int i = 0; i < paths.Length; i++)
                {
                    Logger.Log("Running steam.exe path[" + i + "]: " + paths[i], true);
                }

                Logger.Log("Steam path result was written to: " + resultPath, true);
            }
            catch (Exception ex)
            {
                Logger.Log("Running steam.exe path lookup failed: " + ex.GetType().Name + ": " + ex.Message, true);
            }
        }

        private static void WriteSteamPathResultForTest(string resultPath, string[] paths, int exitCode, string error)
        {
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }

            var lines = new List<string>
            {
                "PowerShell exit code: " + exitCode
            };

            if (!string.IsNullOrWhiteSpace(error))
            {
                lines.Add("PowerShell stderr:");
                lines.Add(error.Trim());
            }

            if (paths == null || paths.Length == 0)
            {
                lines.Add("No running steam.exe process was found.");
            }
            else
            {
                lines.Add("Running steam.exe path:");
                lines.AddRange(paths);
            }

            File.WriteAllLines(resultPath, lines, Encoding.UTF8);
        }

        private static int RunPowerShellForTest(string command, out string output, out string error)
        {
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedCommand,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (Process process = Process.Start(startInfo))
            {
                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        public static void TestWinPcap()
        {
            WinPcapInstallTest.TestDetection();
            WinPcapInstallTest.LogLicenseCheckNotes();
            Logger.Log(WinPcapInstallTest.GetBundledInstallerPath());
        }

        public static void TestPacketScanDivert(string[] args = null)
        {
            string host = args != null && args.Length >= 1 && !string.IsNullOrWhiteSpace(args[0])
                ? args[0]
                : "stunserver2025.stunprotocol.org";

            int port = 3478;
            if (args != null &&
                args.Length >= 2 &&
                int.TryParse(args[1], out int parsedPort) &&
                parsedPort > 0 &&
                parsedPort <= ushort.MaxValue)
            {
                port = parsedPort;
            }

            int runMilliseconds = 5000;
            if (args != null &&
                args.Length >= 3 &&
                int.TryParse(args[2], out int parsedRunMilliseconds) &&
                parsedRunMilliseconds > 0)
            {
                runMilliseconds = parsedRunMilliseconds;
            }

            double patienceMilliseconds = 3000;
            if (args != null &&
                args.Length >= 4 &&
                double.TryParse(args[3], out double parsedPatienceMilliseconds) &&
                parsedPatienceMilliseconds > 0)
            {
                patienceMilliseconds = parsedPatienceMilliseconds;
            }

            IPAddress address = ResolveIpv4AddressForTest(host);
            var endpoint = new IPEndPoint(address, port);
            ulong netId = PacketScanUtil.CalcNetId(endpoint.Address, checked((ushort)endpoint.Port));

            Logger.Log(
                $"Starting PacketScanDivert test: endpoint={endpoint}, netId={netId}, " +
                $"run={runMilliseconds}ms, patience={patienceMilliseconds}ms",
                true);

            PacketScanDivert scan = null;

            try
            {
                scan = new PacketScanDivert(patienceMilliseconds);
                scan.Register(netId, host, 0);
                LogPacketScanHistoriesForTest(scan, "after register");

                Thread.Sleep(300);

                string sendResult = SendClassicStunForDivertTest(endpoint);
                Logger.Log("PacketScanDivert STUN send result: " + sendResult, true);

                Stopwatch stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < runMilliseconds)
                {
                    scan.Update();
                    Thread.Sleep(500);
                }

                scan.Update();
                LogPacketScanHistoriesForTest(scan, "before unregister");

                scan.Unregister(netId);
                scan.Update();
                LogPacketScanHistoriesForTest(scan, "after unregister active");

                BasePlayerPingHistory[] oldHistories = scan.TakeUnseenOldHistories();
                Logger.Log("PacketScanDivert old histories: count=" + oldHistories.Length, true);
                for (int i = 0; i < oldHistories.Length; i++)
                {
                    LogHistoryStatsForTest("old[" + i + "]", oldHistories[i]);
                    oldHistories[i].Archive.SaveCaptureAsync("packet_divert_noop.pcap").Wait();
                    Logger.Log("PacketScanDivert archive no-op save completed for old[" + i + "].", true);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("PacketScanDivert test failed: " + ex.GetType().Name + ": " + ex.Message, true);
                if (ex.InnerException != null)
                {
                    Logger.Log("  Inner: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message, true);
                }
            }
            finally
            {
                try
                {
                    scan?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Log("PacketScanDivert dispose failed: " + ex.GetType().Name + ": " + ex.Message, true);
                }
            }

            Logger.Log("PacketScanDivert test finished.", true);
        }

        private static IPAddress ResolveIpv4AddressForTest(string host)
        {
            if (IPAddress.TryParse(host, out IPAddress address))
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return address;
                }

                throw new InvalidOperationException("Only IPv4 endpoints are supported by PacketScanDivert test.");
            }

            IPAddress resolved = Dns.GetHostAddresses(host)
                .FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork);

            if (resolved == null)
            {
                throw new InvalidOperationException("Could not resolve IPv4 address: " + host);
            }

            return resolved;
        }

        private static string SendClassicStunForDivertTest(IPEndPoint endpoint)
        {
            byte[] request = BuildClassicStunRequestForTest();
            ClassicStunTransactionId id = new ClassicStunTransactionId(request, 4);

            using (var client = new UdpClient())
            {
                client.Client.ReceiveTimeout = 1500;
                client.Connect(endpoint);
                client.Send(request, request.Length);

                try
                {
                    IPEndPoint remote = null;
                    byte[] response = client.Receive(ref remote);
                    return $"Response received: remote={remote}, bytes={response.Length}, id={id}";
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        return "No UDP response within 1500ms. id=" + id;
                    }

                    throw;
                }
            }
        }

        private static byte[] BuildClassicStunRequestForTest()
        {
            const int classicStunRequestLength = 56;
            const int classicStunHeaderLength = 20;

            var bytes = new byte[classicStunRequestLength];
            WriteUInt16BigEndianForTest(bytes, 0, 0x0001);
            WriteUInt16BigEndianForTest(bytes, 2, classicStunRequestLength - classicStunHeaderLength);

            var transactionBytes = new byte[16];
            new Random().NextBytes(transactionBytes);
            Buffer.BlockCopy(transactionBytes, 0, bytes, 4, transactionBytes.Length);

            WriteUInt16BigEndianForTest(bytes, 20, 0x8022);
            WriteUInt16BigEndianForTest(bytes, 22, 32);

            return bytes;
        }

        private static void WriteUInt16BigEndianForTest(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 1] = (byte)(value & 0xFF);
        }

        private static void LogPacketScanHistoriesForTest(IPacketScan scan, string label)
        {
            int count = 0;
            scan.ForEachActiveHistory((state, netId, history) =>
            {
                count++;
                LogHistoryStatsForTest(label + " active netId=" + (netId.HasValue ? netId.Value.ToString() : "none"), history);
            });

            Logger.Log("PacketScanDivert " + label + ": activeCount=" + count, true);
        }

        private static void LogHistoryStatsForTest(string label, BasePlayerPingHistory history)
        {
            if (history == null)
            {
                Logger.Log("PacketScanDivert " + label + ": history=null", true);
                return;
            }

            history.Stats.ReadValues((name, startedAt, min, max, avg, loss, q1, med, q3, recentPings) =>
            {
                Logger.Log(
                    $"PacketScanDivert {label}: name={name}, startedAt={startedAt:HH:mm:ss}, " +
                    $"min={min:F1}, max={max:F1}, avg={avg:F1}, loss={loss:F1}, " +
                    $"q1={q1:F1}, med={med:F1}, q3={q3:F1}, recent=[{string.Join(",", recentPings ?? new double[0])}]",
                    true);
                return 0;
            });
        }

        public static void TestSteamAppFinderEnumWindows(int iterationCount = 10)
        {
            if (iterationCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(iterationCount));
            }

            Logger.Log($"Starting SteamAppFinder.EnumWindows test: iterations={iterationCount}");
            LogSteamAppFinderEnumWindowsFilterStats();

            var finder = new SteamAppFinder();
            var warmupWindows = new List<WindowInfo>();
            finder.EnumWindows(warmupWindows.Add);
            VerifySteamAppFinderWindows(warmupWindows, "warmup");

            int totalWindowCount = 0;
            int minWindowCount = int.MaxValue;
            int maxWindowCount = 0;
            int firstWindowCount = -1;
            int lastWindowCount = -1;
            List<WindowInfo> lastWindows = null;

            TimeSpan started = Logger.GetTimestamp();
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                var windows = new List<WindowInfo>();
                finder.EnumWindows(windows.Add);
                VerifySteamAppFinderWindows(windows, "iteration " + iteration);

                if (iteration == 0)
                {
                    firstWindowCount = windows.Count;
                }

                lastWindowCount = windows.Count;
                totalWindowCount += windows.Count;
                minWindowCount = Math.Min(minWindowCount, windows.Count);
                maxWindowCount = Math.Max(maxWindowCount, windows.Count);
                lastWindows = windows;
            }

            stopwatch.Stop();

            double totalMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            double averageIterationMilliseconds = totalMilliseconds / iterationCount;
            double averageWindowMilliseconds = totalWindowCount == 0
                ? 0.0
                : totalMilliseconds / totalWindowCount;

            Logger.LogWithTimestamp("SteamAppFinder.EnumWindows measured by Logger", started);
            Logger.Log(
                $"SteamAppFinder.EnumWindows: iterations={iterationCount}, totalWindows={totalWindowCount}, " +
                $"first={firstWindowCount}, last={lastWindowCount}, min={minWindowCount}, max={maxWindowCount}, " +
                $"total={totalMilliseconds:F3} ms, avgIteration={averageIterationMilliseconds:F3} ms, " +
                $"avgWindow={averageWindowMilliseconds:F4} ms");

            if (lastWindows != null)
            {
                foreach (WindowInfo window in lastWindows.Take(10))
                {
                    Logger.Log(
                        $"SteamAppFinder.EnumWindows sample: hwnd=0x{window.Handle.ToInt64():X}, " +
                        $"pid={window.ProcessId}, thread={window.ThreadId}, title=\"{window.Title}\", " +
                        $"process=\"{window.ProcessName}\", path=\"{window.ProcessPath}\"");
                }
            }

            Logger.Log("SteamAppFinder.EnumWindows test passed.");
        }

        private sealed class EnumWindowsFilterStats
        {
            public int Raw;
            public int Shell;
            public int Visible;
            public int HasTitleLength;
            public int TextRead;
            public int HasProcessId;
            public int OpenProcessSuccess;
            public int QueryImageSuccess;
            public string FirstOpenProcessError;
            public string FirstQueryImageError;
        }

        private static void LogSteamAppFinderEnumWindowsFilterStats()
        {
            EnumWindowsFilterStats stats = CollectSteamAppFinderEnumWindowsFilterStats();

            Logger.Log(
                "SteamAppFinder.EnumWindows filters: " +
                $"raw={stats.Raw}, shell={stats.Shell}, visible={stats.Visible}, " +
                $"hasTitleLength={stats.HasTitleLength}, textRead={stats.TextRead}, " +
                $"hasProcessId={stats.HasProcessId}, openProcessSuccess={stats.OpenProcessSuccess}, " +
                $"queryImageSuccess={stats.QueryImageSuccess}, " +
                $"firstOpenProcessError={stats.FirstOpenProcessError ?? "none"}, " +
                $"firstQueryImageError={stats.FirstQueryImageError ?? "none"}");
        }

        private static EnumWindowsFilterStats CollectSteamAppFinderEnumWindowsFilterStats()
        {
            var stats = new EnumWindowsFilterStats();
            IntPtr shellWindow = WinApi.GetShellWindow();

            WinApi.EnumWindows((hWnd, lParam) =>
            {
                stats.Raw++;

                if (hWnd == shellWindow)
                {
                    stats.Shell++;
                    return true;
                }

                if (!WinApi.IsWindowVisible(hWnd))
                {
                    return true;
                }

                stats.Visible++;

                int length = WinApi.GetWindowTextLength(hWnd);
                if (length == 0)
                {
                    return true;
                }

                stats.HasTitleLength++;

                var builder = new StringBuilder(length + 1);
                if (WinApi.GetWindowText(hWnd, builder, length + 1) <= 0)
                {
                    return true;
                }

                stats.TextRead++;

                WinApi.GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == 0)
                {
                    return true;
                }

                stats.HasProcessId++;

                IntPtr processHandle = WinApi.OpenProcess(
                    WinApi.ProcessAccessFlags.QueryLimitedInformation,
                    false,
                    (int)processId);

                if (processHandle == IntPtr.Zero)
                {
                    if (stats.FirstOpenProcessError == null)
                    {
                        stats.FirstOpenProcessError = CreateLastWin32ErrorMessage("OpenProcess");
                    }

                    return true;
                }

                try
                {
                    stats.OpenProcessSuccess++;

                    int capacity = 32768;
                    var imageBuilder = new StringBuilder(capacity);
                    if (!WinApi.QueryFullProcessImageName(processHandle, 0, imageBuilder, ref capacity))
                    {
                        if (stats.FirstQueryImageError == null)
                        {
                            stats.FirstQueryImageError = CreateLastWin32ErrorMessage("QueryFullProcessImageName");
                        }

                        return true;
                    }

                    if (!string.IsNullOrEmpty(imageBuilder.ToString()))
                    {
                        stats.QueryImageSuccess++;
                    }
                }
                finally
                {
                    WinApi.CloseHandle(processHandle);
                }

                return true;
            }, 0);

            return stats;
        }

        private static void VerifySteamAppFinderWindows(
            List<WindowInfo> windows,
            string caseName)
        {
            AssertForTest(windows != null, caseName + ": windows should not be null.");

            var handles = new HashSet<IntPtr>();

            foreach (WindowInfo window in windows)
            {
                AssertForTest(window != null, caseName + ": WindowInfo should not be null.");
                AssertForTest(window.Handle != IntPtr.Zero, caseName + ": window handle should not be zero.");
                AssertForTest(!string.IsNullOrEmpty(window.Title), caseName + ": title should not be empty.");
                AssertForTest(window.ProcessId != 0, caseName + ": process id should not be zero.");
                AssertForTest(window.ThreadId != 0, caseName + ": thread id should not be zero.");
                AssertForTest(!string.IsNullOrEmpty(window.ProcessPath), caseName + ": process path should not be empty.");
                AssertForTest(!string.IsNullOrEmpty(window.ProcessName), caseName + ": process name should not be empty.");

                if (!WinApi.IsWindow(window.Handle))
                {
                    Logger.Log($"{caseName}: window handle was already destroyed: 0x{window.Handle.ToInt64():X}");
                    continue;
                }

                if (!WinApi.IsWindowVisible(window.Handle))
                {
                    Logger.Log($"{caseName}: window handle was no longer visible: 0x{window.Handle.ToInt64():X}");
                }

                string expectedProcessName = Path.GetFileNameWithoutExtension(window.ProcessPath);
                AssertForTest(
                    string.Equals(window.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase),
                    $"{caseName}: process name should match path. actual={window.ProcessName}, expected={expectedProcessName}");

                AssertForTest(
                    handles.Add(window.Handle),
                    $"{caseName}: duplicate window handle was returned: 0x{window.Handle.ToInt64():X}");
            }
        }

        public static void TestApproximatePath()
        {
            Logger.Log("Starting ApproximatePath tests.");

            AssertApproximatePathEquals(
                "a/b/c/x.txt",
                "a/b/c/y.txt",
                true,
                "same directory should be treated as equal");

            AssertApproximatePathEquals(
                "a/b/c/x.txt",
                "a/b/y.txt",
                true,
                "child directory and parent directory should be treated as equal");

            AssertApproximatePathEquals(
                "a/b/y.txt",
                "a/b/c/x.txt",
                true,
                "parent directory and child directory should be treated as equal");

            AssertApproximatePathEquals(
                "a/b/d/z.txt",
                "a/b/e/w.txt",
                false,
                "sibling directories should not be treated as equal");

            AssertApproximatePathDictionaryKey(
                "a/b/c/x.txt",
                "a/b/c/y.txt",
                true,
                "same directory should be usable as a Dictionary key");

            AssertApproximatePathDictionaryKey(
                "a/b/c/x.txt",
                "a/b/y.txt",
                true,
                "child and parent directories should be usable as a Dictionary key");

            AssertApproximatePathDictionaryKey(
                "a/b/y.txt",
                "a/b/c/x.txt",
                true,
                "parent and child directories should be usable as a Dictionary key");

            AssertApproximatePathDictionaryKey(
                "a/b/d/z.txt",
                "a/b/e/w.txt",
                false,
                "sibling directories should not collide as a Dictionary key");

            Logger.Log("ApproximatePath tests passed.");
        }

        private static void AssertApproximatePathEquals(
            string leftPath,
            string rightPath,
            bool expected,
            string message)
        {
            var left = new SteamAppFinder.ApproximatePath(leftPath);
            var right = new SteamAppFinder.ApproximatePath(rightPath);
            bool actual = left.Equals(right);

            Logger.Log(
                $"ApproximatePath.Equals: left=\"{leftPath}\", right=\"{rightPath}\", expected={expected}, actual={actual}");

            AssertForTest(
                actual == expected,
                $"{message}: left=\"{leftPath}\", right=\"{rightPath}\", expected={expected}, actual={actual}");
        }

        private static void AssertApproximatePathDictionaryKey(
            string storedPath,
            string lookupPath,
            bool expected,
            string message)
        {
            var dictionary = new Dictionary<SteamAppFinder.ApproximatePath, string>();
            dictionary[new SteamAppFinder.ApproximatePath(storedPath)] = "detected";

            bool actual = dictionary.ContainsKey(new SteamAppFinder.ApproximatePath(lookupPath));

            Logger.Log(
                $"ApproximatePath Dictionary: stored=\"{storedPath}\", lookup=\"{lookupPath}\", expected={expected}, actual={actual}");

            AssertForTest(
                actual == expected,
                $"{message}: stored=\"{storedPath}\", lookup=\"{lookupPath}\", expected={expected}, actual={actual}");
        }

        public static void TestProcessPathLookupCost(int iterationCount = 5)
        {
            if (iterationCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(iterationCount));
            }

            Process[] processes = Process.GetProcesses()
                .OrderBy(process => process.Id)
                .ToArray();

            Logger.Log(
                $"Starting process path lookup cost test: processes={processes.Length}, iterations={iterationCount}");

            // Warm up JIT and common framework paths before measuring the full process set.
            using (Process currentProcess = Process.GetCurrentProcess())
            {
                string warmupPath;
                string warmupError;
                TryGetProcessPathByQueryFullProcessImageName(currentProcess, out warmupPath, out warmupError);
                TryGetProcessPathByMainModule(currentProcess, out warmupPath, out warmupError);
            }

            RunProcessPathLookupCostCase(
                "OpenProcess + QueryFullProcessImageName",
                processes,
                iterationCount,
                TryGetProcessPathByQueryFullProcessImageName);

            RunProcessPathLookupCostCase(
                "Process.MainModule.FileName",
                processes,
                iterationCount,
                TryGetProcessPathByMainModule);

            foreach (Process process in processes)
            {
                process.Dispose();
            }

            Logger.Log("Finished process path lookup cost test.");
        }

        private delegate bool TryGetProcessPathDelegate(
            Process process,
            out string processPath,
            out string error);

        private static void RunProcessPathLookupCostCase(
            string caseName,
            Process[] processes,
            int iterationCount,
            TryGetProcessPathDelegate tryGetProcessPath)
        {
            int successCount = 0;
            int failureCount = 0;
            long totalPathLength = 0;
            string firstFailure = null;

            TimeSpan started = Logger.GetTimestamp();
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                foreach (Process process in processes)
                {
                    string path;
                    string error;
                    if (tryGetProcessPath(process, out path, out error))
                    {
                        successCount++;
                        totalPathLength += path == null ? 0 : path.Length;
                    }
                    else
                    {
                        failureCount++;
                        if (firstFailure == null)
                        {
                            firstFailure = $"pid={SafeGetProcessId(process)}, error={error}";
                        }
                    }
                }
            }

            stopwatch.Stop();

            int attemptCount = processes.Length * iterationCount;
            double totalMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            double averageMilliseconds = attemptCount == 0
                ? 0.0
                : totalMilliseconds / attemptCount;

            Logger.LogWithTimestamp(caseName + " measured by Logger", started);
            Logger.Log(
                $"{caseName}: attempts={attemptCount}, success={successCount}, failure={failureCount}, " +
                $"total={totalMilliseconds:F3} ms, avg={averageMilliseconds:F4} ms, " +
                $"pathChars={totalPathLength}, firstFailure={firstFailure ?? "none"}");
        }

        private static bool TryGetProcessPathByQueryFullProcessImageName(
            Process process,
            out string processPath,
            out string error)
        {
            processPath = string.Empty;
            error = null;

            IntPtr processHandle = WinApi.OpenProcess(
                WinApi.ProcessAccessFlags.QueryLimitedInformation,
                false,
                SafeGetProcessId(process));

            if (processHandle == IntPtr.Zero)
            {
                error = CreateLastWin32ErrorMessage("OpenProcess");
                return false;
            }

            try
            {
                int capacity = 32768;
                var builder = new StringBuilder(capacity);

                if (!WinApi.QueryFullProcessImageName(processHandle, 0, builder, ref capacity))
                {
                    error = CreateLastWin32ErrorMessage("QueryFullProcessImageName");
                    return false;
                }

                processPath = builder.ToString();
                return !string.IsNullOrEmpty(processPath);
            }
            finally
            {
                WinApi.CloseHandle(processHandle);
            }
        }

        private static bool TryGetProcessPathByMainModule(
            Process process,
            out string processPath,
            out string error)
        {
            processPath = string.Empty;
            error = null;

            try
            {
                processPath = process.MainModule?.FileName ?? string.Empty;
                return !string.IsNullOrEmpty(processPath);
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static int SafeGetProcessId(Process process)
        {
            try
            {
                return process.Id;
            }
            catch
            {
                return 0;
            }
        }

        private static string CreateLastWin32ErrorMessage(string apiName)
        {
            var exception = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return $"{apiName} failed: {exception.Message}";
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
