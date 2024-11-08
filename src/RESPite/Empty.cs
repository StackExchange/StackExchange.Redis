using System;
using System.Threading.Tasks;

namespace RESPite;

/// <summary>
/// An empty response or parameter.
/// </summary>
public readonly struct Empty : IEquatable<Empty>
{
    private static readonly Empty s_Value = default;

    /// <summary>
    /// Provides an empty instance.
    /// </summary>
    public static ref readonly Empty Value => ref s_Value;

    /// <summary>
    /// Provides an empty instance.
    /// </summary>
    public static Task<Empty> CompletedTask { get; } = Task.FromResult(s_Value);

    /// <inheritdoc/>
    public override string ToString() => nameof(Empty);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Empty;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;

    bool IEquatable<Empty>.Equals(Empty other) => true;
}
