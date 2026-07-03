using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Steamworks;

namespace SpsLogic
{
    /// <summary>
    /// Represent the base of steam peer to peer
    /// </summary>
    public abstract class SteamPeerBase : ObservableObject, IDisposable
    {
        /// <summary>
        /// Steam ID of the peer.
        /// </summary>
        public CSteamID SteamID { get; protected set; }

        // The following field is probably useless now that Steam appears to have disabled the IsPlayingSharedGame request

        /// <summary>
        /// Main Steam ID of the peer, if playing on an alternate account. 
        /// </summary>
        public CSteamID MainSteamID { get; protected set; }

        /// <summary>
        /// Ping monitor
        /// </summary>
        protected readonly IPacketScan PacketScan;

        /// <summary>
        /// Steam persona name of the peer.
        /// TODO remove to use Stats.Name
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// True if the peer is connected via the deprected api, ISteamNetworking.
        /// </summary>
        public abstract bool IsOldAPI { get; }

        /// <summary>
        /// Name representing the type of connection this peer is using (e.g. SteamNetworking, SteamNetworkingSockets)
        /// </summary>
        public abstract string ConnectionTypeName { get; }

        /// <summary>
        /// Relay server
        /// </summary>
        public abstract bool UsingRelay { get; }

        protected SteamPeerBase(CSteamID steamID, IPacketScan packetScan)
        {
            SteamID = steamID;
            this.PacketScan = packetScan;
        }

        /// <summary>
        /// Update peer info that may not be known at instance creation time.
        /// Should return true if the peer is still connected and false otherwise.
        /// </summary>
        public abstract bool UpdatePeerInfo();

        public virtual void Dispose() { }
    }
}
