using System;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RESPite.Internal;

namespace RESPite.Resp;

/// <summary>
/// Represents a RESP key as either <see cref="byte"/> or <see cref="char"/> based data.
/// </summary>
public readonly struct SimpleString
{
    private readonly object? _obj1, _obj2;
    private readonly int _start, _length;

    /// <summary>
    /// Gets whether this is a single-segment value.
    /// </summary>
    public bool IsSingleSegment => _obj2 is null;

    /// <inheritdoc />
    public override string ToString()
    {
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

    /// <inheritdoc />
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() => throw new NotSupportedException();

    /// <inheritdoc />
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals(object? obj) => throw new NotSupportedException();

    /// <summary>
    /// Indicates whehter this an empty key.
    /// </summary>
    public bool IsEmpty => _length == 0 & _obj2 is null;

    /// <summary>
    /// Indicates whehter this a null key.
    /// </summary>
    public bool IsNull => _obj1 is null;

    /// <summary>
    /// Attempt to get the contents of the key.
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
    /// Attempt to get the contents of the key.
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
    /// Attempt to get the contents of the key.
    /// </summary>
    public bool TryGetBytes(out ReadOnlySequence<byte> sequence)
    {
        if (_obj1 is ReadOnlySequenceSegment<byte> start && _obj2 is ReadOnlySequenceSegment<byte> end)
        {
            sequence = new(start, _start, end, _length);
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
    /// Attempt to get the contents of the key.
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
    /// Attempt to get the contents of the key.
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
    /// Attempt to get the contents of the key.
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
            if (offset < 0 || offset >= value.Length) ThrowArgumentOutOfRange(nameof(offset));
            if (count < 0 || offset + count >= value.Length) ThrowArgumentOutOfRange(nameof(count));
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
            if (offset < 0 || offset >= value.Length) ThrowArgumentOutOfRange(nameof(offset));
            if (count < 0 || offset + count >= value.Length) ThrowArgumentOutOfRange(nameof(count));
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
    private static void ThrowArgumentOutOfRange(string parameterName) => throw new ArgumentOutOfRangeException(parameterName);
}
