using System.Threading;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    private static int _collectedWithoutDispose;
    internal static int CollectedWithoutDispose => Thread.VolatileRead(ref _collectedWithoutDispose);

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
