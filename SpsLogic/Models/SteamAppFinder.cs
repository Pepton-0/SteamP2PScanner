using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace SpsLogic
{
    public interface ISteamAppFinder
    {
        void EnumWindows(Action<WindowInfo> callback);

        SteamAppInfo[] GetSteamProcesses();
    }

    public class WindowInfo
    {
        public readonly IntPtr Handle;
        public readonly string Title;
        public readonly string ProcessName;
        public readonly string ProcessPath;
        public readonly uint ProcessId;
        public readonly uint ThreadId;

        public WindowInfo(IntPtr handle, string title, string processName, string processPath, uint processId, uint threadId)
        {
            Handle = handle;
            Title = title;
            ProcessName = processName;
            ProcessPath = processPath;
            ProcessId = processId;
            ThreadId = threadId;
        }
    }

    public class SteamAppInfo
    {
        public readonly WindowInfo Info;
        public string SteamAppId;
        public readonly bool IsVisible;

        public SteamAppInfo(WindowInfo info, string steamAppId, bool isVisible)
        {
            Info = info;
            SteamAppId = steamAppId;
            this.IsVisible = isVisible;
        }
    }

    public class SteamAppFinder : ISteamAppFinder
    {
        public class ApproximatePath : IEquatable<ApproximatePath>
        {
            public readonly string FullPath;
            private readonly string DirPath;
            private readonly string PDirPath;

            public ApproximatePath(string fullPath)
            {
                FullPath = fullPath;
                DirPath = NormalizeDirectoryPath(Path.GetDirectoryName(fullPath));

                DirectoryInfo parent = string.IsNullOrEmpty(DirPath)
                    ? null
                    : Directory.GetParent(DirPath);

                PDirPath = parent == null
                    ? string.Empty
                    : NormalizeDirectoryPath(parent.FullName);
            }

            public bool Equals(ApproximatePath other)
            {
                if (other == null)
                {
                    return false;
                }

                return PathEquals(DirPath, other.DirPath) ||
                       PathEquals(PDirPath, other.DirPath) ||
                       PathEquals(DirPath, other.PDirPath);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as ApproximatePath);
            }

            public override int GetHashCode()
            {
                return 0;
            }

            private static bool PathEquals(string a, string b)
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }

            private static string NormalizeDirectoryPath(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return string.Empty;
                }

                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        public static class ProcessEnvironmentReader
        {
            private const int PebProcessParametersOffset32 = 0x10;
            private const int PebProcessParametersOffset64 = 0x20;
            private const int ProcessParametersEnvironmentOffset32 = 0x48;
            private const int ProcessParametersEnvironmentOffset64 = 0x80;
            private const int MaxEnvironmentBytes = 1024 * 1024;

            public static bool TryReadEnvironmentVariables(
                int processId,
                out Dictionary<string, string> variables,
                out string error)
            {
                variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                error = null;

                IntPtr processHandle = WinApi.OpenProcess(
                    WinApi.ProcessAccessFlags.QueryLimitedInformation | WinApi.ProcessAccessFlags.VmRead,
                    false,
                    processId);

                if (processHandle == IntPtr.Zero)
                {
                    error = WinApiErrors.CreateLastWin32ErrorMessage("OpenProcess");
                    return false;
                }

                try
                {
                    int pointerSize;
                    IntPtr pebAddress = GetPebAddress(processHandle, out pointerSize);
                    int processParametersOffset = pointerSize == 8
                        ? PebProcessParametersOffset64
                        : PebProcessParametersOffset32;
                    int environmentOffset = pointerSize == 8
                        ? ProcessParametersEnvironmentOffset64
                        : ProcessParametersEnvironmentOffset32;

                    IntPtr processParametersAddress = ReadPointer(
                        processHandle,
                        IntPtr.Add(pebAddress, processParametersOffset),
                        pointerSize);

                    if (processParametersAddress == IntPtr.Zero)
                    {
                        return true;
                    }

                    IntPtr environmentAddress = ReadPointer(
                        processHandle,
                        IntPtr.Add(processParametersAddress, environmentOffset),
                        pointerSize);

                    if (environmentAddress == IntPtr.Zero)
                    {
                        return true;
                    }

                    variables = ParseEnvironmentBlock(ReadEnvironmentBlock(processHandle, environmentAddress));
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"{ex.GetType().Name}: {ex.Message}";
                    return false;
                }
                finally
                {
                    WinApi.CloseHandle(processHandle);
                }
            }

            private static IntPtr GetPebAddress(IntPtr processHandle, out int pointerSize)
            {
                bool isWow64Process = IsWow64Process(processHandle);

                if (isWow64Process)
                {
                    pointerSize = 4;
                    return GetWow64PebAddress(processHandle);
                }

                if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem)
                {
                    throw new NotSupportedException("A 32-bit process cannot read a 64-bit process environment block.");
                }

                pointerSize = IntPtr.Size;
                return GetNativePebAddress(processHandle);
            }

            private static bool IsWow64Process(IntPtr processHandle)
            {
                if (!Environment.Is64BitOperatingSystem)
                {
                    return false;
                }

                bool isWow64Process;
                if (!WinApi.IsWow64Process(processHandle, out isWow64Process))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "IsWow64Process failed.");
                }

                return isWow64Process;
            }

            private static IntPtr GetNativePebAddress(IntPtr processHandle)
            {
                var processInformation = new WinApi.ProcessBasicInformation();
                int returnLength;
                int status = WinApi.NtQueryInformationProcess(
                    processHandle,
                    WinApi.ProcessInformationClass.ProcessBasicInformation,
                    ref processInformation,
                    Marshal.SizeOf(typeof(WinApi.ProcessBasicInformation)),
                    out returnLength);

                if (status != 0)
                {
                    throw new InvalidOperationException(
                        "NtQueryInformationProcess(ProcessBasicInformation) failed with status " +
                        FormatNtStatus(status) + ".");
                }

                return processInformation.PebBaseAddress;
            }

            private static IntPtr GetWow64PebAddress(IntPtr processHandle)
            {
                IntPtr pebAddress;
                int returnLength;
                int status = WinApi.NtQueryInformationProcess(
                    processHandle,
                    WinApi.ProcessInformationClass.ProcessWow64Information,
                    out pebAddress,
                    IntPtr.Size,
                    out returnLength);

                if (status != 0)
                {
                    throw new InvalidOperationException(
                        "NtQueryInformationProcess(ProcessWow64Information) failed with status " +
                        FormatNtStatus(status) + ".");
                }

                return pebAddress;
            }

            private static IntPtr ReadPointer(IntPtr processHandle, IntPtr address, int pointerSize)
            {
                byte[] pointerBytes = ReadBytes(processHandle, address, pointerSize);

                if (pointerSize == 8)
                {
                    return new IntPtr(BitConverter.ToInt64(pointerBytes, 0));
                }

                return new IntPtr(BitConverter.ToInt32(pointerBytes, 0));
            }

            private static byte[] ReadBytes(IntPtr processHandle, IntPtr address, int byteCount)
            {
                byte[] buffer = new byte[byteCount];
                IntPtr bytesRead;

                if (!WinApi.ReadProcessMemory(processHandle, address, buffer, byteCount, out bytesRead))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "ReadProcessMemory failed.");
                }

                if (bytesRead.ToInt64() != byteCount)
                {
                    throw new InvalidOperationException(
                        $"ReadProcessMemory read {bytesRead.ToInt64()} bytes, expected {byteCount} bytes.");
                }

                return buffer;
            }

            private static string ReadEnvironmentBlock(IntPtr processHandle, IntPtr environmentAddress)
            {
                var bytes = new List<byte>();
                int offset = 0;
                ushort previousChar = 0;
                bool hasReadAnyChar = false;

                while (bytes.Count < MaxEnvironmentBytes)
                {
                    byte[] charBytes = ReadBytes(processHandle, IntPtr.Add(environmentAddress, offset), 2);
                    ushort currentChar = BitConverter.ToUInt16(charBytes, 0);

                    if (!hasReadAnyChar && currentChar == 0)
                    {
                        return string.Empty;
                    }

                    if (hasReadAnyChar && previousChar == 0 && currentChar == 0)
                    {
                        return Encoding.Unicode.GetString(bytes.ToArray(), 0, bytes.Count - 2);
                    }

                    bytes.Add(charBytes[0]);
                    bytes.Add(charBytes[1]);
                    previousChar = currentChar;
                    hasReadAnyChar = true;
                    offset += 2;
                }

                throw new InvalidOperationException("Environment block terminator was not found.");
            }

            private static Dictionary<string, string> ParseEnvironmentBlock(string environmentBlock)
            {
                var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string[] entries = environmentBlock.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string entry in entries)
                {
                    int separatorIndex = entry.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    variables[entry.Substring(0, separatorIndex)] = entry.Substring(separatorIndex + 1);
                }

                return variables;
            }

            private static string FormatNtStatus(int status)
            {
                return "0x" + unchecked((uint)status).ToString("X8", CultureInfo.InvariantCulture);
            }
        }

        public static class WinApiErrors
        {
            public static string CreateLastWin32ErrorMessage(string apiName)
            {
                var exception = new Win32Exception(Marshal.GetLastWin32Error());
                return $"{apiName} failed: {exception.Message}";
            }
        }

        private const string SteamAppIdVariableName = "SteamAppId";
        private const string EasyAntiCheatPublisherName = "EasyAntiCheat Oy";

        private Dictionary<ApproximatePath, string> Path2SteamAppId =
            new Dictionary<ApproximatePath, string>();

        /// <summary>
        /// Whether if the process path has any proof which should not be visible
        /// as a candidate because its not steam game itself.
        /// </summary>
        private Dictionary<string, bool> Path2Ignore = 
            new Dictionary<string, bool>();

        public SteamAppFinder()
        {

        }

        /// <summary>
        /// List up all the visible windows
        /// </summary>
        /// <param name="func"></param>
        public void EnumWindows(Action<WindowInfo> func)
        {
            IntPtr shellWindow = WinApi.GetShellWindow();

            WinApi.EnumWindows((hWnd, lParam) =>
            {
                if (hWnd == shellWindow) return true;
                if (!WinApi.IsWindowVisible(hWnd)) return true;

                int length = WinApi.GetWindowTextLength(hWnd);
                if (length == 0) return true;

                StringBuilder builder = new StringBuilder(length + 1);
                if (WinApi.GetWindowText(hWnd, builder, length + 1) <= 0)
                {
                    return true;
                }

                var threadId = WinApi.GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == 0)
                {
                    return true;
                }

                var pHandle = WinApi.OpenProcess(WinApi.ProcessAccessFlags.QueryLimitedInformation, false, (int)processId);
                string processPath = null;
                string processName = null;
                try
                {
                    if (pHandle != IntPtr.Zero)
                    {
                        int cap = 32768;
                        StringBuilder imgBuilder = new StringBuilder(cap);
                        if (!WinApi.QueryFullProcessImageName(pHandle, 0, imgBuilder, ref cap))
                        {
                            return true;
                        }

                        processPath = imgBuilder.ToString();
                        if (string.IsNullOrEmpty(processPath))
                        {
                            return true;
                        }

                        processName = Path.GetFileNameWithoutExtension(processPath);
                    }
                }
                finally
                {
                    if (pHandle != IntPtr.Zero)
                    {
                        WinApi.CloseHandle(pHandle);
                    }
                }

                if (processName != null && processPath != null)
                {
                    WindowInfo wInfo = new WindowInfo(
                        hWnd,
                        builder.ToString(),
                        processName,
                        processPath,
                        processId,
                        threadId
                        );

                    func.Invoke(wInfo);
                }

                return true;
            }, 0);
        }

        /// <summary>
        /// List up all the processes and its steam app ids.
        /// Call this frequently ( ex. per 1 sec) to detect a temporal launcher which has steam app id envrionment variable.
        /// </summary>
        /// <returns></returns>
        public SteamAppInfo[] GetSteamProcesses()
        {
            List<SteamAppInfo> steamApps = new List<SteamAppInfo>();

            EnumWindows((w) =>
            {
                string appId = null;
                bool invisible = true;
                var registered = GameConfig.Instance.RegisteredGames[w.ProcessPath];
                if (registered != null)
                {
                    appId = registered;
                    invisible = false;
                }
                else
                {
                    var approxPath = new ApproximatePath(w.ProcessPath);
                    if (Path2SteamAppId.TryGetValue(approxPath, out var steamAppId0))
                    { // if the path exe has detected steam app id
                        if (steamAppId0.Length > 0)
                        {
                            appId = steamAppId0;
                        }
                    }
                    else
                    { // if the path exe still doesn't have steam app id envrionment var yet
                        //TODO なんかめちゃくちゃ重い
                        ProcessEnvironmentReader
                        .TryReadEnvironmentVariables(
                            (int)w.ProcessId,
                            out var variables,
                            out var err);
                        /*var variables = new Dictionary<string, string>();
                        variables.Add(SteamAppIdVariableName, "114514");*/

                        if (variables.TryGetValue(SteamAppIdVariableName, out var steamAppId1))
                        {
                            Path2SteamAppId[approxPath] = steamAppId1;
                            if (steamAppId1.Length > 0)
                            {
                                appId = steamAppId1;
                            }
                        }
                    }
                }

                if(appId != null)
                {
                    if(Path2Ignore.TryGetValue(w.ProcessPath, out var ignore))
                    {
                        invisible = ignore;
                    }
                    else
                    {
                        invisible = IsEasyAntiCheatSigned(w.ProcessPath);
                        Path2Ignore[w.ProcessPath] = invisible;
                    }

                    var info = new SteamAppInfo(w, appId, !invisible);
                    steamApps.Add(info);
                }
            });

            return steamApps.ToArray();
        }

        private static bool IsEasyAntiCheatSigned(string processPath)
        {
            if (string.IsNullOrEmpty(processPath))
            {
                return false;
            }

            try
            {
                var cert = X509Certificate.CreateFromSignedFile(processPath);
                var cert2 = new X509Certificate2(cert);
                string subject = cert2.Subject ?? string.Empty;
                return subject.IndexOf(EasyAntiCheatPublisherName, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
