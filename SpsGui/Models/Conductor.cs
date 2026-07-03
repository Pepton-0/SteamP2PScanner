using SpsLogic;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SpsGui.Models
{
    public interface IConductor
    {
        /// <summary>
        /// Gets the packet scanner shared with the current Steam peer manager.
        /// </summary>
        IPacketScan PacketScan { get; }

        /// <summary>
        /// Creates a Steam peer manager for the selected application.
        /// </summary>
        /// <param name="info">Selected Steam application information. Must not be null.</param>
        /// <returns>A manager initialized for the selected application and shared packet scanner.</returns>
        SteamPeerManager CreateSteamPeerManager(SteamAppInfo info);
    }

    public class Conductor : IConductor
    {
        private readonly IPacketScan packetScan;

        public Conductor(IPacketScan packetScan)
        {
            this.packetScan = packetScan ?? throw new ArgumentNullException(nameof(packetScan));

            string culture = CultureInfo.CurrentCulture.Name;
            try
            {
                string local = CultureInfo.CreateSpecificCulture(culture).Name;
                var localDictionary = new ResourceDictionary()
                {
                    Source = new Uri(@"Resources/StringResource." + local + @".xaml", UriKind.Relative)
                };
                App.Current.Resources.MergedDictionaries.Add(localDictionary);
            }
            catch (CultureNotFoundException)
            { // for CultureInfo.CreateSpecificCulture
                Logger.Log(culture + " has no info so failed to load the localization");
            }
            catch
            {
                Logger.Log($"Failed to find local dictionary for {culture} so use default en-us.");
            }
        }

        /// <summary>
        /// Gets the packet scanner shared with the current profiling session.
        /// </summary>
        public IPacketScan PacketScan
        {
            get
            {
                return packetScan;
            }
        }

        /// <summary>
        /// Creates a Steam peer manager and prepares the packet scanner for profiling.
        /// </summary>
        /// <param name="info">Selected Steam application information. Must not be null.</param>
        /// <returns>A Steam peer manager using the conductor-owned packet scanner.</returns>
        public SteamPeerManager CreateSteamPeerManager(SteamAppInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            if (!SteamPeerManager.InitializeSteamApi(info))
            {
                return null;
            }

            return new SteamPeerManager(packetScan, info.Info.ProcessName);
        }
    }
}
