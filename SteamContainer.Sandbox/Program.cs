using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using SteamContainer.Util;
using SteamKit2;
using SteamKit2.GC.Dota.Internal;

namespace SteamContainer.Sandbox
{
    class Program
    {
        class LoginProvider : ISteamLoginProvider
        {
            public SteamAccount GetAccount()
            {
                return new SteamAccount("XXXX", "XXXXXX");
            }

            public void ReturnAccount(ref SteamAccount account, ReturnReason reason)
            {
                account = null;
            }
        }


        private const uint appId = 570;

        static void Main(string[] args)
        {
            SteamClient c = new SteamClient();
            CallbackManager mng = new CallbackManager(c);

            SteamController con = new SteamController(c, mng, new LoginProvider());

            GameClient gc = new GameClient(c, mng, TypeToEDOTAGCMsg.DotA2TypeToId, appId);

            con.Start();

            con.WaitReady();
            
            gc.EnterGame();

            var match = gc.SendMessage<CMsgGCMatchDetailsRequest, CMsgGCMatchDetailsResponse>(req =>
            {
                req.match_id = 834391254;
            });

            PrintMatchDetails(match.match);

            con.Stop();

            Console.ReadLine();
        }


        static void PrintMatchDetails(CMsgDOTAMatch match)
        {
            if (match == null)
            {
                Console.WriteLine("No match details to display");
                return;
            }
            // use some lazy reflection to print out details
            var fields = typeof(CMsgDOTAMatch).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields.OrderBy(f => f.Name))
            {
                var value = field.GetValue(match, null);
                Console.WriteLine("{0}: {1}", field.Name, value);
            }
        }
    }
}
