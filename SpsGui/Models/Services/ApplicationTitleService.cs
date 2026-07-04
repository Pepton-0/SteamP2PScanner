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
        public string CreateApplicationTitle()
        {
            return "SteamP2PScanner " + Assembly.GetExecutingAssembly().GetName().Version;
        }
    }
}
