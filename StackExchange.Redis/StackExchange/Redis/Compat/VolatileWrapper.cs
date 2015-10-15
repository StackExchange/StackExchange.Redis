using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal static class VolatileWrapper
    {
        public static int Read(ref int location)
        {
#if !NETCORE
            return System.Threading.Thread.VolatileRead(ref location);
#else
            return System.Threading.Volatile.Read(ref location);
#endif
        }

        public static void Write(ref int address, int value)
        {
#if !NETCORE
            System.Threading.Thread.VolatileWrite(ref address, value);
#else
            System.Threading.Volatile.Write(ref address, value);
#endif
        }
    }
}
