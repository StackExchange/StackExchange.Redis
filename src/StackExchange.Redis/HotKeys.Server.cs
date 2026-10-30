using System;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

internal partial class RedisServer
{
    // HOTKEYS: on the RESP context, so these travel on the connection the core holds to this server - while
    // both cores existed, the alternative was a v3 bridge built (and dialled) to carry them.
    public void HotKeysStart(
        HotKeysMetrics metrics = (HotKeysMetrics)~0,
        long count = 0,
        TimeSpan duration = default,
        long sampleRatio = 1,
        int[]? slots = null,
        CommandFlags flags = CommandFlags.None)
        => Wait(Context.Raw.SendAsync(
            $"{RedisCommand.HOTKEYS}{new HotKeysStartOperand(metrics, count, duration, sampleRatio, slots)}", flags));

    public Task HotKeysStartAsync(
        HotKeysMetrics metrics = (HotKeysMetrics)~0,
        long count = 0,
        TimeSpan duration = default,
        long sampleRatio = 1,
        int[]? slots = null,
        CommandFlags flags = CommandFlags.None)
        => Context.Raw.SendAsync(
            $"{RedisCommand.HOTKEYS}{new HotKeysStartOperand(metrics, count, duration, sampleRatio, slots)}", flags)
            .AsTask(asyncState, flags);

    public bool HotKeysStop(CommandFlags flags = CommandFlags.None)
        => Wait(Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RespLiterals.Stop}", flags, HotKeysResult.StopHandler.Instance));

    public Task<bool> HotKeysStopAsync(CommandFlags flags = CommandFlags.None)
        => Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RespLiterals.Stop}", flags, HotKeysResult.StopHandler.Instance)
            .AsTask(asyncState, flags);

    public void HotKeysReset(CommandFlags flags = CommandFlags.None)
        => Wait(Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RespLiterals.Reset}", flags));

    public Task HotKeysResetAsync(CommandFlags flags = CommandFlags.None)
        => Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RespLiterals.Reset}", flags).AsTask(asyncState, flags);

    public HotKeysResult? HotKeysGet(CommandFlags flags = CommandFlags.None)
        => Wait(Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RespLiterals.Get}", flags, HotKeysResult.Handler.Instance));

    public Task<HotKeysResult?> HotKeysGetAsync(CommandFlags flags = CommandFlags.None)
        => Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RespLiterals.Get}", flags, HotKeysResult.Handler.Instance)
            .AsTask(asyncState, flags);

    /// <summary>
    /// The arguments of <c>HOTKEYS START</c>:
    /// <c>START METRICS n [CPU] [NET] [COUNT k] [DURATION s] [SAMPLE ratio] [SLOTS n slot...]</c>.
    /// </summary>
    /// <remarks>
    /// One operand for the same reason as the stream operands: the clauses are ordered with respect to one
    /// another and most of them are conditional, which an interpolation cannot say without a hole per
    /// maybe-absent token. This used to render a <c>RedisValue[]</c> and splice it in; the operand writes
    /// the same tokens straight into the frame.
    /// </remarks>
    internal readonly struct HotKeysStartOperand(
        HotKeysMetrics metrics,
        long count,
        TimeSpan duration,
        long sampleRatio,
        int[]? slots) : IRespArgument
    {
        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            handler.AppendFormatted(RespLiterals.Start);
            handler.AppendFormatted(RespLiterals.Metrics);
            bool cpu = (metrics & HotKeysMetrics.Cpu) != 0, net = (metrics & HotKeysMetrics.Network) != 0;
            handler.AppendFormatted((RedisValue)((cpu ? 1 : 0) + (net ? 1 : 0)));
            if (cpu) handler.AppendFormatted(RespLiterals.Cpu);
            if (net) handler.AppendFormatted(RespLiterals.Net);

            if (count != 0)
            {
                handler.AppendFormatted(RespLiterals.Count);
                handler.AppendFormatted((RedisValue)count);
            }

            if (duration != TimeSpan.Zero)
            {
                handler.AppendFormatted(RespLiterals.Duration);
                handler.AppendFormatted((RedisValue)Math.Ceiling(duration.TotalSeconds));
            }

            if (sampleRatio != 1)
            {
                handler.AppendFormatted(RespLiterals.Sample);
                handler.AppendFormatted((RedisValue)sampleRatio);
            }

            if (slots is { Length: > 0 })
            {
                handler.AppendFormatted(RespLiterals.Slots);
                handler.AppendFormatted((RedisValue)slots.Length);
                foreach (var slot in slots) handler.AppendFormatted((RedisValue)slot);
            }
        }
    }
}
