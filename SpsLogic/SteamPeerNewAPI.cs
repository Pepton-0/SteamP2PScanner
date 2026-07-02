using Steamworks;

namespace SpsLogic
{
    /// <summary>
    /// Represents a Steam P2P peer connected using the ISteamNetworkingMessages API.
    /// </summary>
    class SteamPeerNewAPI : SteamPeerBase
    {
        /// <summary>
        /// Object providing information on the steam P2P connection.
        /// </summary>
        private SteamNetConnectionInfo_t mConnInfo;

        /// <summary>
        /// Object providing realtime information on the steam P2P connection (namely ping)
        /// </summary>
        private SteamNetConnectionRealTimeStatus_t mRealTimeStatus;

        public override bool IsOldAPI { get { return false; } }

        public override string ConnectionTypeName => "SteamNetworkingSockets";

        // What relay are we using to communicate with the remote host?
        // (0 if not applicable.)
        public override bool UsingRelay { get { return (uint)mConnInfo.m_idPOPRelay != 0; } }

        // TODO remove to use Stats.Name or any managed names in ping monitor 
        private readonly string _name;
        public override string Name => _name;

        private bool ShouldStartAccumulatingValue = false;

        public SteamPeerNewAPI(CSteamID steamId, IPacketScan packetScan) : base(steamId, packetScan)
        {
            mConnInfo = new SteamNetConnectionInfo_t();
            mRealTimeStatus = new SteamNetConnectionRealTimeStatus_t();
            _name = SteamFriends.GetFriendPersonaName(SteamID);
        }

        public override bool UpdatePeerInfo()
        {
            SteamNetworkingIdentity networkingIdentity = new SteamNetworkingIdentity();
            networkingIdentity.SetSteamID(SteamID);

            var connState = SteamNetworkingMessages.GetSessionConnectionInfo(ref networkingIdentity, out mConnInfo, out mRealTimeStatus);

            // Pingが取得できるようになるまで記録しない
            double ping = mRealTimeStatus.m_nPing;
            this.ShouldStartAccumulatingValue |= ping > 0;
            if (this.ShouldStartAccumulatingValue)
            {
                // this._stats.PushPing(ping);
                // TODO New API prepares the ping function? Check it out.
            }

            return IsConnStateOK(connState);
        }

        public static bool IsConnStateOK(ESteamNetworkingConnectionState connState)
        {
            return connState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting ||
                     connState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected;
        }
    }
}
