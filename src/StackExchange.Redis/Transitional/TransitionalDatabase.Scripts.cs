using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

/// <summary>
/// The scripting commands, over the context surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>These keep the shipped wire behaviour rather than the better one.</b> The string overload chooses
/// <c>EVALSHA</c> when the string looks like a SHA1 and otherwise sends <c>EVAL</c> with the body, every
/// call, uncached - where <c>Scripts.EvaluateAsync</c> does <c>SCRIPT LOAD</c> then <c>EVALSHA</c> and
/// keeps the rendering. Switching existing callers to that would add a load to the first call and leave
/// the script in the server's non-evictable cache, which is a decision rather than a translation.
/// </para>
/// <para>
/// The <c>LuaScript</c> and <c>LoadedLuaScript</c> overloads need nothing here: they extract their
/// parameters and call back into the string and hash forms through <see cref="IDatabase"/>, which is this
/// object, so they travel the same path the moment those two do.
/// </para>
/// </remarks>
internal sealed partial class TransitionalDatabase
{
    private ValueTask<RedisResult> Eval(string script, RedisKey[]? keys, RedisValue[]? values, bool readOnly, CommandFlags flags)
    {
        if (script is null) throw new ArgumentNullException(nameof(script));

        // a 40-character hex string is taken as a hash, which is the shipped rule; it is why a script
        // whose body happens to look like one cannot be sent by this overload
        var isHash = ResultProcessor.ScriptLoadProcessor.IsSHA1(script);
        return _inner.Scripts.EvaluateDirectResult(script.AsRedisValue(), keys ?? [], values ?? [], isHash, readOnly, flags);
    }

    private ValueTask<RedisResult> EvalHash(byte[] hash, RedisKey[]? keys, RedisValue[]? values, bool readOnly, CommandFlags flags)
        => _inner.Scripts.EvaluateDirectResult(
            Required(hash, nameof(hash)), keys ?? [], values ?? [], isHash: true, readOnly, flags);

    /// <inheritdoc/>
    public RedisResult ScriptEvaluate(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Wait(Eval(script, keys, values, readOnly: false, flags));

    /// <inheritdoc/>
    public Task<RedisResult> ScriptEvaluateAsync(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Eval(script, keys, values, readOnly: false, flags).AsTask();

    /// <inheritdoc/>
    public RedisResult ScriptEvaluateReadOnly(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Wait(Eval(script, keys, values, readOnly: true, flags));

    /// <inheritdoc/>
    public Task<RedisResult> ScriptEvaluateReadOnlyAsync(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Eval(script, keys, values, readOnly: true, flags).AsTask();

    /// <inheritdoc/>
    public RedisResult ScriptEvaluate(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Wait(EvalHash(hash, keys, values, readOnly: false, flags));

    /// <inheritdoc/>
    public Task<RedisResult> ScriptEvaluateAsync(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => EvalHash(hash, keys, values, readOnly: false, flags).AsTask();

    /// <inheritdoc/>
    public RedisResult ScriptEvaluateReadOnly(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => Wait(EvalHash(hash, keys, values, readOnly: true, flags));

    /// <inheritdoc/>
    public Task<RedisResult> ScriptEvaluateReadOnlyAsync(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None)
        => EvalHash(hash, keys, values, readOnly: true, flags).AsTask();

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
        var isHash = ResultProcessor.ScriptLoadProcessor.IsSHA1(script);
        return _inner.Scripts.EvaluateDirectResp(script.AsRedisValue(), keys.Span, values.Span, isHash, readOnly, flags);
    }

    /// <inheritdoc/>
    public RespResult ScriptEvaluateResp(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
        => Wait(EvalResp(script, keys, values, readOnly: false, flags));

    /// <inheritdoc/>
    public Task<RespResult> ScriptEvaluateRespAsync(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
        => EvalResp(script, keys, values, readOnly: false, flags).AsTask();

    /// <inheritdoc/>
    public RespResult ScriptEvaluateReadOnlyResp(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
        => Wait(EvalResp(script, keys, values, readOnly: true, flags));

    /// <inheritdoc/>
    public Task<RespResult> ScriptEvaluateReadOnlyRespAsync(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
        => EvalResp(script, keys, values, readOnly: true, flags).AsTask();
}
