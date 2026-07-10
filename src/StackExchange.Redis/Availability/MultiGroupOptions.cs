using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using RESPite;

namespace StackExchange.Redis.Availability;

/// <summary>
/// Configuration options for controlling connections to multiple groups.
/// </summary>
[Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
public sealed class MultiGroupOptions()
{
    private static MultiGroupOptions? _default;
    private bool _frozen;

    /// <summary>
    /// Default shared options.
    /// </summary>
    public static MultiGroupOptions Default => _default ??= CreateDefault();

    private static MultiGroupOptions CreateDefault()
    {
        var options = new MultiGroupOptions();
        options.Freeze();
        return Interlocked.CompareExchange(ref _default, options, null) ?? options;
    }

    /// <summary>
    /// The health check to use for members of the group when no per-member health check is specified.
    /// </summary>
    public HealthCheck HealthCheck
    {
        get => field ?? HealthCheck.Default;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// The circuit-breaker to use for members of the group when no per-member circuit-breaker is specified.
    /// </summary>
    public CircuitBreaker CircuitBreaker
    {
        get => field ?? CircuitBreaker.Default;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// If a member has been marked unhealthy by a failing health-check or circuit-breaker, it will not be
    /// re-selected as the active member until it has remained healthy for this interval following its most
    /// recent failure. When <see cref="TimeSpan.Zero"/> (the default), failback is immediate; when
    /// <see cref="TimeSpan.MaxValue"/>, failback is not automatic and requires explicit use of
    /// <see cref="ConnectionGroupMember.ResetIsUnhealthy"/> or <see cref="IConnectionGroup.TryFailoverTo"/>.
    /// </summary>
    public TimeSpan FailbackDelay
    {
        get => field;
        set => SetField(ref field, value);
    }

    // ReSharper disable once RedundantAssignment
    private void SetField<T>(ref T field, T value, [CallerMemberName] string caller = "")
    {
        if (_frozen) Throw(caller);
        field = value;

        static void Throw(string caller) => throw new InvalidOperationException($"{nameof(MultiGroupOptions)}.{caller} cannot be modified once the object is in use.");
    }

    internal void Freeze() => _frozen = true;
}
