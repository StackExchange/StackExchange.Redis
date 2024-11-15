using System;
using System.ComponentModel;

namespace RESPite.Resp;

/// <summary>
/// Represents a <see cref="SimpleString"/> with an associated lifetime.
/// </summary>
public readonly struct LeasedString : IDisposable
{
    /// <inheritdoc />
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals(object? obj) => throw new NotSupportedException();

    /// <inheritdoc />
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() => throw new NotSupportedException();

    /// <inheritdoc />
    public override string? ToString() => Value.ToString();

    private readonly Lease<byte> _lease;

    /// <inheritdoc cref="SimpleString.IsNull"/>
    public bool IsNull => _lease.IsNull;

    /// <inheritdoc cref="SimpleString.IsEmpty"/>
    public bool IsEmpty => _lease.IsEmpty;

    /// <summary>
    /// Gets the leased value as a <see cref="SimpleString"/>.
    /// </summary>
    public SimpleString Value => IsNull ? default : new(_lease.Memory);

    /// <summary>
    /// Gets an empty instance.
    /// </summary>
    public static LeasedString Empty { get; } = new(0, out var _);

    /// <inheritdoc cref="Value"/>
    public static implicit operator SimpleString(in LeasedString value) => value.Value;

    /// <summary>
    /// Create a new leased buffer of the requested length.
    /// </summary>
    public LeasedString(int length, out Memory<byte> memory)
    {
        _lease = new Lease<byte>(length);
        memory = _lease.Memory;
    }

    internal LeasedString(byte[] buffer, int length)
        => _lease = new Lease<byte>(buffer, length);

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose() => _lease.Dispose();
}
