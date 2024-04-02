//using RESPite.Internal;
//using System;
//using System.Buffers;
//using System.Diagnostics;
//using System.Runtime.CompilerServices;
//using System.Runtime.InteropServices;

//namespace RESPite;

///// <summary>
///// Represents a RESP message that has been prepared for transmission
///// </summary>
//public readonly struct RequestBuffer
//{
//    private readonly ReadOnlySequence<byte> _buffer;
//    private readonly int _preambleIndex, _payloadIndex;

//    /// <summary>
//    /// Indicates the length of the current payload
//    /// </summary>
//    public long Length => _buffer.Length - _preambleIndex;

//    private RequestBuffer(in ReadOnlySequence<byte> buffer, int preambleIndex, int payloadIndex)
//    {
//        _buffer = buffer;
//        _preambleIndex = preambleIndex;
//        _payloadIndex = payloadIndex;
//    }

//    internal RequestBuffer(in ReadOnlySequence<byte> buffer, int payloadIndex)
//    {
//        _buffer = buffer;
//        _preambleIndex = _payloadIndex = payloadIndex;
//    }

//    public bool TryGetSpan(out ReadOnlySpan<byte> span)
//    {
//        var buffer = GetBuffer(); // handle preamble
//        if (buffer.IsSingleSegment)
//        {
//#if NETCOREAPP3_1_OR_GREATER
//            span = buffer.FirstSpan;
//#else
//            span = buffer.First.Span;
//#endif
//            return true;
//        }
//        span = default;
//        return false;
//    }

//    /// <summary>
//    /// Gets the buffer representing the payload for this content
//    /// </summary>
//    public ReadOnlySequence<byte> GetBuffer() => _preambleIndex == 0 ? _buffer : _buffer.Slice(_preambleIndex);

//    /// <summary>
//    /// Gets a text (UTF8) representation of the RESP payload; this API is intended for debugging purposes only, and may
//    /// be misleading for non-UTF8 payloads.
//    /// </summary>
//    public override string ToString()
//    {
//        var length = Length;
//        if (length == 0) return "";
//        if (length > 1024) return $"({length} bytes)";
//        var buffer = GetBuffer();
//#if NET6_0_OR_GREATER
//        return RespWriter.UTF8.GetString(buffer);
//#else
//#if NETCOREAPP3_0_OR_GREATER
//        if (buffer.IsSingleSegment)
//        {
//            return RespWriter.UTF8.GetString(buffer.FirstSpan);
//        }
//#endif
//        var arr = ArrayPool<byte>.Shared.Rent((int)length);
//        buffer.CopyTo(arr);
//        var s = RespWriter.UTF8.GetString(arr, 0, (int)length);
//        ArrayPool<byte>.Shared.Return(arr);
//        return s;
//#endif
//    }

//    /// <summary>
//    /// Releases all buffers associated with this instance.
//    /// </summary>
//    public void Recycle()
//    {
//        var buffer = _buffer;
//        // nuke self (best effort to prevent multi-release)
//        Unsafe.AsRef(in this) = default;
//        new LeasedSequence<byte>(buffer).Dispose();
//    }

//    /// <summary>
//    /// Prepends the given preamble contents 
//    /// </summary>
//    public RequestBuffer WithPreamble(ReadOnlySpan<byte> value)
//    {
//        if (value.IsEmpty) return this; // trivial

//        int length = value.Length, preambleIndex = _preambleIndex - length;
//        if (preambleIndex < 0) Throw();
//        var target = _buffer.Slice(preambleIndex, length);
//        if (target.IsSingleSegment)
//        {
//            value.CopyTo(MemoryMarshal.AsMemory(target.First).Span);
//        }
//        else
//        {
//            MultiCopy(in target, value);
//        }
//        return new(_buffer, preambleIndex, _payloadIndex);

//        static void Throw() => throw new InvalidOperationException("There is insufficient capacity to add the requested preamble");

//        static void MultiCopy(in ReadOnlySequence<byte> buffer, ReadOnlySpan<byte> source)
//        {
//            // note that we've already asserted that the source is non-trivial
//            var iter = buffer.GetEnumerator();
//            while (iter.MoveNext())
//            {
//                var target = MemoryMarshal.AsMemory(iter.Current).Span;
//                if (source.Length <= target.Length)
//                {
//                    source.CopyTo(target);
//                    return;
//                }
//                source.Slice(0, target.Length).CopyTo(target);
//                source = source.Slice(target.Length);
//                Debug.Assert(!source.IsEmpty);
//            }
//            Debug.Assert(!source.IsEmpty);
//            Throw();
//            static void Throw() => throw new InvalidOperationException("Insufficient target space");
//        }
//    }

//    /// <summary>
//    /// Removes all preamble, reverting to just the original payload
//    /// </summary>
//    public RequestBuffer WithoutPreamble() => new RequestBuffer(_buffer, _payloadIndex, _payloadIndex);
//    internal string GetCommand()
//    {
//        var buffer = WithoutPreamble().GetBuffer();
//        var reader = new RespReader(in buffer);
//        if (reader.TryReadNext() && reader.Prefix == RespPrefix.Array && reader.ChildCount > 0
//            && reader.TryReadNext() && reader.Prefix == RespPrefix.BulkString)
//        {
//            return reader.ReadString() ?? "";
//        }
//        else
//        {
//            return "(unexpected RESP)";
//        }
//    }

//    [Conditional("DEBUG")]
//    internal void DebugValidateGenericFragment()
//    {
//#if DEBUG
//        var buffer = WithoutPreamble().GetBuffer();
//        Debug.Assert(!buffer.IsEmpty, "buffer should not be empty");
//        var reader = new RespReader(in buffer);
//        int remaining = 1;
//        while (remaining > 0)
//        {
//            if (!reader.TryReadNext()) RespReader.ThrowEOF();
//            remaining = remaining - 1 + reader.ChildCount;
//        }
//        Debug.Assert(remaining == 0, "should have zero outstanding RESP fragments");
//        Debug.Assert(reader.BytesConsumed == buffer.Length, "should be fully consumed");
//#endif
//    }

//    [Conditional("DEBUG")]
//    internal void DebugValidateCommand()
//    {
//#if DEBUG
//        var buffer = WithoutPreamble().GetBuffer();
//        Debug.Assert(!buffer.IsEmpty, "buffer should not be empty");
//        var reader = new RespReader(in buffer);
//        if (!reader.TryReadNext()) RespReader.ThrowEOF();
//        Debug.Assert(reader.Prefix == RespPrefix.Array, "root must be an array");
//        Debug.Assert(reader.ChildCount > 0, "must have at least one element");
//        int count = reader.ChildCount;
//        for (int i = 0; i < count; i++)
//        {
//            if (!reader.TryReadNext()) RespReader.ThrowEOF();
//            Debug.Assert(reader.Prefix == RespPrefix.BulkString, "all parameters must be bulk strings");
//        }
//        Debug.Assert(!reader.TryReadNext(), "should be nothing left");
//        Debug.Assert(reader.BytesConsumed == buffer.Length, "should be fully consumed");
//#endif
//    }
//}
