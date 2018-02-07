namespace StackExchange.Redis
{
    internal static class VolatileWrapper
    {
        public static int Read(ref int location)
        {
#if !CORE_CLR
            return System.Threading.Thread.VolatileRead(ref location);
#else
            return System.Threading.Volatile.Read(ref location);
#endif
        }

        public static void Write(ref int address, int value)
        {
#if !CORE_CLR
            System.Threading.Thread.VolatileWrite(ref address, value);
#else
            System.Threading.Volatile.Write(ref address, value);
#endif
        }
    }
}
