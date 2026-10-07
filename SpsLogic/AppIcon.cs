using System.Drawing;
using System.IO;

namespace SpsLogic
{
    /// <summary>Application icon shared by SpsGui and SpsLauncher, embedded in this assembly.</summary>
    public static class AppIcon
    {
        private const string ResourceName = "SteamP2PScanner.ico";

        /// <summary>Loads the icon image closest to <paramref name="size"/>, e.g. the small icon size for the tray.</summary>
        public static Icon Load(Size size)
        {
            using (Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName))
            {
                return new Icon(stream, size);
            }
        }
    }
}
