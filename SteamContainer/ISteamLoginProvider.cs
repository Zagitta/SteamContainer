using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamContainer
{
    public interface ISteamLoginProvider
    {
        SteamAccount GetAccount();
        void ReturnAccount(ref SteamAccount account, ReturnReason reason);
    }
}
