using System;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interpolated
{
    /// <summary>
    /// The scripting commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="IDatabase"/> spelling of <c>RespSurface.Scripts.cs</c>.
    /// </para>
    /// <para>
    /// <b>Only the <c>Resp</c>-returning members move for now.</b> They are the ones whose shape already
    /// matches - a script, its keys, its arguments, and the reply undecoded. The rest stay generated
    /// deliberately rather than by omission: the <see cref="RedisResult"/> forms need a
    /// <see cref="RespResult"/> conversion, and the <c>LuaScript</c>, <c>LoadedLuaScript</c> and
    /// hash-addressed overloads carry a loaded-ness the caller asserts rather than one we established -
    /// which is the one entry in the script registry that is not "we loaded this", and wants settling
    /// before it is adapted.
    /// </para>
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public RespResult ScriptEvaluateResp(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Scripts.EvaluateAsync(script, keys.Span, values.Span, flags));

        /// <inheritdoc/>
        public Task<RespResult> ScriptEvaluateRespAsync(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
            => Context.Scripts.EvaluateAsync(script, keys.Span, values.Span, flags).AsTask();

        /// <inheritdoc/>
        public RespResult ScriptEvaluateReadOnlyResp(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
            => Wait(Context.Scripts.EvaluateReadOnlyAsync(script, keys.Span, values.Span, flags));

        /// <inheritdoc/>
        public Task<RespResult> ScriptEvaluateReadOnlyRespAsync(string script, ReadOnlyMemory<RedisKey> keys, ReadOnlyMemory<RedisValue> values, CommandFlags flags = CommandFlags.None)
            => Context.Scripts.EvaluateReadOnlyAsync(script, keys.Span, values.Span, flags).AsTask();
    }
}
