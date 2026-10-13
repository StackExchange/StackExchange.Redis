using System;
using System.Collections.Generic;
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
            $"{RedisCommand.HOTKEYS}{HotKeysStartArgs(metrics, count, duration, sampleRatio, slots)}", flags));

    public Task HotKeysStartAsync(
        HotKeysMetrics metrics = (HotKeysMetrics)~0,
        long count = 0,
        TimeSpan duration = default,
        long sampleRatio = 1,
        int[]? slots = null,
        CommandFlags flags = CommandFlags.None)
        => Context.Raw.SendAsync(
            $"{RedisCommand.HOTKEYS}{HotKeysStartArgs(metrics, count, duration, sampleRatio, slots)}", flags)
            .AsTask(asyncState, flags);

    public bool HotKeysStop(CommandFlags flags = CommandFlags.None)
        => Wait(Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RedisLiterals.STOP}", flags, HotKeysResult.StopHandler.Instance));

    public Task<bool> HotKeysStopAsync(CommandFlags flags = CommandFlags.None)
        => Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RedisLiterals.STOP}", flags, HotKeysResult.StopHandler.Instance)
            .AsTask(asyncState, flags);

    public void HotKeysReset(CommandFlags flags = CommandFlags.None)
        => Wait(Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RedisLiterals.RESET}", flags));

    public Task HotKeysResetAsync(CommandFlags flags = CommandFlags.None)
        => Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RedisLiterals.RESET}", flags).AsTask(asyncState, flags);

    public HotKeysResult? HotKeysGet(CommandFlags flags = CommandFlags.None)
        => Wait(Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RedisLiterals.GET}", flags, HotKeysResult.Handler.Instance));

    public Task<HotKeysResult?> HotKeysGetAsync(CommandFlags flags = CommandFlags.None)
        => Context.Raw.SendAsync($"{RedisCommand.HOTKEYS}{RedisLiterals.GET}", flags, HotKeysResult.Handler.Instance)
            .AsTask(asyncState, flags);

    /// <summary>
    /// The arguments of <c>HOTKEYS START</c>, in the order and spelling <c>HotKeysStartMessage</c> writes them:
    /// <c>START METRICS n [CPU] [NET] [COUNT k] [DURATION s] [SAMPLE ratio] [SLOTS n slot...]</c>.
    /// </summary>
    private static RedisValue[] HotKeysStartArgs(HotKeysMetrics metrics, long count, TimeSpan duration, long sampleRatio, int[]? slots)
    {
        var args = new List<RedisValue> { "START", "METRICS" };
        bool cpu = (metrics & HotKeysMetrics.Cpu) != 0, net = (metrics & HotKeysMetrics.Network) != 0;
        args.Add((cpu ? 1 : 0) + (net ? 1 : 0));
        if (cpu) args.Add("CPU");
        if (net) args.Add("NET");
        if (count != 0)
        {
            args.Add("COUNT");
            args.Add(count);
        }

        if (duration != TimeSpan.Zero)
        {
            args.Add("DURATION");
            args.Add(Math.Ceiling(duration.TotalSeconds));
        }

        if (sampleRatio != 1)
        {
            args.Add("SAMPLE");
            args.Add(sampleRatio);
        }

        if (slots is { Length: > 0 })
        {
            args.Add("SLOTS");
            args.Add(slots.Length);
            foreach (var slot in slots) args.Add(slot);
        }

        return args.ToArray();
    }
}
