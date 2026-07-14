using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace SpsLogic
{
    /// <summary>
    /// Finds steam.exe paths without depending on PowerShell or shell commands.
    /// </summary>
    public interface IFindSteamExeService
    {
        /// <summary>
        /// Finds steam.exe candidates from running processes and known Steam registry values.
        /// </summary>
        /// <returns>A result containing normalized candidate paths and diagnostic messages.</returns>
        FindSteamExeResult Find();
    }

    /// <summary>
    /// Steam executable path finder backed by Win32 process queries and registry fallbacks.
    /// </summary>
    public class FindSteamExeService : IFindSteamExeService
    {
        private const string SteamProcessName = "steam";
        private const string SteamExeFileName = "steam.exe";

        private static readonly RegistryLocation[] RegistryLocations =
        {
            new RegistryLocation(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamExe"),
            new RegistryLocation(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath"),
            new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath"),
            new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Valve\Steam", "InstallPath")
        };

        /// <inheritdoc />
        public FindSteamExeResult Find()
        {
            var candidates = new List<SteamExeCandidate>();
            var messages = new List<string>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddRunningProcessCandidates(candidates, messages, seenPaths);
            AddRegistryCandidates(candidates, messages, seenPaths);

            return new FindSteamExeResult(candidates.ToArray(), messages.ToArray());
        }

        /// <summary>
        /// Adds candidates from currently running steam.exe processes.
        /// </summary>
        /// <param name="candidates">Candidate list that receives valid steam.exe paths.</param>
        /// <param name="messages">Diagnostic list that receives lookup status and failures.</param>
        /// <param name="seenPaths">Path set used to avoid duplicate candidates.</param>
        private static void AddRunningProcessCandidates(
            ICollection<SteamExeCandidate> candidates,
            ICollection<string> messages,
            ISet<string> seenPaths)
        {
            Process[] processes;
            try
            {
                // we assumes that the desired name is steam.exe
                processes = Process.GetProcessesByName(SteamProcessName);
            }
            catch (Exception ex)
            {
                messages.Add("Process enumeration failed: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            messages.Add("Running steam.exe process count: " + processes.Length);
            foreach (Process process in processes)
            {
                using (process)
                {
                    string error;
                    string path = TryGetProcessPath(process.Id, out error);
                    if (!string.IsNullOrEmpty(error))
                    {
                        messages.Add("PID " + process.Id + ": " + error);
                    }

                    AddCandidate(candidates, seenPaths, path, "running process", process.Id, messages);
                }
            }
        }

        /// <summary>
        /// Adds candidates from Steam registry values.
        /// </summary>
        /// <param name="candidates">Candidate list that receives valid steam.exe paths.</param>
        /// <param name="messages">Diagnostic list that receives lookup status and failures.</param>
        /// <param name="seenPaths">Path set used to avoid duplicate candidates.</param>
        private static void AddRegistryCandidates(
            ICollection<SteamExeCandidate> candidates,
            ICollection<string> messages,
            ISet<string> seenPaths)
        {
            foreach (RegistryLocation location in RegistryLocations)
            {
                string value = ReadRegistryString(location, messages);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                string path = NormalizeRegistrySteamPath(value);
                AddCandidate(candidates, seenPaths, path, "registry: " + location, null, messages);
            }
        }

        /// <summary>
        /// Reads a string value from the requested registry location.
        /// </summary>
        /// <param name="location">Registry key and value name to read.</param>
        /// <param name="messages">Diagnostic list that receives registry access failures.</param>
        /// <returns>The raw registry string, or null when it is missing or unreadable.</returns>
        private static string ReadRegistryString(RegistryLocation location, ICollection<string> messages)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(location.Hive, location.View))
                using (RegistryKey key = baseKey.OpenSubKey(location.SubKey))
                {
                    object value = key == null ? null : key.GetValue(location.ValueName);
                    return value as string;
                }
            }
            catch (Exception ex)
            {
                messages.Add("Registry read failed: " + location + ": " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Converts a Steam registry value into an expected steam.exe path.
        /// </summary>
        /// <param name="value">Registry value that may be steam.exe or the Steam installation directory.</param>
        /// <returns>A candidate steam.exe path, or null when the value cannot be normalized.</returns>
        private static string NormalizeRegistrySteamPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string path = value.Trim().Trim('"');
            if (string.Equals(Path.GetFileName(path), SteamExeFileName, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return Path.Combine(path, SteamExeFileName);
        }

        /// <summary>
        /// Reads a process image path using QueryFullProcessImageName.
        /// </summary>
        /// <param name="processId">Target process id. It must identify an existing local process.</param>
        /// <param name="error">Diagnostic error text when the path cannot be read; otherwise null.</param>
        /// <returns>The process image path, or null when it cannot be read.</returns>
        private static string TryGetProcessPath(int processId, out string error)
        {
            error = null;
            IntPtr processHandle = WinApi.OpenProcess(
                WinApi.ProcessAccessFlags.QueryLimitedInformation,
                false,
                processId);

            if (processHandle == IntPtr.Zero)
            {
                error = SteamAppFinder.WinApiErrors.CreateLastWin32ErrorMessage("OpenProcess");
                return null;
            }

            try
            {
                int capacity = 32768;
                var builder = new StringBuilder(capacity);
                if (!WinApi.QueryFullProcessImageName(processHandle, 0, builder, ref capacity))
                {
                    error = SteamAppFinder.WinApiErrors.CreateLastWin32ErrorMessage("QueryFullProcessImageName");
                    return null;
                }

                return builder.ToString();
            }
            finally
            {
                WinApi.CloseHandle(processHandle);
            }
        }

        /// <summary>
        /// Adds a candidate only when the path is a real steam.exe file and has not been seen.
        /// </summary>
        /// <param name="candidates">Candidate list that receives the normalized candidate.</param>
        /// <param name="seenPaths">Path set used to avoid duplicate candidates.</param>
        /// <param name="path">Candidate path to validate and normalize.</param>
        /// <param name="source">Human-readable source of the candidate.</param>
        /// <param name="processId">Process id when the candidate came from a running process; otherwise null.</param>
        /// <param name="messages">Diagnostic list that receives skipped candidate reasons.</param>
        private static void AddCandidate(
            ICollection<SteamExeCandidate> candidates,
            ISet<string> seenPaths,
            string path,
            string source,
            int? processId,
            ICollection<string> messages)
        {
            string normalizedPath = NormalizeCandidatePath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return;
            }

            if (!string.Equals(Path.GetFileName(normalizedPath), SteamExeFileName, StringComparison.OrdinalIgnoreCase))
            {
                messages.Add("Skipped non-steam.exe candidate from " + source + ": " + normalizedPath);
                return;
            }

            if (!File.Exists(normalizedPath))
            {
                messages.Add("Skipped missing candidate from " + source + ": " + normalizedPath);
                return;
            }

            if (!seenPaths.Add(normalizedPath))
            {
                return;
            }

            candidates.Add(new SteamExeCandidate(normalizedPath, source, processId));
        }

        /// <summary>
        /// Normalizes a raw candidate path into a full local path.
        /// </summary>
        /// <param name="path">Raw path text from a process or registry value.</param>
        /// <returns>A full path, or null when the path is empty or invalid.</returns>
        private static string NormalizeCandidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path.Trim().Trim('"'));
            }
            catch
            {
                return null;
            }
        }

        private sealed class RegistryLocation
        {
            public RegistryLocation(RegistryHive hive, RegistryView view, string subKey, string valueName)
            {
                Hive = hive;
                View = view;
                SubKey = subKey;
                ValueName = valueName;
            }

            public RegistryHive Hive { get; private set; }

            public RegistryView View { get; private set; }

            public string SubKey { get; private set; }

            public string ValueName { get; private set; }

            public override string ToString()
            {
                return Hive + "(" + View + ")\\" + SubKey + "\\" + ValueName;
            }
        }
    }

    /// <summary>
    /// Result of a steam.exe discovery run.
    /// </summary>
    public sealed class FindSteamExeResult
    {
        public FindSteamExeResult(SteamExeCandidate[] candidates, string[] messages)
        {
            Candidates = candidates ?? new SteamExeCandidate[0];
            Messages = messages ?? new string[0];
        }

        /// <summary>
        /// Gets valid steam.exe candidates found by the service.
        /// </summary>
        public SteamExeCandidate[] Candidates { get; private set; }

        /// <summary>
        /// Gets diagnostic messages produced during discovery.
        /// </summary>
        public string[] Messages { get; private set; }

        /// <summary>
        /// Gets the first candidate path ordered by the service preference.
        /// </summary>
        public string BestPath
        {
            get
            {
                SteamExeCandidate candidate = Candidates.FirstOrDefault();
                return candidate == null ? null : candidate.Path;
            }
        }
    }

    /// <summary>
    /// A validated steam.exe candidate path with source diagnostics.
    /// </summary>
    public sealed class SteamExeCandidate
    {
        public SteamExeCandidate(string path, string source, int? processId)
        {
            Path = path;
            Source = source;
            ProcessId = processId;
        }

        /// <summary>
        /// Gets the normalized full path to steam.exe.
        /// </summary>
        public string Path { get; private set; }

        /// <summary>
        /// Gets the discovery source such as running process or registry.
        /// </summary>
        public string Source { get; private set; }

        /// <summary>
        /// Gets the process id when the candidate came from a running process.
        /// </summary>
        public int? ProcessId { get; private set; }
    }
}
