using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
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
            interpreter.Log($"{Process.GetCurrentProcess().ProcessName} administrator privileges: {IsRunningAsAdministrator()}", true);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                interpreter.Error("Unhandled exception: " + SafeExceptionText(e.ExceptionObject as Exception));
            };
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                interpreter.Error("Unobserved task exception: " + SafeExceptionText(e.Exception));
                e.SetObserved();
            };

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

                SafeShutdownSteamApi();
                interpreter.Exited("shutdown", Success);
                return Success;
            }
            catch (Exception ex)
            {
                interpreter.Error(SafeExceptionText(ex));
                SafeShutdownSteamApi();
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
                    interpreter.Error("Invalid command json: " + SafeExceptionText(ex));
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
                    interpreter.Error(command.Type + " failed: " + SafeExceptionText(ex));
                }
            }
        }

        private static void StartLifetimeWatcher(SteamMonitorOptions options, SteamMonitorInterpreter interpreter)
        {
            Task.Run(() =>
            {
                try
                {
                    while (true)
                    {
                        Thread.Sleep(1000);

                        if (options.ParentPid > 0 && !IsProcessAlive(options.ParentPid))
                        {
                            ExitProcess(interpreter, "parent process exited", Success);
                        }

                        if (options.TargetPid > 0 && !IsProcessAlive(options.TargetPid))
                        {
                            ExitProcess(interpreter, "target process exited", Success);
                        }
                    }
                }
                catch (Exception ex)
                {
                    interpreter.Error("Lifetime watcher failed: " + SafeExceptionText(ex));
                    ExitProcess(interpreter, "lifetime watcher failed", UnhandledError);
                }
            });
        }

        private static void ExitProcess(SteamMonitorInterpreter interpreter, string reason, int exitCode)
        {
            interpreter.Exited(reason, exitCode);
            SafeShutdownSteamApi();
            Environment.Exit(exitCode);
        }

        private static void SafeShutdownSteamApi()
        {
            try
            {
                SteamPeerManager.ShutdownSteamApi();
            }
            catch
            {
            }
        }

        private static string SafeExceptionText(Exception ex)
        {
            if (ex == null)
            {
                return "Unknown exception";
            }

            string typeName;
            try
            {
                typeName = ex.GetType().FullName;
            }
            catch
            {
                typeName = "UnknownException";
            }

            string message;
            try
            {
                message = ex.Message;
            }
            catch
            {
                message = "(message unavailable)";
            }

            string stackTrace;
            try
            {
                stackTrace = ex.StackTrace;
            }
            catch
            {
                stackTrace = null;
            }

            return string.IsNullOrWhiteSpace(stackTrace)
                ? typeName + ": " + message
                : typeName + ": " + message + Environment.NewLine + stackTrace;
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

        private static bool IsRunningAsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
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
