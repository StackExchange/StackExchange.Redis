using System;
using System.IO;
using System.IO.Compression;
using System.Net.Security;

namespace StackExchange.Redis
{
    internal class PlatformHelper
    {
        public static SocketMode DefaultSocketMode = IsMono && IsUnix ? SocketMode.Async : SocketMode.Poll;

        public static bool IsMono { get; } = Type.GetType("Mono.Runtime") != null;
        public static bool IsUnix { get; } = (int)Environment.OSVersion.Platform == 4
                                          || (int)Environment.OSVersion.Platform == 6
                                          || (int)Environment.OSVersion.Platform == 128;

        /// <summary>
        /// Gets the compression level from a string, avoiding a naming bug inside ancient mono versions.
        /// </summary>
        /// <remarks>
        /// See: https://github.com/mono/mono/commit/714efcf7d1f9c9017b370af16bb3117179dd60e5
        /// </remarks>
        /// <param name="level">The level.</param>
        /// <returns></returns>
        public static CompressionLevel GetCompressionLevel(string level)
        {
            try
            {
                return (CompressionLevel)Enum.Parse(typeof(CompressionLevel), level);
            }
            catch (ArgumentException)
            {
                // Oops, ancient mono here.. let's tray again.
                return (CompressionLevel)Enum.Parse(typeof(CompressionLevel), level.Replace(nameof(CompressionLevel.Optimal), "Optional"));
            }
        }
    }
}
