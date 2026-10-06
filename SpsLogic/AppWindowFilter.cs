using System;
using System.IO;
using System.Text;

namespace SpsLogic
{
    /// <summary>Conditions for a top-level window that can be a game window, shared by Sps and SpsLauncher.</summary>
    public static class AppWindowFilter
    {
        /// <summary>False when the window is the shell, hidden, untitled, owned by <paramref name="excludedProcessId"/>,
        /// or its exe path cannot be read.</summary>
        public static bool TryInspect(IntPtr hWnd, uint excludedProcessId, out WindowInfo info)
        {
            return TryInspect(hWnd, WinApi.GetShellWindow(), excludedProcessId, out info);
        }

        /// <summary>Calls <paramref name="callback"/> for each window that passes <see cref="TryInspect(IntPtr, uint, out WindowInfo)"/>.</summary>
        public static void Enumerate(uint excludedProcessId, Action<WindowInfo> callback)
        {
            IntPtr shellWindow = WinApi.GetShellWindow();
            WinApi.EnumWindows((hWnd, lParam) =>
            {
                WindowInfo info;
                if (TryInspect(hWnd, shellWindow, excludedProcessId, out info))
                {
                    callback(info);
                }

                return true;
            }, 0);
        }

        /// <summary>Finds a window of the process. <paramref name="preferredWindow"/> is used while it still belongs to the process.</summary>
        public static bool TryFindProcessWindow(uint processId, IntPtr preferredWindow, uint excludedProcessId, out WindowInfo info)
        {
            if (TryInspect(preferredWindow, excludedProcessId, out info) && info.ProcessId == processId)
            {
                return true;
            }

            WindowInfo found = null;
            Enumerate(excludedProcessId, window =>
            {
                if (found == null && window.ProcessId == processId)
                {
                    found = window;
                }
            });

            info = found;
            return info != null;
        }

        private static bool TryInspect(IntPtr hWnd, IntPtr shellWindow, uint excludedProcessId, out WindowInfo info)
        {
            info = null;
            if (hWnd == IntPtr.Zero || hWnd == shellWindow || !WinApi.IsWindowVisible(hWnd))
            {
                return false;
            }

            int length = WinApi.GetWindowTextLength(hWnd);
            if (length == 0)
            {
                return false;
            }

            var title = new StringBuilder(length + 1);
            if (WinApi.GetWindowText(hWnd, title, length + 1) <= 0)
            {
                return false;
            }

            uint processId;
            uint threadId = WinApi.GetWindowThreadProcessId(hWnd, out processId);
            if (processId == 0 || processId == excludedProcessId)
            {
                return false;
            }

            string processPath = WinApi.TryGetProcessPath((int)processId);
            if (string.IsNullOrEmpty(processPath))
            {
                return false;
            }

            info = new WindowInfo(hWnd, title.ToString(), Path.GetFileNameWithoutExtension(processPath), processPath, processId, threadId);
            return true;
        }
    }
}
