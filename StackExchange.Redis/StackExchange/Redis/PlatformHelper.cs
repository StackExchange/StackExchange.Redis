using System;

namespace StackExchange.Redis
{
    internal static class PlatformHelper
    {
        public static bool IsMono { get; } = Type.GetType("Mono.Runtime") != null;

        public static bool IsUnix { get; } = (int)Environment.OSVersion.Platform == 4
                                          || (int)Environment.OSVersion.Platform == 6
                                          || (int)Environment.OSVersion.Platform == 128;

        public static SocketMode DefaultSocketMode = IsMono && IsUnix ? SocketMode.Async : SocketMode.Poll;
    }
}
