using System;
using System.Linq;
using Steamworks;

namespace SpsLogic
{
    /// <summary>
    /// Represents a Steam P2P peer connected using the ISteamNetworking API.
    /// </summary>
    class SteamPeerOldAPI : SteamPeerBase
    {
        /// <summary>
        /// Combination of IP/port. Used query ping for old API connections using the ETW ping monitor.
        /// </summary>
        private ulong mNetIdentity;

        /// <summary>
        /// Object providing information on the steam P2P connection.
        /// </summary>
        private P2PSessionState_t mSessionState;

        public override bool IsOldAPI { get { return true; } }

        public override string ConnectionTypeName => "SteamNetworking";

        private readonly string _name;
        public override string Name => _name;

        public override bool UsingRelay { get { return mSessionState.m_bUsingRelay != 0; } }

        public SteamPeerOldAPI(CSteamID steamId, ISteamPeerInterpreter interpreter) : base(steamId, interpreter)
        {
            mSessionState = new P2PSessionState_t();
            _name = SteamFriends.GetFriendPersonaName(SteamID);
        }

        public override void Dispose()
        {
            Interpreter.Unregister(mNetIdentity);
        }

        public override bool UpdatePeerInfo()
        {
            if (!SteamNetworking.GetP2PSessionState(SteamID, out P2PSessionState_t session) || !IsSessionStateOK(session))
                return false;

            bool endpointChanged = mSessionState.m_nRemoteIP != session.m_nRemoteIP || mSessionState.m_nRemotePort != session.m_nRemotePort;
            mSessionState = session;

            if (endpointChanged)
            {
                Interpreter.Unregister(mNetIdentity);

                byte[] ipBytes = BitConverter.GetBytes(mSessionState.m_nRemoteIP).Reverse().ToArray();
                mNetIdentity = ((ulong)mSessionState.m_nRemotePort << 32) | BitConverter.ToUInt32(ipBytes, 0);

                Interpreter.Register(mNetIdentity, Name, SteamID.m_SteamID);
            }

            return true;
        }

        public static bool IsSessionStateOK(P2PSessionState_t session)
        {
            return session.m_eP2PSessionError == 0 && (session.m_bConnecting != 0 || session.m_bConnectionActive != 0);
        }
    }
}
