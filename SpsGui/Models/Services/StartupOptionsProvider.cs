using SpsLogic;

namespace SpsGui.Models.Services
{
    /// <summary>Holds the command line options of this run. A struct cannot be registered as a DI singleton directly.</summary>
    public interface IStartupOptionsProvider
    {
        SpsGuiStartupOptions Options { get; }
    }

    public sealed class StartupOptionsProvider : IStartupOptionsProvider
    {
        public StartupOptionsProvider(SpsGuiStartupOptions options)
        {
            Options = options;
        }

        public SpsGuiStartupOptions Options { get; }
    }
}
