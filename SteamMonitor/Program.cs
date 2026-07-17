using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SpsLogic;

namespace SteamMonitor
{
    internal static class Program
    {
        private const int Success = 0;
        private const int InvalidArguments = 2;
        private const int SteamApiInitFailed = 3;
        private const int UnhandledError = 10;

        private static int Main(string[] args)
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;

            var interpreter = new SteamMonitorInterpreter(Console.Out);

            try
            {
                var options = SteamMonitorOptions.Parse(args);
                if (string.IsNullOrWhiteSpace(options.SteamAppId) ||
                    string.IsNullOrWhiteSpace(options.GameProcessName))
                {
                    interpreter.Error("steamAppId and gameProcessName are required.");
                    return InvalidArguments;
                }

                StartLifetimeWatcher(options, interpreter);

                var appInfo = new SteamAppInfo(
                    new WindowInfo(
                        IntPtr.Zero,
                        "SteamMonitor",
                        options.GameProcessName,
                        options.GameProcessName + ".exe",
                        (uint)Math.Max(0, options.TargetPid),
                        0),
                    options.SteamAppId,
                    true);

                if (!SteamPeerManager.InitializeSteamApi(appInfo))
                {
                    interpreter.Error("Steam API initialization returned false.");
                    return SteamApiInitFailed;
                }

                interpreter.Log("Steam API initialized in SteamMonitor.", true);

                using (var manager = new SteamPeerManager(interpreter, options.GameProcessName))
                {
                    RunCommandLoop(Console.In, manager, interpreter);
                }

                SteamPeerManager.ShutdownSteamApi();
                interpreter.Exited("shutdown", Success);
                return Success;
            }
            catch (Exception ex)
            {
                interpreter.Error(ex.GetType().Name + ": " + ex.Message);
                SteamPeerManager.ShutdownSteamApi();
                return UnhandledError;
            }
        }

        private static void RunCommandLoop(
            TextReader input,
            SteamPeerManager manager,
            SteamMonitorInterpreter interpreter)
        {
            while (true)
            {
                string line = input.ReadLine();
                if (line == null)
                {
                    return;
                }

                SteamMonitorMessage command;
                try
                {
                    command = JsonConvert.DeserializeObject<SteamMonitorMessage>(line);
                }
                catch (Exception ex)
                {
                    interpreter.Error("Invalid command json: " + ex.Message);
                    continue;
                }

                if (command == null || string.IsNullOrWhiteSpace(command.Type))
                {
                    interpreter.Error("Invalid command: type is empty.");
                    continue;
                }

                try
                {
                    if (command.Type == SteamMonitorMessageType.UpdatePeerList)
                    {
                        manager.UpdatePeerList();
                        interpreter.Ack(command.RequestId);
                    }
                    else if (command.Type == SteamMonitorMessageType.UpdatePeerStats)
                    {
                        manager.UpdatePeerStats();
                        interpreter.Ack(command.RequestId);
                    }
                    else if (command.Type == SteamMonitorMessageType.Shutdown)
                    {
                        interpreter.Ack(command.RequestId);
                        return;
                    }
                    else
                    {
                        interpreter.Error("Unknown command type: " + command.Type);
                    }
                }
                catch (Exception ex)
                {
                    interpreter.Error(command.Type + " failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static void StartLifetimeWatcher(SteamMonitorOptions options, SteamMonitorInterpreter interpreter)
        {
            Task.Run(() =>
            {
                while (true)
                {
                    Thread.Sleep(1000);

                    if (options.ParentPid > 0 && !IsProcessAlive(options.ParentPid))
                    {
                        interpreter.Exited("parent process exited", Success);
                        SteamPeerManager.ShutdownSteamApi();
                        Environment.Exit(Success);
                    }

                    if (options.TargetPid > 0 && !IsProcessAlive(options.TargetPid))
                    {
                        interpreter.Exited("target process exited", Success);
                        SteamPeerManager.ShutdownSteamApi();
                        Environment.Exit(Success);
                    }
                }
            });
        }

        private static bool IsProcessAlive(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return !process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private sealed class SteamMonitorOptions
        {
            public string SteamAppId { get; private set; }
            public string GameProcessName { get; private set; }
            public int ParentPid { get; private set; }
            public int TargetPid { get; private set; }

            public static SteamMonitorOptions Parse(string[] args)
            {
                var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < args.Length; i++)
                {
                    string key = args[i];
                    if (!key.StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                    {
                        continue;
                    }

                    pairs[key.Substring(2)] = args[++i];
                }

                return new SteamMonitorOptions
                {
                    SteamAppId = GetValue(pairs, "steamAppId"),
                    GameProcessName = GetValue(pairs, "gameProcessName"),
                    ParentPid = GetIntValue(pairs, "parentPid"),
                    TargetPid = GetIntValue(pairs, "targetPid")
                };
            }

            private static string GetValue(Dictionary<string, string> pairs, string key)
            {
                string value;
                return pairs.TryGetValue(key, out value) ? value : null;
            }

            private static int GetIntValue(Dictionary<string, string> pairs, string key)
            {
                string value;
                int result;
                if (pairs.TryGetValue(key, out value) &&
                    int.TryParse(value, out result))
                {
                    return result;
                }

                return 0;
            }
        }
    }
}
