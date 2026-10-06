using System;
using System.Globalization;

namespace SpsLogic
{
    /// <summary>Arguments with which SpsLauncher starts SpsGui to monitor a game window.</summary>
    public readonly struct SpsGuiStartupOptions
    {
        private const string SteamAppIdArgument = "--steam-app-id";
        private const string ProcessIdArgument = "--pid";
        private const string WindowHandleArgument = "--hwnd";

        public readonly string SteamAppId;
        public readonly uint ProcessId;
        public readonly IntPtr WindowHandle;

        public SpsGuiStartupOptions(string steamAppId, uint processId, IntPtr windowHandle)
        {
            SteamAppId = steamAppId;
            ProcessId = processId;
            WindowHandle = windowHandle;
        }

        /// <summary>False for a normal start without a monitor target.</summary>
        public bool HasTarget
        {
            get { return !string.IsNullOrWhiteSpace(SteamAppId) && ProcessId != 0; }
        }

        public string ToArguments()
        {
            return SteamAppIdArgument + " " + CommandLine.Quote(SteamAppId) + " " +
                ProcessIdArgument + " " + ProcessId.ToString(CultureInfo.InvariantCulture) + " " +
                WindowHandleArgument + " " + WindowHandle.ToInt64().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Returns options without a target when the arguments are absent or invalid.</summary>
        public static SpsGuiStartupOptions Parse(string[] args)
        {
            string steamAppId = null;
            uint processId = 0;
            long windowHandle = 0;
            for (int i = 0; i < args.Length - 1; i++)
            {
                string value = args[i + 1];
                switch (args[i])
                {
                    case SteamAppIdArgument:
                        steamAppId = value;
                        break;
                    case ProcessIdArgument:
                        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out processId);
                        break;
                    case WindowHandleArgument:
                        long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out windowHandle);
                        break;
                }
            }

            return new SpsGuiStartupOptions(steamAppId, processId, new IntPtr(windowHandle));
        }

        public override string ToString()
        {
            return "steamAppId=" + SteamAppId + ", pid=" + ProcessId + ", hwnd=0x" + WindowHandle.ToInt64().ToString("X");
        }
    }
}
