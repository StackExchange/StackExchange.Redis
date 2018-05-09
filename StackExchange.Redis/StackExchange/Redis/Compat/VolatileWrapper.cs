namespace StackExchange.Redis
{
    internal static class VolatileWrapper
    {
        public static int Read(ref int location)
        {
#if NETSTANDARD1_5
            return System.Threading.Volatile.Read(ref location);
#else
            return System.Threading.Thread.VolatileRead(ref location);
#endif
        }

        public static void Write(ref int address, int value)
        {
#if NETSTANDARD1_5
            System.Threading.Volatile.Write(ref address, value);
#else
            System.Threading.Thread.VolatileWrite(ref address, value);
#endif
        }
    }
}
