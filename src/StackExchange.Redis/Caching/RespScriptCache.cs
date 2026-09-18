using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using RESPite;
using RESPite.Buffers;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis.Caching
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The rendered <c>SCRIPT LOAD</c> for a script, and its hash, kept so the body is
    /// encoded once rather than once per call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A request cache, not a response cache.</b> It looks superficially like
    /// <see cref="RespClientCache"/> and behaves oppositely: it is keyed by the script text rather than by
    /// a frame, and it <b>never invalidates</b>, because the bytes of <c>SCRIPT LOAD &lt;body&gt;</c> and
    /// the SHA of a script are pure functions of that script. Only the belief about whether a
    /// <i>server</i> holds it is soft, and that lives elsewhere.
    /// </para>
    /// <para>
    /// <b>Right-sized arrays, not pooled rents.</b> Entries live for the life of the connection, and a rent
    /// held indefinitely is worse than wasteful - it is permanently removed from the pool, which degrades
    /// the pool for everything else. So the frame is rendered into a pooled buffer, copied out to an exact
    /// array, and the rent is handed straight back. One allocation per script, in the generation where
    /// long-lived things belong.
    /// </para>
    /// <para>
    /// <b>Bounded by the flag rather than by a limit.</b> The population that would grow this without end
    /// is scripts generated per call - which the Redis documentation calls an anti-pattern, and which
    /// <see cref="CommandFlags.NoScriptCache"/> already marks. Those are not admitted, so the flag means
    /// "transient" on both sides: the server puts such a script in its evictable pool, and we do not retain
    /// its rendering. A well-behaved application has a handful of scripts.
    /// </para>
    /// </remarks>
    public sealed class RespScriptCache
    {
        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        /// <summary>The number of scripts whose rendering is held.</summary>
        public int Count => _entries.Count;

        /// <summary>Scripts rendered for the first time; compare with <see cref="Count"/> to see churn.</summary>
        public long Rendered => Volatile.Read(ref _rendered);

        /// <summary>
        /// The bytes held by cached renderings - exactly, because the arrays are exactly sized.
        /// </summary>
        /// <remarks>
        /// Worth exposing rather than inferring: it is the one externally visible difference between
        /// keeping a copy and keeping the pooled rent it was made from. A rent is bucket-sized, so this
        /// number would exceed the bytes actually on the wire; a copy makes them equal. That is the whole
        /// claim this type makes about its memory, so it should be checkable.
        /// </remarks>
        public long Bytes
        {
            get
            {
                long total = 0;
                foreach (var pair in _entries) total += pair.Value.Length;
                return total;
            }
        }

        private long _rendered;

        /// <summary>
        /// The hash and the rendered <c>SCRIPT LOAD</c> for a script, rendering it if this is the first time.
        /// </summary>
        /// <param name="context">The context to render through.</param>
        /// <param name="script">The Lua source.</param>
        /// <param name="hash">The script's SHA1, lower-case hex, as the server reports it.</param>
        /// <param name="gate">Decides at write time whether the endpoint still needs this preamble.</param>
        /// <returns>A request over the cached bytes; it owns nothing poolable, so disposing it is a no-op.</returns>
        internal RespRequest GetPreamble(in RespContext context, string script, out string hash, out IRespPreambleGate gate)
        {
            if (!_entries.TryGetValue(script, out var entry))
            {
                entry = Render(in context, script);

                // a race just renders twice and keeps whichever landed; both are byte-identical, and the
                // loser is an exact array that the GC takes, not a rent that had to be given back
                entry = _entries.GetOrAdd(script, entry);
            }

            hash = entry.Hash;
            gate = entry.Gate;
            return entry.AsRequest();
        }

        private Entry Render(in RespContext context, string script)
        {
            Interlocked.Increment(ref _rendered);

            var frame = context.Render($"{RedisCommand.SCRIPT}{RespLiterals.Load}{script.AsRedisValue()}");
            try
            {
                return new Entry(script, Scripts.Sha1Hex(script), frame.Span.ToArray(), frame.ArgCount);
            }
            finally
            {
                // the rent goes back immediately; what we keep is the copy
                frame.Dispose();
            }
        }

        private sealed class Entry(string script, string hash, byte[] bytes, int argCount)
        {
            internal string Hash { get; } = hash;

            /// <summary>Whether the endpoint being written to still needs this preamble.</summary>
            /// <remarks>
            /// One per entry rather than one per call: the entry is global and immutable, and so is the
            /// (script, hash) pair the gate closes over. The belief it consults is per-endpoint and lives
            /// on the endpoint, so sharing this costs nothing and keeps the ASCII hash rendered once.
            /// </remarks>
            internal IRespPreambleGate Gate { get; } = new ScriptLoadGate(script, hash);

            /// <summary>The exact size of the rendering; see <see cref="RespScriptCache.Bytes"/>.</summary>
            internal int Length => bytes.Length;

            /// <remarks>
            /// <see cref="RefCountedBuffer.CreateFixed"/>: the array is ours for ever, so the request must
            /// not be able to hand it to a pool when its references drop. Everything downstream - retain,
            /// release, dispose - then works as usual and simply never reclaims anything.
            /// </remarks>
            internal RespRequest AsRequest()
                => new(bytes, RefCountedBuffer.CreateFixed(bytes), 0, bytes.Length, argCount: argCount, command: RedisCommand.SCRIPT);
        }
    }
}
