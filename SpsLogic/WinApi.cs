using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SpsLogic
{
    public static class WinApi
    {
        [Flags]
        public enum ProcessAccessFlags : uint
        {
            VmRead = 0x0010,
            QueryLimitedInformation = 0x1000
        }

        public enum ProcessInformationClass
        {
            ProcessBasicInformation = 0,
            ProcessWow64Information = 26
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessBasicInformation
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2_0;
            public IntPtr Reserved2_1;
            public IntPtr UniqueProcessId;
            public IntPtr Reserved3;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(
            ProcessAccessFlags dwDesiredAccess,
            bool bInheritHandle,
            int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            int dwSize,
            out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryFullProcessImageName(
            IntPtr hProcess,
            int dwFlags,
            StringBuilder lpExeName,
            ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWow64Process(
            IntPtr hProcess,
            out bool wow64Process);

        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            ProcessInformationClass processInformationClass,
            ref ProcessBasicInformation processInformation,
            int processInformationLength,
            out int returnLength);

        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            ProcessInformationClass processInformationClass,
            out IntPtr processInformation,
            int processInformationLength,
            out int returnLength);


        public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
                           IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll", SetLastError = false)]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hmod, int dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern ushort GetAsyncKeyState(int nVirtKey);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int x1; // x position of upper-left corner
            public int y1; // y position of upper-left corner
            public int x2; // x position of lower-right corner
            public int y2; // y position of lower-right corner
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int smIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        public static bool SetWindowZOrder(IntPtr hWnd, IntPtr hWndInsertAfter, uint uFlags = 0)
        {
            return SetWindowPos(hWnd, hWndInsertAfter, 0, 0, 0, 0, 0x03 | uFlags);
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool IsIconic(IntPtr hWnd);

        public delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);

        [DllImport("USER32.DLL")]
        public static extern bool EnumWindows(EnumWindowsProc enumFunc, int lParam);

        [DllImport("USER32.DLL")]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("USER32.DLL")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("USER32.DLL")]
        public static extern IntPtr GetShellWindow();
    }
}
