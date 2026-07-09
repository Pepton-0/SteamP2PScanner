using System.Reflection;

namespace SpsGui.Models.Services
{
    public interface IApplicationTitleService
    {
        string CreateApplicationTitle();
    }

    /// <summary>
    /// Creates user-visible application title strings.
    /// </summary>
    public class ApplicationTitleService : IApplicationTitleService
    {
        private const string Version = "1.0";

        public string CreateApplicationTitle()
        {
            return "SteamP2PScanner " + Version;
        }
    }
}
