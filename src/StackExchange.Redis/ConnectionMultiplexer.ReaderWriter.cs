using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    internal SocketManager? SocketManager { get; private set; }

    [MemberNotNull(nameof(SocketManager))]
    private void OnCreateReaderWriter(ConfigurationOptions configuration)
    {
        SocketManager = configuration.SocketManager ?? GetDefaultSocketManager();
    }

    private void OnCloseReaderWriter()
    {
        SocketManager = null;
    }

    /// <summary>
    /// .NET 6.0+ has changes to sync-over-async stalls in the .NET primary thread pool
    /// If we're in that environment, by default remove the overhead of our own threadpool
    /// This will eliminate some context-switching overhead and better-size threads on both large
    /// and small environments, from 16 core machines to single core VMs where the default 10 threads
    /// isn't an ideal situation.
    /// </summary>
    internal static SocketManager GetDefaultSocketManager()
    {
#if NET6_0_OR_GREATER
        return SocketManager.ThreadPool;
#else
        return SocketManager.Shared;
#endif
    }
}
