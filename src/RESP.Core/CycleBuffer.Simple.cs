/*
using System;
using System.Buffers;
using System.Diagnostics;

#pragma warning disable SA1205 // accessibility on partial - for debugging/test practicality

partial struct CycleBuffer // basic impl for debugging / validation; just uses single-buffer pack-down
{
    private byte[] _buffer;
    private int _committed;

    public void DiscardCommitted(long fullyConsumed)
        => DiscardCommitted(checked((int)fullyConsumed));

    public void DiscardCommitted(int fullyConsumed)
    {
        Debug.Assert(fullyConsumed >= 0 & fullyConsumed <= _committed);
        var remaining = _committed - fullyConsumed;
        if (remaining != 0)
        {
            var buffer = _buffer;
            buffer.AsSpan(fullyConsumed, remaining).CopyTo(buffer);
        }

        _committed -= fullyConsumed;
    }

    public ReadOnlySequence<byte> GetAllCommitted()
        => new(_buffer, 0, _committed);

    public bool TryGetCommitted(out ReadOnlySpan<byte> span)
    {
        span = _buffer.AsSpan(0, _committed);
        return true;
    }

    public void Release()
    {
        var buffer = _buffer;
        _committed = 0;
        _buffer = [];
        ArrayPool<byte>.Shared.Return(buffer);
    }

    public int PageSize { get; }

    private CycleBuffer(int pageSize)
    {
        _buffer = [];
        PageSize = pageSize;
    }

    public static CycleBuffer Create(MemoryPool<byte>? pool = null, int pageSize = 0)
    {
        _ = pool;
        return new(Math.Max(pageSize, 1024));
    }

    public void Commit(int bytesRead)
    {
        Debug.Assert(bytesRead >= 0 & bytesRead <= UncommittedAvailable);
        _committed += bytesRead;
    }

    public Span<byte> GetUncommittedSpan(int hint = 1)
    {
        if (UncommittedAvailable < hint) Grow(hint);
        return _buffer.AsSpan(_committed);
    }
    public Memory<byte> GetUncommittedMemory(int hint = 1)
    {
        if (UncommittedAvailable < hint) Grow(hint);
        return _buffer.AsMemory(_committed);
    }

    private void Grow(int hint)
    {
        hint = Math.Max(hint, 128); // at least a reasonable size
        var newLength = Math.Max(_committed + hint, _committed * 2); // what we need, or double what we have; the larger

        var newBuffer = ArrayPool<byte>.Shared.Rent(newLength);
        var oldBuffer = _buffer;
        Debug.Assert(newBuffer.Length > oldBuffer.Length, " should have increased");
        oldBuffer.AsSpan(0, _committed).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(oldBuffer);
        _buffer = newBuffer;
    }

    public int UncommittedAvailable => _buffer.Length - _committed;
    public bool CommittedIsEmpty => _committed == 0;

    public int GetCommittedLength() => _committed;

    public bool TryGetFirstCommittedSpan(bool fullOnly, out ReadOnlySpan<byte> span)
    {
        var buffer = _buffer;
        if (fullOnly)
        {
            if (_committed >= PageSize)
            {
                span = buffer.AsSpan(0, _committed);
                return true;
            }
            // offer up a reasonable page size
            span = default;
            return false;
        }

        span = buffer.AsSpan(0, _committed);
        return _committed != 0;
    }
}
*/
