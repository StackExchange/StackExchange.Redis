using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. Everything about a client-side cache that is settled once, when the connection
    /// is made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Options versus policy.</b> The test for which side a setting belongs on is whether it changes what
    /// we <i>send</i> or what we <i>record</i>, or only how we <i>interpret</i> what we already hold. The
    /// first kind is a fact about the connection and lives here. The second kind is
    /// <see cref="CachePolicy"/>, and can vary per call, because it is applied when an entry is read and
    /// never stamped when it is stored - which it has to be, since entries are shared and one copy must
    /// serve callers with different tolerances.
    /// </para>
    /// <para>
    /// <see cref="Prefixes"/> is the clearest case: it <i>is</i> the argument list sent in
    /// <c>CLIENT TRACKING ... BCAST</c>, so it cannot vary per call - the server was only told once.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public sealed class CacheOptions
    {
        /// <summary>The default options, used when none are given.</summary>
        public static CacheOptions Default { get; } = new();

        /// <summary>Whether a cache is created at all.</summary>
        /// <remarks>
        /// The global switch. Suppressing the cache for one <i>command</i> is
        /// <see cref="CommandFlags.NoClientCache"/>, which suppresses the probe as well as the store; this
        /// is the deployment-level decision that there is no cache to probe.
        /// </remarks>
        public bool Enabled { get; init; } = true;

        /// <summary>How entries behave, unless a caller says otherwise.</summary>
        public CachePolicy DefaultPolicy { get; init; } = CachePolicy.Default;

        /// <summary>
        /// The key prefixes this connection asks the server to track; empty means all keys.
        /// </summary>
        /// <remarks>
        /// <para>
        /// These are the <c>PREFIX</c> arguments of <c>CLIENT TRACKING ... BCAST</c>, and they are declared
        /// here rather than derived from anything because the server's rules are not the library's: prefixes
        /// are <b>connection-global</b>, must not overlap one another, and cannot be removed individually.
        /// Context key-prefixes routinely nest, so they are the wrong source. See design notes 6.13.
        /// </para>
        /// <para>
        /// <b>A prefix list is also a statement about what may be cached.</b> Under <c>BCAST</c> the server
        /// announces only keys matching a prefix, so an entry whose key matches none of them has no
        /// invalidation path - nothing will ever say it is wrong, and it is served until
        /// <see cref="CachePolicy.TimeToLive"/> alone retires it. That is the same defect as caching a
        /// keyless reply, and it is refused the same way: see
        /// <see cref="RespClientCache.RefusedNotTracked"/>.
        /// </para>
        /// <para>
        /// Narrowing the prefix list therefore narrows the cache. That is the trade being made: broadcasting
        /// everything means being told about every key any client touches, and scoping it down buys quiet at
        /// the cost of only caching what is in scope.
        /// </para>
        /// <para>
        /// Empty - the default - means <c>BCAST</c> with no prefix: every key is tracked, so every key is
        /// cacheable. An empty or null entry is not a way to spell that; it is rejected, because
        /// <c>""</c> matches everything and would silently turn a narrow list into a total one.
        /// </para>
        /// </remarks>
        public IReadOnlyList<string> Prefixes
        {
            get => _prefixes;
            init
            {
                _prefixes = value ?? throw new ArgumentNullException(nameof(value));
                _prefixBytes = Encode(_prefixes);
            }
        }

        private readonly IReadOnlyList<string> _prefixes = Array.Empty<string>();
        private readonly byte[][] _prefixBytes = [];

        /// <summary>Whether <see cref="Prefixes"/> restricts what may be cached.</summary>
        internal bool HasPrefixes => _prefixBytes.Length != 0;

        /// <summary>
        /// Whether a key is inside the tracked set, and so has something that can invalidate it.
        /// </summary>
        /// <remarks>
        /// Compared as <b>bytes</b>, against the key as it was written to the wire. That is the only
        /// comparison that means anything: the server matches the bytes it received and names those bytes
        /// back, so anything done to the key on the way out - a context key-prefix, keyspace isolation - is
        /// already baked in by the time it gets here.
        /// </remarks>
        internal bool IsTracked(scoped ReadOnlySpan<byte> key)
        {
            var prefixes = _prefixBytes;
            for (var i = 0; i < prefixes.Length; i++)
            {
                if (key.StartsWith(prefixes[i])) return true;
            }

            return false;
        }

        /// <remarks>
        /// Overlap is rejected rather than tolerated because the server rejects it: <c>CLIENT TRACKING</c>
        /// refuses a prefix list where one entry is a prefix of another. Catching it here means the failure
        /// arrives where the mistake was made, rather than as a handshake error much later.
        /// </remarks>
        private static byte[][] Encode(IReadOnlyList<string> prefixes)
        {
            if (prefixes.Count == 0) return [];

            var result = new byte[prefixes.Count][];
            for (var i = 0; i < prefixes.Count; i++)
            {
                var prefix = prefixes[i];
                if (string.IsNullOrEmpty(prefix))
                {
                    throw new ArgumentException(
                        "An empty cache prefix matches every key; use an empty prefix list to track everything.",
                        nameof(Prefixes));
                }

                result[i] = Encoding.UTF8.GetBytes(prefix);
            }

            for (var i = 0; i < result.Length; i++)
            {
                for (var j = 0; j < result.Length; j++)
                {
                    if (i != j && result[i].AsSpan().StartsWith(result[j]))
                    {
                        throw new ArgumentException(
                            $"Cache prefixes must not overlap, but '{prefixes[i]}' starts with '{prefixes[j]}'.",
                            nameof(Prefixes));
                    }
                }
            }

            return result;
        }
    }
}
