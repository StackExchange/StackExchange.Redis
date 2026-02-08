using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

internal partial class RedisServer
{
    public void HotKeysStart(
        HotKeysMetrics metrics = (HotKeysMetrics)~0,
        long count = -1,
        TimeSpan duration = default,
        long sampleRatio = 1,
        short[]? slots = null,
        CommandFlags flags = CommandFlags.None)
        => ExecuteSync(
            new HotKeysStartMessage(flags, metrics, count, duration, sampleRatio, slots),
            ResultProcessor.DemandOK);

    public Task HotKeysStartAsync(
        HotKeysMetrics metrics = (HotKeysMetrics)~0,
        long count = -1,
        TimeSpan duration = default,
        long sampleRatio = 1,
        short[]? slots = null,
        CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(
            new HotKeysStartMessage(flags, metrics, count, duration, sampleRatio, slots),
            ResultProcessor.DemandOK);

    public void HotKeysStop(CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(Message.Create(-1, flags, RedisCommand.HOTKEYS, RedisLiterals.STOP), ResultProcessor.DemandOK, server);

    public Task HotKeysStopAsync(CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(Message.Create(-1, flags, RedisCommand.HOTKEYS, RedisLiterals.STOP), ResultProcessor.DemandOK, server);

    public void HotKeysReset(CommandFlags flags = CommandFlags.None)
        => ExecuteSync(Message.Create(-1, flags, RedisCommand.HOTKEYS, RedisLiterals.RESET), ResultProcessor.DemandOK, server);

    public Task HotKeysResetAsync(CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(Message.Create(-1, flags, RedisCommand.HOTKEYS, RedisLiterals.RESET), ResultProcessor.DemandOK, server);

    public HotKeysResult? HotKeysGet(CommandFlags flags = CommandFlags.None)
        => ExecuteSync(Message.Create(-1, flags, RedisCommand.HOTKEYS, RedisLiterals.GET), HotKeysResult.Processor, server);

    public Task<HotKeysResult?> HotKeysGetAsync(CommandFlags flags = CommandFlags.None)
        => ExecuteAsync(Message.Create(-1, flags, RedisCommand.HOTKEYS, RedisLiterals.GET), HotKeysResult.Processor, server);
}
