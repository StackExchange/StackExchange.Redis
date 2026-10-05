using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis
{
    /// <summary>
    /// Per-key protection flags managed by the <c>BLESS</c> command family.
    /// </summary>
    /// <remarks><seealso href="https://redis.io/commands/bless"/></remarks>
    [Flags]
    [Experimental(Experiments.Server_8_12, UrlFormat = Experiments.UrlFormat)]
    public enum BlessFlags
    {
        /// <summary>
        /// No flags; this is the state of an unblessed key, and is not valid as a command argument.
        /// </summary>
        None = 0,

        /// <summary>
        /// The key is never chosen as an eviction victim under any <c>maxmemory-policy</c> (<c>NO-EVICT</c>).
        /// </summary>
        NoEvict = 1 << 0,
    }
}
