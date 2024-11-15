using System;
using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RESPite.Internal;

namespace RESPite.Resp;

/// <summary>
/// Represents an opaque string as either <see cref="byte"/> or <see cref="char"/> based data.
/// </summary>
public readonly struct SimpleString
{
    /// <inheritdoc />
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals(object? obj) => throw new NotSupportedException();

    /// <inheritdoc />
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() => throw new NotSupportedException();

    private readonly object? _obj1, _obj2;
    private readonly int _start, _length;

    /// <summary>
    /// Gets whether this is a single-segment value.
    /// </summary>
    public bool IsSingleSegment => _obj2 is null;

    /// <summary>
    /// Gets whether this is a string of <see cref="byte"/> based data.
    /// </summary>
    public bool IsBytes => _obj1 is byte[] or MemoryManager<byte> or ReadOnlySequenceSegment<byte>;

    /// <summary>
    /// Gets whether this is a string of <see cref="char"/> based data.
    /// </summary>
    public bool IsChars => _obj1 is string or char[] or MemoryManager<char> or ReadOnlySequenceSegment<char>;

    /// <inheritdoc />
    public override string? ToString()
    {
        if (IsNull) return null;
        if (IsEmpty) return "";
        if (IsSingleSegment)
        {
            if (_obj1 is string s && s.Length == _length) return s;

            if (TryGetChars(span: out var chars))
            {
#if NETCOREAPP3_1
                return new string(chars);
#else
                unsafe
                {
                    fixed (char* ptr = chars)
                    {
                        return new string(ptr, _start, _length);
                    }
                }
#endif
            }
            if (TryGetBytes(span: out var bytes))
            {
                return Constants.UTF8.GetString(bytes);
            }
            if (TryGetChars(sequence: out var charsSeq))
            {
                return Join(charsSeq);
            }
            if (TryGetBytes(sequence: out var bytesSeq))
            {
                return Constants.UTF8.GetString(bytesSeq);
            }
        }

        return "(???)";

        static string Join(in ReadOnlySequence<char> value)
        {
            if (value.IsEmpty) return "";
            int len = checked((int)value.Length);
            var lease = ArrayPool<char>.Shared.Rent(len);
            value.CopyTo(lease);
            var s = new string(lease, 0, len);
            ArrayPool<char>.Shared.Return(lease);
            return s;
        }
    }

    /// <summary>
    /// Indicates whehter this an empty string.
    /// </summary>
    public bool IsEmpty => _length == 0 & _obj2 is null;

    /// <summary>
    /// Indicates whether this a null string.
    /// </summary>
    public bool IsNull => _obj1 is null;

    /// <summary>
    /// Attempt to get the contents of the string.
    /// </summary>
    public bool TryGetBytes(out ReadOnlySpan<byte> span)
    {
        if (IsSingleSegment)
        {
            if (_obj1 is byte[] arr)
            {
                span = new(arr, _start, _length);
                return true;
            }

            if (_obj1 is MemoryManager<byte> mgr)
            {
                span = mgr.GetSpan().Slice(_start, _length);
                return true;
            }
        }

        span = default;
        return IsEmpty;
    }

    /// <summary>
    /// Attempt to get the contents of the string.
    /// </summary>
    public bool TryGetBytes(out ReadOnlyMemory<byte> memory)
    {
        if (IsSingleSegment)
        {
            if (_obj1 is byte[] arr)
            {
                memory = new(arr, _start, _length);
                return true;
            }

            if (_obj1 is MemoryManager<byte> mgr)
            {
                memory = mgr.Memory.Slice(_start, _length);
                return true;
            }
        }

        memory = default;
        return IsEmpty;
    }

    /// <summary>
    /// Attempt to get the contents of the string.
    /// </summary>
    public bool TryGetBytes(out ReadOnlySequence<byte> sequence)
    {
        if (_obj1 is ReadOnlySequenceSegment<byte> start)
        {
            sequence = new(start, _start, (ReadOnlySequenceSegment<byte>)_obj2!, _length);
            return true;
        }
        if (IsEmpty)
        {
            sequence = default;
            return true;
        }
        if (IsSingleSegment && TryGetBytes(out ReadOnlyMemory<byte> mem))
        {
            sequence = new(mem);
            return true;
        }
        sequence = default;
        return false;
    }

    /// <summary>
    /// Attempt to get the contents of the string.
    /// </summary>
    public bool TryGetChars(out ReadOnlySpan<char> span)
    {
        if (IsSingleSegment)
        {
            if (_obj1 is string s)
            {
                span = s.AsSpan(_start, _length);
                return true;
            }

            if (_obj1 is char[] arr)
            {
                span = new(arr, _start, _length);
                return true;
            }

            if (_obj1 is MemoryManager<char> mgr)
            {
                span = mgr.GetSpan().Slice(_start, _length);
                return true;
            }
        }

        span = default;
        return IsEmpty;
    }

    /// <summary>
    /// Attempt to get the contents of the string.
    /// </summary>
    public bool TryGetChars(out ReadOnlyMemory<char> memory)
    {
        if (IsSingleSegment)
        {
            if (_obj1 is string s)
            {
                memory = s.AsMemory(_start, _length);
                return true;
            }

            if (_obj1 is char[] arr)
            {
                memory = new(arr, _start, _length);
                return true;
            }

            if (_obj1 is MemoryManager<char> mgr)
            {
                memory = mgr.Memory.Slice(_start, _length);
                return true;
            }
        }

        memory = default;
        return IsEmpty;
    }

    /// <summary>
    /// Attempt to get the contents of the string.
    /// </summary>
    public bool TryGetChars(out ReadOnlySequence<char> sequence)
    {
        if (_obj1 is ReadOnlySequenceSegment<char> start && _obj2 is ReadOnlySequenceSegment<char> end)
        {
            sequence = new(start, _start, end, _length);
            return true;
        }
        if (IsEmpty)
        {
            sequence = default;
            return true;
        }
        if (IsSingleSegment && TryGetChars(out ReadOnlyMemory<char> mem))
        {
            sequence = new(mem);
            return true;
        }
        sequence = default;
        return false;
    }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public static implicit operator SimpleString(in ReadOnlySequence<byte> value) => new(in value);

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public static implicit operator SimpleString(in ReadOnlySequence<char> value) => new(in value);

    private static readonly SimpleString _empty = new(0);

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public static implicit operator SimpleString(string value) => new(value);

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public static implicit operator SimpleString(byte[] value) => new(value);

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public static implicit operator SimpleString(char[] value) => new(value);

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public static implicit operator SimpleString(ArraySegment<byte> value) => new(value);

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public static implicit operator SimpleString(ArraySegment<char> value) => new(value);

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public static implicit operator SimpleString(ReadOnlyMemory<byte> value) => new(value);

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public static implicit operator SimpleString(ReadOnlyMemory<char> value) => new(value);

    /// <summary>
    /// An empty <see cref="SimpleString"/>.
    /// </summary>
    public static ref readonly SimpleString Empty => ref _empty;

    private SimpleString(int dummy) // used to create the empty value
    {
        _start = _length = 0;
        _obj1 = Array.Empty<byte>();
        _obj2 = null;
    }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public SimpleString(string value)
    {
        if (value is null)
        {
            this = default;
        }
        else if (value.Length == 0)
        {
            this = _empty;
        }
        else
        {
            _obj1 = value;
            _obj2 = null;
            _start = 0;
            _length = value.Length;
        }
    }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public SimpleString(byte[] value)
    {
        if (value is null)
        {
            this = default;
        }
        else if ((_length = value.Length) == 0)
        {
            this = _empty;
        }
        else
        {
            _obj1 = value;
            _obj2 = null;
            _start = 0;
        }
    }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public SimpleString(char[] value)
    {
        if (value is null)
        {
            this = default;
        }
        else if ((_length = value.Length) == 0)
        {
            this = _empty;
        }
        else
        {
            _obj1 = value;
            _obj2 = null;
            _start = 0;
        }
    }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public SimpleString(byte[] value, int offset, int count)
    {
        if (value is null)
        {
            this = default;
        }
        else if (count == 0)
        {
            this = _empty;
        }
        else
        {
            _obj1 = value;
            _obj2 = null;
            _start = offset;
            _length = count;
            if (offset < 0 || offset >= value.Length) ThrowOffset();
            if (count < 0 || offset + count >= value.Length) ThrowCount();
        }
    }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public SimpleString(char[] value, int offset, int count)
    {
        if (value is null)
        {
            this = default;
        }
        else if (count == 0)
        {
            this = _empty;
        }
        else
        {
            _obj1 = value;
            _obj2 = null;
            _start = offset;
            _length = count;
            if (offset < 0 || offset >= value.Length) ThrowOffset();
            if (count < 0 || offset + count >= value.Length) ThrowCount();
        }
    }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public SimpleString(in ReadOnlySequence<byte> value)
    {
        if (value.IsEmpty)
        {
            this = _empty;
        }
        else if (value.IsSingleSegment)
        {
            this = new(value.First);
        }
        else
        {
            var pos = value.Start;
            _obj1 = pos.GetObject();
            _start = pos.GetInteger();

            pos = value.End;
            _obj2 = pos.GetObject();
            _length = pos.GetInteger();
        }
    }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public SimpleString(in ReadOnlySequence<char> value)
    {
        if (value.IsEmpty)
        {
            this = _empty;
        }
        else if (value.IsSingleSegment)
        {
            this = new(value.First);
        }
        else
        {
            var pos = value.Start;
            _obj1 = pos.GetObject();
            _start = pos.GetInteger();

            pos = value.End;
            _obj2 = pos.GetObject();
            _length = pos.GetInteger();
        }
    }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public SimpleString(ArraySegment<byte> value) : this(value.Array!, value.Offset, value.Count) { }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public SimpleString(ArraySegment<char> value) : this(value.Array!, value.Offset, value.Count) { }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public SimpleString(ReadOnlyMemory<byte> value)
    {
        if (value.IsEmpty)
        {
            this = _empty;
        }
        else if (MemoryMarshal.TryGetArray(value, out var segment))
        {
            _obj1 = segment.Array!;
            _obj2 = null;
            _start = segment.Offset;
            _length = segment.Count;
        }
        else if (MemoryMarshal.TryGetMemoryManager(value, out MemoryManager<byte>? mgr, out _start, out _length))
        {
            _obj1 = mgr!;
            _obj2 = null;
        }
        else
        {
            this = ThrowMemoryKind();
        }
    }

    internal SimpleString(MemoryManager<byte>? manager, int start, int length)
    {
        if (manager is null)
        {
            this = _empty;
        }
        else
        {
            _obj1 = manager;
            _obj2 = null;
            _start = start;
            _length = length;
        }
    }

    /// <summary>
    /// Create a new <see cref="SimpleString"/> from the provided <paramref name="value"/>.
    /// </summary>
    public SimpleString(ReadOnlyMemory<char> value)
    {
        if (value.IsEmpty)
        {
            this = _empty;
        }
        else if (MemoryMarshal.TryGetString(value, out var s, out _start, out _length))
        {
            _obj1 = s;
            _obj2 = null;
        }
        else if (MemoryMarshal.TryGetArray(value, out var segment))
        {
            _obj1 = segment.Array!;
            _obj2 = null;
            _start = segment.Offset;
            _length = segment.Count;
        }
        else if (MemoryMarshal.TryGetMemoryManager(value, out MemoryManager<char>? mgr, out _start, out _length))
        {
            _obj1 = mgr!;
            _obj2 = null;
        }
        else
        {
            this = ThrowMemoryKind();
        }
    }

    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static SimpleString ThrowMemoryKind() => throw new ArgumentException("Unexpected memory kind", "value");

    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOffset() => throw new ArgumentOutOfRangeException("offset");

    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowCount() => throw new ArgumentOutOfRangeException("count");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySequence<byte> GetByteSequencePrechecked()
    {
        Debug.Assert(_obj1 is ReadOnlySequenceSegment<byte>, $"{nameof(_obj1)} should have been prechecked");
        Debug.Assert(_obj2 is ReadOnlySequenceSegment<byte>, $"{nameof(_obj2)} should be a ROSS-byte");

        ReadOnlySequence<byte> value = new(Unsafe.As<ReadOnlySequenceSegment<byte>>(_obj1!), _start, Unsafe.As<ReadOnlySequenceSegment<byte>>(_obj2!), _length);
        Debug.Assert(!(value.IsEmpty || value.IsSingleSegment), "should already have excluded trivial sequences during .ctor");
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySequence<char> GetCharSequencePrechecked()
    {
        Debug.Assert(_obj1 is ReadOnlySequenceSegment<char>, $"{nameof(_obj1)} should have been prechecked");
        Debug.Assert(_obj2 is ReadOnlySequenceSegment<char>, $"{nameof(_obj2)} should be a ROSS-char");

        ReadOnlySequence<char> value = new(Unsafe.As<ReadOnlySequenceSegment<char>>(_obj1!), _start, Unsafe.As<ReadOnlySequenceSegment<char>>(_obj2!), _length);
        Debug.Assert(!(value.IsEmpty || value.IsSingleSegment), "should already have excluded trivial sequences during .ctor");
        return value;
    }

    /// <summary>
    /// Gets the number of bytes represented by this content; for <see cref="byte"/> based data, this is direct; for <see cref="char"/> based data,
    /// this is calculated using <see cref="UTF8Encoding"/>.
    /// </summary>
    public int GetByteCount() => _obj1 switch
    {
        null => 0,
        byte[] or MemoryManager<byte> => _length,
        ReadOnlySequenceSegment<byte> bseg => checked((int)GetByteSequencePrechecked().Length),
        string s => Constants.UTF8.GetByteCount(s.AsSpan(_start, _length)),
        char[] c => Constants.UTF8.GetByteCount(c, _start, _length),
        MemoryManager<char> m => Constants.UTF8.GetByteCount(m.GetSpan().Slice(_start, _length)),
        ReadOnlySequenceSegment<char> cseg => SlowGetByteCountFromCharSequencePrechecked(),
        _ => ThrowInvalidContent(),
    };

    /// <summary>
    /// Write this value to the provided <see cref="Span{Byte}"/>.
    /// </summary>
    public int CopyTo(Span<byte> destination)
    {
        if (TryGetBytes(span: out var bytes))
        {
            bytes.CopyTo(destination);
            return bytes.Length;
        }
        else if (_obj1 is ReadOnlySequenceSegment<byte>)
        {
            var seq = GetByteSequencePrechecked();
            seq.CopyTo(destination);
            return (int)seq.Length;
        }
        else if (TryGetChars(span: out var chars))
        {
            return Constants.UTF8.GetBytes(chars, destination);
        }
        else if (_obj1 is ReadOnlySequenceSegment<char>)
        {
            return SlowCopyToFromCharSequencePrechecked(destination);
        }
        else
        {
            ThrowInvalidContent();
            return 0;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int SlowGetByteCountFromCharSequencePrechecked()
    {
        var value = GetCharSequencePrechecked();
        int tally = 0;
        foreach (var chunk in value)
        {
            var chunkSize = Constants.UTF8.GetByteCount(chunk.Span);
            checked
            {
                tally += chunkSize;
            }
        }
        return tally;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int SlowCopyToFromCharSequencePrechecked(Span<byte> destination)
    {
        var value = GetCharSequencePrechecked();
        int tally = 0;
        foreach (var chunk in value)
        {
            var chunkSize = Constants.UTF8.GetBytes(chunk.Span, destination);
            checked
            {
                tally += chunkSize;
            }
            destination = destination.Slice(chunkSize);
        }
        return tally;
    }

    internal int ReadLitteEndianInt64(int offset)
    {
        switch (_obj1)
        {
            case byte[] arr:
                if (offset + sizeof(int) > _length) ThrowOffset();
                return BinaryPrimitives.ReadInt32LittleEndian(new(arr, _start + offset, sizeof(int)));
            case MemoryManager<byte> mgr:
                if (offset + sizeof(int) > _length) ThrowOffset();
                return BinaryPrimitives.ReadInt32LittleEndian(mgr.GetSpan().Slice(_start + offset, sizeof(int)));
            case ReadOnlySequenceSegment<byte> seg:
                return SlowReadLitteEndianInt32Prechecked(offset);
            default:
                return ThrowInvalidContent();
        }
    }

    private int SlowReadLitteEndianInt32Prechecked(int offset)
    {
        var value = GetByteSequencePrechecked();
        Span<byte> scratch = stackalloc byte[sizeof(int)];
        value.Slice(offset, sizeof(int)).CopyTo(scratch);
        return BinaryPrimitives.ReadInt32LittleEndian(scratch);
    }

    [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
    private int ThrowInvalidContent([CallerMemberName] string operation = "")
        => throw new InvalidOperationException($"Invalid content for {operation}: {_obj1?.GetType().Name ?? "null"}");

    /// <summary>
    /// Get a substring of a <see cref="byte"/> based payload.
    /// </summary>
    public SimpleString SliceBytes(int offset, int count) => new(in this, offset, count);

    /// <summary>
    /// Performs a slice on *exclusively* byte data.
    /// </summary>
    private SimpleString(in SimpleString source, int offset, int count, [CallerMemberName] string caller = "")
    {
        if (source.IsChars) ThrowInvalidContent(caller);
        if (offset < 0) ThrowOffset();
        if (count < 0) ThrowCount();
        var bytes = source.GetByteCount();
        if (offset + count > bytes) ThrowCount();
        if (count == 0)
        {
            this = source.IsNull ? default : Empty;
            return;
        }

        if (source._obj1 is byte[] or MemoryManager<byte>)
        {
            this = source;
            _start += offset;
            _length = count;
            return;
        }

        if (source._obj1 is ReadOnlySequenceSegment<byte>)
        {
            this = new(GetByteSequencePrechecked().Slice(offset, count));
        }

        ThrowInvalidContent(caller);
        this = default; // not reached
    }
}
