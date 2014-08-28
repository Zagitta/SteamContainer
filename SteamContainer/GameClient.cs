using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ProtoBuf;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.Internal;

namespace SteamContainer
{
    /// <summary>
    /// A synchronous wrapper around sending messages to game coordinators for various games.
    /// </summary>
    /// <remarks>
    /// Make sure to frequently run callbacks on the provided CallbackManager and that the SteamClient is ready
    /// </remarks>
    public class GameClient
    {
        //5 second timeout
        private const int Timeout = 5000;
        
        private readonly SteamClient _steam;
        private readonly SteamGameCoordinator _coordinator;
        private readonly Func<Type, uint> _typeToId;
        private readonly uint _appId;
        private readonly ConcurrentDictionary<ulong, TaskCompletionSource<IPacketGCMsg>> _messages;
        private readonly ConcurrentDictionary<uint, ConcurrentBag<Action<IPacketGCMsg>>> _events;
        private readonly AutoResetEvent _enteredGameEevent = new AutoResetEvent(false);
        private bool _inGame;
        private readonly SteamUser _user;

        /// <summary>
        /// Initializes a new instance of the GameClient for a specific game.
        /// </summary>
        /// <param name="steam">A ready to use SteamClient from SteamKit2.</param>
        /// <param name="mng">A CallbackManager associated with the provided SteamClient.</param>
        /// <param name="typeToId">A method that takes a type and returns the id corresponding to the ExxxGCMsg enum type.</param>
        /// <param name="appId">The application id to send messages to.</param>
        public GameClient(SteamClient steam, CallbackManager mng, Func<Type, uint> typeToId, uint appId)
        {
            _steam = steam;
            _typeToId = typeToId;
            _appId = appId;
            _coordinator = _steam.GetHandler<SteamGameCoordinator>();
            _user = _steam.GetHandler<SteamUser>();

            if(_steam.GetHandler<PlayingSession>() == null)
                _steam.AddHandler(new PlayingSession());
            
            _messages = new ConcurrentDictionary<ulong, TaskCompletionSource<IPacketGCMsg>>();
            _events = new ConcurrentDictionary<uint, ConcurrentBag<Action<IPacketGCMsg>>>();

            new Callback<SteamGameCoordinator.MessageCallback>(OnGCMessage, mng);
            new Callback<PlayingSession.StateCallback>(OnPlayingSession, mng);
        }

        private void OnGCMessage(SteamGameCoordinator.MessageCallback cb)
        {
            //First handle messages beign waited for in SendMessage function
            TaskCompletionSource<IPacketGCMsg> tcs;
            ulong id = cb.Message.TargetJobID;

            if (_messages.TryRemove(id, out tcs))
            {
                tcs.SetResult(cb.Message);
            }

            //Then dispatch messages to registered handlers
            ConcurrentBag<Action<IPacketGCMsg>> bag;
            
            if(_events.TryGetValue(cb.Message.MsgType, out bag))
            {
                foreach (var action in bag)
                {
                    //copy variable to closure to avoid issues with c# 4
                    Action<IPacketGCMsg> handler = action;
                    Task.Run(() => handler(cb.Message));
                }
            }
        }

        private void OnPlayingSession(PlayingSession.StateCallback stateCallback)
        {
            if (stateCallback.PlayingBlocked || stateCallback.AppId != _appId)
                return;

            _enteredGameEevent.Set();
        }

        /// <summary>
        /// Blocks the current thread until the game has been entered.
        /// </summary>
        /// <returns>Returns whether the game was entered or not</returns>
        public bool EnterGame()
        {
            if (_inGame)
                return true;

            var r = new Random();

            var packet = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed)
            {
                SourceJobID = _steam.GetNextJobID()
            };

            //emulate windows
            packet.Body.client_os_type = 14;

            packet.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_id = _appId,
                process_id = (uint)r.Next(0, 10000),
                owner_id = (uint)_user.SteamID
            });

            for (int i = 0; i < 3; i++)
            {
                _steam.Send(packet);

                if (_enteredGameEevent.WaitOne(Timeout))
                {
                    return _inGame = true;
                }
            }

            return _inGame = false;
        }

        /// <summary>
        /// Sends a message and blocks until a corresponding message has been received.
        /// </summary>
        /// <typeparam name="TMsg">The type of message being sent.</typeparam>
        /// <typeparam name="TResponse">The type of message being received.</typeparam>
        /// <param name="msgInit">Message initializing function</param>
        /// <returns></returns>
        public TResponse SendMessage<TMsg, TResponse>(Action<TMsg> msgInit)
            where TResponse : IExtensible, new()
            where TMsg : IExtensible, new()
        {
            ulong id = _steam.GetNextJobID();
            var msgType = _typeToId(typeof(TMsg));
            var resType = _typeToId(typeof (TResponse));
            var msg = new ClientGCMsgProtobuf<TMsg>(msgType)
            {
                SourceJobID = id
            };

            msgInit(msg.Body);

            var tcs = new TaskCompletionSource<IPacketGCMsg>();

            _messages.TryAdd(id, tcs);

            for (int i = 0; i < 3; i++)
            {
                _coordinator.Send(msg, _appId);

                if (tcs.Task.Wait(Timeout))
                {
                    var res = tcs.Task.Result;

                    if(res.MsgType != resType)
                        throw new InvalidOperationException("Wrong message type received for id");

                    var gcMsg = new ClientGCMsgProtobuf<TResponse>(res);

                    return gcMsg.Body;
                }
            }

            return default(TResponse);
        }

        /// <summary>
        /// Registers a handler to be run when a certain message is received.
        /// </summary>
        /// <remarks>Don't execute long running commands in the handler as it's run on the threadpool.</remarks>
        /// <typeparam name="TMsg">The type of message the handler should receive.</typeparam>
        /// <param name="handler">The action to execute on message reception.</param>
        public void RegisterMessageHandler<TMsg>(Action<TMsg> handler) where TMsg : IExtensible, new()
        {
            var bag = _events.GetOrAdd(_typeToId(typeof (TMsg)), new ConcurrentBag<Action<IPacketGCMsg>>());

            bag.Add(pkt =>
            {
                var msg = new ClientGCMsgProtobuf<TMsg>(pkt);
                handler(msg.Body);
            });
        }
    }
}
