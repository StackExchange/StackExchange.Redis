using System;
using StackExchange.Redis.Profiling;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    private Func<ProfilingSession>? _profilingSessionProvider;

    /// <summary>
    /// Register a callback to provide an on-demand ambient session provider based on the
    /// calling context; the implementing code is responsible for reliably resolving the same provider
    /// based on ambient context, or returning null to not profile
    /// </summary>
    /// <param name="profilingSessionProvider">The session provider to register.</param>
    public void RegisterProfiler(Func<ProfilingSession> profilingSessionProvider) => _profilingSessionProvider = profilingSessionProvider;
}
