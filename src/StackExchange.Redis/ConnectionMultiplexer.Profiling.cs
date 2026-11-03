using System;
using StackExchange.Redis.Profiling;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    private Func<ProfilingSession?>? _profilingSessionProvider;

    /// <summary>
    /// Register a callback to provide an on-demand ambient session provider based on the
    /// calling context; the implementing code is responsible for reliably resolving the same provider
    /// based on ambient context, or returning null to not profile.
    /// </summary>
    /// <param name="profilingSessionProvider">The session provider to register.</param>
    public void RegisterProfiler(Func<ProfilingSession?> profilingSessionProvider) => _profilingSessionProvider = profilingSessionProvider;

    /// <summary>
    /// Gives the message somewhere to record its timings, if a profiler is registered and has a session for the
    /// calling context. Every path that hands a message to a server calls this once the server is known: single
    /// commands via <c>PrepareToPushMessageToBridge</c>, and batches, which go to the bridge directly (#2175).
    /// </summary>
    internal void AttachProfiling(Message message, ServerEndPoint server)
    {
        var profilingSession = _profilingSessionProvider?.Invoke();
        if (profilingSession != null)
        {
            message.SetProfileStorage(ProfiledCommand.NewWithContext(profilingSession, server));
        }
    }
}
