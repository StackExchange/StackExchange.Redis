using System.Threading;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    private static int _collectedWithoutDispose, s_DisposedCount, s_MuxerCreateCount;
    internal static int CollectedWithoutDispose => Volatile.Read(ref _collectedWithoutDispose);

    internal static int GetLiveObjectCount(out int created, out int disposed, out int finalized)
    {
        // read destroy first, to prevent negative numbers in race conditions
        disposed = Volatile.Read(ref s_DisposedCount);
        created = Volatile.Read(ref s_MuxerCreateCount);
        finalized = Volatile.Read(ref _collectedWithoutDispose);
        return created - (disposed + finalized);
    }

    /// <summary>
    /// Invoked by the garbage collector.
    /// </summary>
    ~ConnectionMultiplexer()
    {
        Interlocked.Increment(ref _collectedWithoutDispose);
    }

    bool IInternalConnectionMultiplexer.AllowConnect
    {
        get => AllowConnect;
        set => AllowConnect = value;
    }

    bool IInternalConnectionMultiplexer.IgnoreConnect
    {
        get => IgnoreConnect;
        set => IgnoreConnect = value;
    }

    /// <summary>
    /// For debugging: when not enabled, servers cannot connect.
    /// </summary>
    internal volatile bool AllowConnect = true;

    /// <summary>
    /// For debugging: when not enabled, end-connect is silently ignored (to simulate a long-running connect).
    /// </summary>
    internal volatile bool IgnoreConnect;
}
