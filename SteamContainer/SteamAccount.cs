using System.Security;
using SteamKit2;

namespace SteamContainer
{
    public class SteamAccount
    {
        private readonly string _username;
        private readonly string _password;

        public SteamAccount(string username, string password)
        {
            _username = username;
            _password = password;
        }

        public static explicit operator SteamUser.LogOnDetails(SteamAccount acc)
        {
            return new SteamUser.LogOnDetails {Username = acc._username, Password = acc._password};
        }
    }
}