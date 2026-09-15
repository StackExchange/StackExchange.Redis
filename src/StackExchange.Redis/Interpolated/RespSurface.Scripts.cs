using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using RESPite;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// EXPERIMENTAL SPIKE. The scripting command group: <c>target.Scripts.Evaluate(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Always <c>EVALSHA</c>, with <c>SCRIPT LOAD</c> in front of it when needed.</b> The alternative
    /// considered was a frame that could re-spell itself as <c>EVAL &lt;body&gt;</c> after a
    /// <c>NOSCRIPT</c>; this is better, because it keeps a frame a pure function of its arguments - the
    /// property that lets frames be cached, routed and replayed - and moves the cleverness into
    /// composition, where transactions and <c>HIMPORT</c> already live.
    /// </para>
    /// <para>
    /// <b>This is the unconditional first cut:</b> the pair is sent every time, so nothing yet consults
    /// whether the script is already loaded. That is deliberate. A duplicate <c>SCRIPT LOAD</c> is
    /// idempotent and costs only the body on the wire, which leaves the composition mechanism as the single
    /// thing being proven here. See design notes for the two pieces that follow: a registry holding the
    /// rendered <c>SCRIPT LOAD</c> frame so the body is encoded once ever, and the write-time belief check
    /// that skips the preamble entirely.
    /// </para>
    /// </remarks>
    [Experimental(Experiments.InterpolatedWriter, UrlFormat = Experiments.UrlFormat)]
    public readonly struct RespScripts
    {
        private readonly RespContext _context;

        /// <summary>Group the scripting commands of a context.</summary>
        /// <param name="context">The context to send through.</param>
        public RespScripts(in RespContext context) => _context = context;

        /// <summary>The underlying context.</summary>
        public RespContext Context => _context;
    }

    public static partial class RespSurface
    {
        extension(IRespTarget target)
        {
            /// <summary>The scripting commands.</summary>
            public RespScripts Scripts => new(target.Context);
        }

        extension(in RespContext context)
        {
            /// <summary>The scripting commands.</summary>
            public RespScripts Scripts => new(context);
        }

        /// <summary>EVALSHA, preceded by SCRIPT LOAD so the hash is certain to resolve.</summary>
        /// <param name="scripts">The scripting command group.</param>
        /// <param name="script">The Lua source.</param>
        /// <param name="keys">The keys the script accesses; these route the command.</param>
        /// <param name="args">Everything else the script needs.</param>
        /// <param name="flags">Command flags.</param>
        /// <remarks>
        /// <para>
        /// The hash is computed here rather than taken from the server's reply, which is what lets the body
        /// go on the wire <b>once</b>. The existing path cannot do that - it has no hash until
        /// <c>SCRIPT LOAD</c> answers, so its first call sends <c>SCRIPT LOAD</c> and then <c>EVAL</c>,
        /// carrying the body twice.
        /// </para>
        /// <para>
        /// <b>Every key the script touches must be passed in <paramref name="keys"/>.</b> That is the
        /// server's rule rather than ours - it is how a script routes in a cluster - and it is also what
        /// lets a client-side cache know what to invalidate. A script that reaches a key it did not declare
        /// is already broken before caching enters the picture.
        /// </para>
        /// </remarks>
        public static ValueTask<RespResult> Evaluate(
            this in RespScripts scripts,
            string script,
            ReadOnlySpan<RedisKey> keys = default,
            ReadOnlySpan<RedisValue> args = default,
            CommandFlags flags = CommandFlags.None)
        {
            if (script is null) throw new ArgumentNullException(nameof(script));

            var context = scripts.Context;

            // NoScriptCache means exactly what it says about the protocol: send the body, every time, and
            // take no hash. The script still lands in the server's cache - nothing a client sends can stop
            // that - but in the pool it evicts from, which is the point. It is also why such a script is not
            // admitted to the registry below: the caller has told us it is not worth keeping.
            if ((flags & CommandFlags.NoScriptCache) != 0)
            {
                return context.SendAsync<RespResult>(
                    $"{RedisCommand.EVAL}{(RedisValue)script}{(RedisValue)keys.Length}{keys}{args}",
                    flags.WithDefaultCategory(RedisCommand.EVAL));
            }

            var registry = context.ScriptCache;
            if (registry is null)
            {
                // no registry: render the preamble afresh, which is correct and wasteful
                var hash = Sha1Hex(script);
                var fresh = context.Render($"{RedisCommand.SCRIPT}{RespLiterals.Load}{(RedisValue)script}");
                try
                {
                    return SendPair(in context, ref fresh, hash, keys, args, flags);
                }
                finally
                {
                    fresh.Dispose();
                }
            }

            var preamble = registry.GetPreamble(in context, script, out var known);
            return SendPair(in context, preamble, known, keys, args, flags);
        }

        /// <summary>Render the EVALSHA and send it behind the preamble.</summary>
        private static ValueTask<RespResult> SendPair(
            in RespContext context,
            ref RespFrame preamble,
            string hash,
            ReadOnlySpan<RedisKey> keys,
            ReadOnlySpan<RedisValue> args,
            CommandFlags flags)
        {
            var request = context.Render($"{RedisCommand.EVALSHA}{(RedisValue)hash}{(RedisValue)keys.Length}{keys}{args}");
            try
            {
                return context.SendWithPreambleAsync(
                    ref preamble, ref request, flags.WithDefaultCategory(RedisCommand.EVALSHA), RespHandlers.Result);
            }
            finally
            {
                request.Dispose();
            }
        }

        /// <inheritdoc cref="SendPair(in RespContext, ref RespFrame, string, ReadOnlySpan{RedisKey}, ReadOnlySpan{RedisValue}, CommandFlags)"/>
        /// <remarks>
        /// The registry's preamble owns nothing poolable - it is a fixed array that is never returned - so
        /// it needs no disposal and can be handed over directly.
        /// </remarks>
        private static ValueTask<RespResult> SendPair(
            in RespContext context,
            RespRequest preamble,
            string hash,
            ReadOnlySpan<RedisKey> keys,
            ReadOnlySpan<RedisValue> args,
            CommandFlags flags)
        {
            var request = context.Render($"{RedisCommand.EVALSHA}{(RedisValue)hash}{(RedisValue)keys.Length}{keys}{args}");
            try
            {
                return context.SendWithPreambleAsync(
                    preamble, ref request, flags.WithDefaultCategory(RedisCommand.EVALSHA), RespHandlers.Result);
            }
            finally
            {
                request.Dispose();
            }
        }

        /// <summary>The SHA1 of a script, lower-case hex, as the server would report it.</summary>
        /// <remarks>
        /// Computed rather than learned so that <c>EVALSHA</c> can be written before <c>SCRIPT LOAD</c> has
        /// answered - the whole reason the body travels once instead of twice.
        /// </remarks>
        internal static string Sha1Hex(string script)
        {
            var bytes = Encoding.UTF8.GetBytes(script);
#if NET
            Span<byte> digest = stackalloc byte[20];
            SHA1.HashData(bytes, digest);
#else
            using var sha = SHA1.Create();
            var digest = sha.ComputeHash(bytes);
#endif

            // one path for every target: lower-case hex is what the server reports, and `ToHexStringLower`
            // is too new to use here. Not hot - and once the rendered SCRIPT LOAD frame is cached, this
            // runs once per script rather than once per call.
            const string Hex = "0123456789abcdef";
            var chars = new char[40];
            for (var i = 0; i < 20; i++)
            {
                chars[i * 2] = Hex[digest[i] >> 4];
                chars[(i * 2) + 1] = Hex[digest[i] & 0xF];
            }

            return new string(chars);
        }
    }
}
