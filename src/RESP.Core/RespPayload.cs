using System;
using System.Buffers;

namespace Resp;

public sealed class RespPayload : IDisposable
{
    public int Count { get; }
    private ReadOnlySequence<byte> _payload;
    private Action<ReadOnlySequence<byte>>? _onDispose;
    private bool _isDisposed;

    public ReadOnlySequence<byte> Payload
    {
        get
        {
            ThrowIfDisposed();
            return _payload;
        }
    }

    /// <inheritdoc/>
    public override int GetHashCode() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override string ToString() => _isDisposed ? "(disposed)" : $"{_payload} bytes, {Count} message(s)";

    private void ThrowIfDisposed()
    {
        if (_isDisposed) Throw();
        static void Throw() => throw new ObjectDisposedException(nameof(RespPayload));
    }

    /// <summary>
    /// Create a new instance using the supplied payload, optionally specifying a custom dispose action.
    /// </summary>
    public RespPayload(ReadOnlySequence<byte> payload, int count = 1, Action<ReadOnlySequence<byte>>? onDispose = null)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        _payload = payload;
        _onDispose = onDispose;
        Count = count;
    }

    /// <summary>
    /// Ensure that this is a valid RESP payload and contains the expected number of top-level elements.
    /// </summary>
    /// <param name="checkError">Whether to check for error replies.</param>
    public void Validate(bool checkError = true)
    {
        ThrowIfDisposed();
        RespReader reader = new(in _payload);
        int count = 0;
        while (reader.TryMoveNext(checkError))
        {
            reader.SkipChildren();
            count++;
        }

        if (count != Count)
            throw new InvalidOperationException($"{nameof(Count)} mismatch: expected {Count}, found {count}");
    }

    /// <inheritdoc cref="IDisposable.Dispose" />
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        var onDispose = _onDispose;
        var payload = _payload;
        _onDispose = null;
        _payload = default;
        onDispose?.Invoke(payload);
    }
}
