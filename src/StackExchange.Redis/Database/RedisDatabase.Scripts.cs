using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

/// <summary>
/// The scripting commands, over the context surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>These keep the shipped wire behaviour, which means they DO pair.</b> This paragraph previously said
/// the opposite - that the shipped surface sends <c>EVAL</c> with the body every call, uncached, and that
/// pairing would be a behaviour change. That was simply wrong: <c>ScriptEvalMessage.GetMessages</c> asks
/// the endpoint whether it knows the script and returns <c>LoadThenEvaluate()</c> when it does not, so all
/// four shipped entry points already do <c>SCRIPT LOAD</c> then <c>EVALSHA</c> and remember it per server.
/// Not pairing was the divergence, and <c>ScriptLoadPairingTests</c> is the test that says so.
/// </para>
/// <para>
/// A string that looks like a SHA1 is still taken as a hash and sent directly - that is the shipped rule,
/// and there is nothing to load. Only the script-body case goes through the registry.
/// </para>
/// <para>
/// The <c>LuaScript</c> and <c>LoadedLuaScript</c> overloads need nothing here: they extract their
/// parameters and call back into the string and hash forms through <see cref="IDatabase"/>, which is this
/// object, so they travel the same path the moment those two do.
/// </para>
/// </remarks>
internal partial class RedisDatabase
{
    private ValueTask<RedisResult> Eval(string script, RedisKey[]? keys, RedisValue[]? values, bool readOnly, CommandFlags flags)
    {
        if (script is null) throw new ArgumentNullException(nameof(script));

        // a 40-character hex string is taken as a hash, which is the shipped rule; it is why a script
        // whose body happens to look like one cannot be sent by this overload
        var isHash = RespParsers.IsSHA1(script);
        return isHash
            ? _inner.Scripts.EvaluateDirectResult(script.AsRedisValue(), keys ?? [], values ?? [], isHash: true, readOnly, flags)
            : _inner.Scripts.EvaluateResult(script, keys ?? [], values ?? [], readOnly, flags);
    }

    /// <summary>A raw SHA1 digest, sent the way the server expects it.</summary>
    /// <remarks>
    /// <b>Hex, not the raw twenty bytes.</b> The shipped overload takes the digest as
    /// <see cref="byte"/>[] and writes it with <c>WriteSha1AsHex</c>; passing it through as a value sent
    /// twenty binary bytes where the server wanted forty hex characters, and every such call answered
    /// <c>NOSCRIPT</c> - for a script that was loaded, which is about as misleading as an error gets.
    /// The length check is the shipped one too: a wrong-sized array is a caller mistake worth naming here
    /// rather than a mystery at the server.
    /// </remarks>
    private ValueTask<RedisResult> EvalHash(byte[] hash, RedisKey[]? keys, RedisValue[]? values, bool readOnly, CommandFlags flags)
    {
        Required(hash, nameof(hash));
        if (hash.Length != RespParsers.Sha1HashLength)
        {
            throw new ArgumentOutOfRangeException(nameof(hash), "Invalid hash length");
        }

        return _inner.Scripts.EvaluateDirectResult(
            (RedisValue)ToHex(hash), keys ?? [], values ?? [], isHash: true, readOnly, flags);
    }

    private static string ToHex(byte[] hash)
    {
        const string Digits = "0123456789abcdef";
        var chars = new char[hash.Length * 2];
        for (int i = 0, j = 0; i < hash.Length; i++)
        {
            chars[j++] = Digits[hash[i] >> 4];
            chars[j++] = Digits[hash[i] & 0xF];
        }

        return new string(chars);
    }

    /// <inheritdoc/>
    public RedisResult ScriptEvaluate(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Wait(Eval(script, keys, values, readOnly: false, flags));

    /// <inheritdoc/>
    public Task<RedisResult> ScriptEvaluateAsync(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Eval(script, keys, values, readOnly: false, flags).AsTask(AsyncState, flags);

    /// <inheritdoc/>
    public RedisResult ScriptEvaluateReadOnly(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Wait(Eval(script, keys, values, readOnly: true, flags));

    /// <inheritdoc/>
    public Task<RedisResult> ScriptEvaluateReadOnlyAsync(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Eval(script, keys, values, readOnly: true, flags).AsTask(AsyncState, flags);

    /// <inheritdoc/>
    public RedisResult ScriptEvaluate(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Wait(EvalHash(hash, keys, values, readOnly: false, flags));

    /// <inheritdoc/>
    public Task<RedisResult> ScriptEvaluateAsync(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => EvalHash(hash, keys, values, readOnly: false, flags).AsTask(AsyncState, flags);

    /// <inheritdoc/>
    public RedisResult ScriptEvaluateReadOnly(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Wait(EvalHash(hash, keys, values, readOnly: true, flags));

    /// <inheritdoc/>
    public Task<RedisResult> ScriptEvaluateReadOnlyAsync(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => EvalHash(hash, keys, values, readOnly: true, flags).AsTask(AsyncState, flags);

    /// <inheritdoc/>
    public RedisResult ScriptEvaluate(LuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        => script.Evaluate(this, parameters, null, flags);

    /// <inheritdoc/>
    public Task<RedisResult> ScriptEvaluateAsync(LuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        => script.EvaluateAsync(this, parameters, null, flags);

    /// <inheritdoc/>
    public RedisResult ScriptEvaluate(LoadedLuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        => script.Evaluate(this, parameters, withKeyPrefix: null, flags);

    /// <inheritdoc/>
    public Task<RedisResult> ScriptEvaluateAsync(LoadedLuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None)
        => script.EvaluateAsync(this, parameters, withKeyPrefix: null, flags);

    /// <summary>The low-allocation shipped shape, which travels the same uncached route.</summary>
    /// <remarks>
    /// <c>ScriptEvaluateResp</c> is the older surface's own answer to materialising a whole reply, so it
    /// maps onto the context surface's <see cref="RespResult"/> without translation - only the send
    /// differs from <c>Scripts.EvaluateAsync</c>, and it differs for the reason above.
    /// </remarks>
    private ValueTask<RespResult> EvalResp(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, bool readOnly, CommandFlags flags)
    {
        if (script is null) throw new ArgumentNullException(nameof(script));
        var isHash = RespParsers.IsSHA1(script);
        if (isHash) return _inner.Scripts.EvaluateDirectResp(script.AsRedisValue(), keys.Span, values.Span, isHash: true, readOnly, flags);

        return readOnly
            ? _inner.Scripts.EvaluateReadOnlyAsync(script, keys.Span, values.Span, flags)
            : _inner.Scripts.EvaluateAsync(script, keys.Span, values.Span, flags);
    }

    /// <inheritdoc/>
    public RespResult ScriptEvaluateResp(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
        => Wait(EvalResp(script, keys, values, readOnly: false, flags));

    /// <inheritdoc/>
    public Task<RespResult> ScriptEvaluateRespAsync(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
        => EvalResp(script, keys, values, readOnly: false, flags).AsTask(AsyncState, flags);

    /// <inheritdoc/>
    public RespResult ScriptEvaluateReadOnlyResp(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
        => Wait(EvalResp(script, keys, values, readOnly: true, flags));

    /// <inheritdoc/>
    public Task<RespResult> ScriptEvaluateReadOnlyRespAsync(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
        => EvalResp(script, keys, values, readOnly: true, flags).AsTask(AsyncState, flags);
}
