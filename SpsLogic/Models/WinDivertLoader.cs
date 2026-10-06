using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SpsLogic
{
    /// <summary>Decides which WinDivert.dll is used. WinDivert loads its driver from the directory of this DLL,
    /// and the WinDivert imports search System32 only, so they bind to the module loaded here.</summary>
    internal static class WinDivertLoader
    {
        private const string DllName = "WinDivert.dll";
        private static readonly object syncRoot = new object();
        private static bool loaded;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        /// <summary>Loads the install directory copy so that the Sps directory can be deleted as a whole.
        /// Falls back to the copy next to the exe when SpsLauncher is not installed.</summary>
        public static void EnsureLoaded()
        {
            lock (syncRoot)
            {
                if (!loaded)
                {
                    loaded = TryLoad(Path.Combine(StartupTask.InstallDirectory, DllName)) ||
                        TryLoad(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DllName));
                }
            }
        }

        private static bool TryLoad(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (LoadLibrary(path) == IntPtr.Zero)
            {
                Logger.Log("Failed to load " + path + " (error " + Marshal.GetLastWin32Error() + ").", true);
                return false;
            }

            Logger.Log("Loaded WinDivert from " + path, true);
            return true;
        }
    }
}
