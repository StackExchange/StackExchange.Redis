using System;

namespace RESPite.Resp;

/// <summary>
/// Represents a <see cref="SimpleString"/> with an associated lifetime.
/// </summary>
public readonly struct LeasedString : IDisposable
{
    /// <inheritdoc />
    public override string ToString() => IsNull ? "" : Value.ToString();

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

    /// <summary>
    /// Gets the leased value as a <see cref="SimpleString"/>.
    /// </summary>
    public static implicit operator SimpleString(LeasedString value) => value.Value;

    /// <summary>
    /// Create a new leased buffer of the requested length.
    /// </summary>
    public LeasedString(int length, out Memory<byte> memory)
    {
        _lease = new Lease<byte>(length);
        memory = _lease.Memory;
    }

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose() => _lease.Dispose();
}
