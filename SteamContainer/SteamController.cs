using System;
using System.Threading;
using SteamKit2;
using SteamKit2.Internal;

namespace SteamContainer
{
    /// <summary>
    /// A controller class that handles and runs callback on a seperate thread
    /// </summary>
    public class SteamController : IDisposable
    {
        #region SteamKit
        private readonly SteamClient _steam;
        private readonly SteamUser _user;
        private readonly SteamFriends _friends;
        private readonly CallbackManager _manager;
        #endregion

        #region Threading
        private Thread _thread;
        private CancellationTokenSource _cancellationSource;
        private readonly ManualResetEvent _readyEvent = new ManualResetEvent(false);
        static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
        #endregion

        private readonly ISteamLoginProvider _loginProvider;
        private SteamAccount _account;
        private bool _isLoggedIn;
        private int _loginAttemps;
        private int _connectionAttemps;
        private uint _appId;


        public SteamController(SteamClient steam, CallbackManager manager, ISteamLoginProvider loginProvider)
        {
            _steam = steam;
            _manager = manager;
            _loginProvider = loginProvider;

            _user = _steam.GetHandler<SteamUser>();
            _friends = _steam.GetHandler<SteamFriends>();

            MaxLoginAttemps = 3;
            MaxConnectionAttemps = 3;

            #region Callback setup
            new Callback<SteamClient.ConnectedCallback>(OnConnected, _manager);
            new Callback<SteamClient.DisconnectedCallback>(OnDisconnected, _manager);
            new Callback<SteamUser.LoggedOffCallback>(OnLoggedOff, _manager);
            new Callback<SteamUser.LoggedOnCallback>(OnLoggedOn, _manager);
            new Callback<SteamUser.AccountInfoCallback>(OnAccountInfo, _manager);
            #endregion
        }

        #region props

        /// <summary>
        /// Set to true to show as online to friends
        /// </summary>
        public bool ShowAsOnline { get; set; }

        /// <summary>
        /// Amount of attempt to login in a row before aborting
        /// </summary>
        public int MaxLoginAttemps { get; set; }

        /// <summary>
        /// Amount of times to reconnect in a row before aborting
        /// </summary>
        public int MaxConnectionAttemps { get; set; }

        #endregion

        /// <summary>
        /// Blocks the calling thread until the client is ready to send messages
        /// </summary>
        public void WaitReady()
        {
            _readyEvent.WaitOne();
        }
        
        
        #region Steam and DotA message handling
        private void OnLoggedOn(SteamUser.LoggedOnCallback obj)
        {
            if (obj.Result == EResult.OK)
            {
                _isLoggedIn = true;
                _readyEvent.Set();
                return;
            }

            var reason = ReturnReason.None;
            
            switch (obj.Result)
            {
                case EResult.AccountDisabled:
                case EResult.AccountLocked:
                case EResult.AccountLogonDenied:
                case EResult.Banned:
                    reason = ReturnReason.Banned;
                    break;

                case EResult.InvalidName:
                case EResult.InvalidEmail:
                case EResult.InvalidPassword:
                    reason = ReturnReason.Invalid;
                    break;
            }

            _loginProvider.ReturnAccount(ref _account, reason);
        }

        private void OnLoggedOff(SteamUser.LoggedOffCallback obj)
        {
            _readyEvent.Reset();

            _isLoggedIn = false;

            AttemptLogin();
        }

        private void OnDisconnected(SteamClient.DisconnectedCallback obj)
        {
            if (_connectionAttemps++ > MaxConnectionAttemps)
            {
                Abort();
                return;
            }

            _steam.Connect();
        }

        private void OnConnected(SteamClient.ConnectedCallback obj)
        {
            if (obj.Result != EResult.OK) return;

            AttemptLogin();
        }

        private void AttemptLogin()
        {
            if(_account == null)
                _account = _loginProvider.GetAccount();


            if (_loginAttemps++ > MaxLoginAttemps)
            {
                Abort();
                return;
            }

            _user.LogOn((SteamUser.LogOnDetails)_account);
        }

        private void OnAccountInfo(SteamUser.AccountInfoCallback obj)
        {
            if (ShowAsOnline)
            {
                _friends.SetPersonaState(EPersonaState.Online);
            }
        }
        #endregion


        #region Threading
        public void Start()
        {
            if(_thread != null)
                return;

            _thread = new Thread(Run);

            _cancellationSource = new CancellationTokenSource();

            _thread.Start(_cancellationSource.Token);
        }

        public void Stop()
        {
            if(_thread == null)
                return;

            _cancellationSource.Cancel();
            _thread.Join();
            _thread = null;
        }

        private void Abort()
        {
            _cancellationSource.Cancel();
            _readyEvent.Reset();
        }
        
        private void Run(object parm)
        {
            var token = (CancellationToken)parm;

            _steam.Connect();

            while (token.IsCancellationRequested == false)
            {
                _manager.RunWaitCallbacks(Timeout);
            }

            _steam.Disconnect();
        }
        #endregion

        public void Dispose()
        {
            _loginProvider.ReturnAccount(ref _account, ReturnReason.None);
            _cancellationSource.Dispose();
            _readyEvent.Dispose();
        }
    }
}
