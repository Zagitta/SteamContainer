using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Internal;

namespace SteamContainer
{
    class PlayingSession : ClientMsgHandler
    {
        public override void HandleMsg(IPacketMsg packetMsg)
        {
            if (packetMsg.MsgType != EMsg.ClientPlayingSessionState) return;


            var pk = new ClientMsgProtobuf<CMsgClientPlayingSessionState>(packetMsg);

            var body = pk.Body;

            var cb = new StateCallback(body.playing_blocked, body.playing_app);

            Client.PostCallback(cb);
        }

        internal class StateCallback : CallbackMsg
        {
            public bool PlayingBlocked { get; private set; }
            public GameID AppId { get; private set; }

            internal StateCallback(bool playingBlocked, GameID appId)
            {
                PlayingBlocked = playingBlocked;
                AppId = appId;
            }
        }
    }
}
