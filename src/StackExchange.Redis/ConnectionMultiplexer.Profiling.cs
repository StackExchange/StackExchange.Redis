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

    /// <summary>The session to profile into right now, or null when nobody is profiling.</summary>
    /// <remarks>
    /// Exposed for the new core, which creates its records where it dispatches rather than where it
    /// selects a server. Reading the provider per command is the existing behaviour and is the point:
    /// a profiling session is ambient and can change between calls.
    /// </remarks>
    internal ProfilingSession? CurrentProfilingSession => _profilingSessionProvider?.Invoke();
}
