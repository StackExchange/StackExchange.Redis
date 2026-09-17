using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// The scripts commands.
/// </summary>
/// <remarks>
/// Here rather than in a single surface-wide class, so that a group is one place. The extension methods
/// bind by namespace, and the namespace is <c>StackExchange.Redis</c>, so this costs a caller nothing.
/// </remarks>
public static partial class Scripts
{
    /// <summary>EVALSHA, preceded by SCRIPT LOAD so the hash is certain to resolve.</summary>
    /// <param name="scripts">The scripting command group.</param>
    /// <param name="script">The Lua source.</param>
    /// <param name="keys">The keys the script accesses; these route the command.</param>
    /// <param name="args">Everything else the script needs.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
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
    public static ValueTask<RespResult> EvaluateAsync(
        this in RespScripts scripts,
        string script,
        ReadOnlySpan<RedisKey> keys = default,
        ReadOnlySpan<RedisValue> args = default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => Evaluate(in scripts, script, keys, args, flags, readOnly: false);

    private static ValueTask<RespResult> Evaluate(
        in RespScripts scripts,
        string script,
        ReadOnlySpan<RedisKey> keys,
        ReadOnlySpan<RedisValue> args,
        CommandFlags flags,
        bool readOnly)
    {
        if (script is null) throw new ArgumentNullException(nameof(script));

        var context = scripts.Context;

        // NoScriptCache means exactly what it says about the protocol: send the body, every time, and
        // take no hash. The script still lands in the server's cache - nothing a client sends can stop
        // that - but in the pool it evicts from, which is the point. It is also why such a script is not
        // admitted to the registry below: the caller has told us it is not worth keeping.
        if ((flags & CommandFlags.NoScriptCache) != 0)
        {
            var eval = readOnly ? RedisCommand.EVAL_RO : RedisCommand.EVAL;
            return context.SendAsync<RespResult>(
                $"{eval}{script.AsRedisValue()}{(RedisValue)keys.Length}{keys}{args}",
                flags);
        }

        var registry = context.ScriptCache;
        if (registry is null)
        {
            // no registry: render the preamble afresh, which is correct and wasteful
            var hash = Sha1Hex(script);
            var fresh = context.Render($"{RedisCommand.SCRIPT}{RespLiterals.Load}{script.AsRedisValue()}");
            try
            {
                // a gate per call here, where the registry keeps one per script: without a registry
                // there is nowhere to keep it, and the skip is worth more than the allocation
                return SendPair(in context, ref fresh, hash, keys, args, flags, readOnly, new ScriptLoadGate(script, hash));
            }
            finally
            {
                fresh.Dispose();
            }
        }

        var preamble = registry.GetPreamble(in context, script, out var known, out var gate);
        return SendPair(in context, preamble, known, keys, args, flags, readOnly, gate);
    }

    /// <summary>EVALSHA_RO, preceded by SCRIPT LOAD; the read-only form of <c>Evaluate</c>.</summary>
    /// <param name="scripts">The scripting command group.</param>
    /// <param name="script">The Lua source; it must not write.</param>
    /// <param name="keys">The keys the script accesses; these route the command.</param>
    /// <param name="args">Everything else the script needs.</param>
    /// <param name="flags">Command flags.</param>
    /// <param name="cancellationToken">Cancels the request; only cancellation <i>before</i> the send is honoured today.</param>
    /// <remarks>
    /// <para>
    /// A separate method rather than a flag on <c>Evaluate</c>, because it is a separate command with a
    /// different retry category: <c>EVALSHA_RO</c> is read-only, where <c>EVALSHA</c> must be treated as
    /// a write. Hiding that behind a boolean would let a caller's retry semantics change invisibly.
    /// </para>
    /// <para>
    /// <b>Requires a server that has the read-only forms</b> (7.0 and later). The older surface probes
    /// the connection and falls back; this does not, because the frame is chosen before the connection
    /// is - which is the same constraint that puts the loaded-script belief at write time.
    /// </para>
    /// </remarks>
    public static ValueTask<RespResult> EvaluateReadOnlyAsync(
        this in RespScripts scripts,
        string script,
        ReadOnlySpan<RedisKey> keys = default,
        ReadOnlySpan<RedisValue> args = default,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => Evaluate(in scripts, script, keys, args, flags, readOnly: true);

    /// <summary>Render the EVALSHA and send it behind the preamble.</summary>
    private static ValueTask<RespResult> SendPair(
        in RespContext context,
        ref RespRequestFrame preamble,
        string hash,
        ReadOnlySpan<RedisKey> keys,
        ReadOnlySpan<RedisValue> args,
        CommandFlags flags,
        bool readOnly,
        IRespPreambleGate gate)
    {
        var command = readOnly ? RedisCommand.EVALSHA_RO : RedisCommand.EVALSHA;
        var request = context.Render($"{command}{hash.AsRedisValue()}{(RedisValue)keys.Length}{keys}{args}");
        try
        {
            return context.SendWithPreambleAsync(
                ref preamble, ref request, flags, RespHandlers.Result, gate);
        }
        finally
        {
            request.Dispose();
        }
    }

    /// <inheritdoc cref="SendPair(in RespContext, ref RespRequestFrame, string, ReadOnlySpan{RedisKey}, ReadOnlySpan{RedisValue}, CommandFlags, bool, IRespPreambleGate)"/>
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
        CommandFlags flags,
        bool readOnly,
        IRespPreambleGate gate)
    {
        var command = readOnly ? RedisCommand.EVALSHA_RO : RedisCommand.EVALSHA;
        var request = context.Render($"{command}{hash.AsRedisValue()}{(RedisValue)keys.Length}{keys}{args}");
        try
        {
            return context.SendWithPreambleAsync(
                preamble, ref request, flags, RespHandlers.Result, gate);
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
