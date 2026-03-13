using System;
using System.Threading;

namespace StackExchange.Redis;

/// <summary>
/// Configuration options for controlling connections to multiple groups.
/// </summary>
public sealed class MultiGroupOptions
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
    /// Create a new options instance.
    /// </summary>
    public MultiGroupOptions()
    {
        CheckInterval = TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// The frequency to check the status of the nodes in the group.
    /// </summary>
    public TimeSpan CheckInterval
    {
        get => field;
        set
        {
            ThrowIfFrozen();
            field = value;
        }
    }

    private void ThrowIfFrozen()
    {
        if (_frozen) Throw();
        static void Throw() => throw new InvalidOperationException($"{nameof(MultiGroupOptions)} is in use and cannot be used.");
    }

    internal void Freeze() => _frozen = true;
}
