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
        private readonly IVersionCheckService VerService;

        public ApplicationTitleService(IVersionCheckService verServ)
        {
            VerService = verServ;
        }

        public string CreateApplicationTitle()
        {
            return "SteamP2PScanner " + VerService.GetVersion();
        }
    }
}
