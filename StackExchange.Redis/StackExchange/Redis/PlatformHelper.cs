using System;
using System.IO.Compression;

namespace StackExchange.Redis
{
    internal class PlatformHelper
    {
        public static SocketMode DefaultSocketMode => IsMono && IsUnix ? SocketMode.Async : SocketMode.Poll;

        public static bool IsMono => Type.GetType("Mono.Runtime") != null;
        public static bool IsUnix => (int)Environment.OSVersion.Platform == 4
                                  || (int)Environment.OSVersion.Platform == 6
                                  || (int)Environment.OSVersion.Platform == 128;
    }
}
