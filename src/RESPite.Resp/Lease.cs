using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RESPite.Resp;

/// <summary>
/// Provides safe leased memory of type <typeparamref name="T"/>.
/// </summary>
public readonly struct Lease<T> : IDisposable
{
    private readonly LeaseCore? _lease;

    /// <summary>
    /// Create a new <see cref="Lease{T}"/> value with the specified capacity.
    /// </summary>
    public Lease(int length) => _lease = length == 0 ? LeaseCore.Empty : new LeaseCore(length);

    // trusted .ctor using existing array
    internal Lease(T[] value, int count)
    {
        if (count == 0)
        {
            ArrayPool<T>.Shared.Return(value);
            _lease = LeaseCore.Empty;
        }
        else
        {
            _lease = new LeaseCore(value, count);
        }
    }

    /// <summary>
    /// An empty instance.
    /// </summary>
    public static Lease<T> Empty { get; } = new(0);

    /// <summary>
    /// Gets the memory associated with this lease.
    /// </summary>
    public Memory<T> Memory => _lease is null ? default : _lease.Memory;

    /// <summary>
    /// Gets the number of elements in this lease.
    /// </summary>
    public int Length => _lease == null ? 0 : _lease.Length;

    /// <summary>
    /// Indicates whether this is a null lease.
    /// </summary>
    public bool IsNull => _lease is null;

    /// <summary>
    /// Indicates whether this is a zero-length lease.
    /// </summary>
    public bool IsEmpty => Length == 0;

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose() => _lease?.Dispose();

    private sealed class LeaseCore : MemoryManager<T>
    {
        public LeaseCore(int length)
        {
            _arr = ArrayPool<T>.Shared.Rent(length);
            Length = length;
        }

        // trusted existing data .ctor
        internal LeaseCore(T[] arr, int length)
        {
            _arr = arr;
            Length = length;
        }

        internal static readonly LeaseCore Empty = new(0);

        private T[]? _arr;
        public readonly int Length;

        private uint _pinCount;
        private GCHandle _pin;

        public void Dispose() => ((IDisposable)this).Dispose();

        public override Span<T> GetSpan()
        {
            return _arr is { } arr ? new(arr, 0, Length) : ThrowDisposed();

            [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
            static Span<T> ThrowDisposed()
            {
                ThrowDisposed();
                return default;
            }
        }

        public override Memory<T> Memory
        {
            get
            {
                if (_arr is null) ThrowDisposed();
                return CreateMemory(Length);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining), DoesNotReturn]
        private static void ThrowDisposed() => throw new ObjectDisposedException(nameof(LeasedString));

        protected override void Dispose(bool disposing)
        {
            if (disposing && Length != 0)
            {
                var arr = _arr;
                _arr = null;
                if (arr is not null) ArrayPool<T>.Shared.Return(arr);
            }
        }

        protected override bool TryGetArray(out ArraySegment<T> segment)
        {
            if (_arr is { } arr)
            {
                segment = new(arr, 0, arr.Length);
                return true;
            }
            return base.TryGetArray(out segment);
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            if (elementIndex < 0 || elementIndex >= Length) throw new ArgumentOutOfRangeException(nameof(elementIndex));
            if (_arr is null) ThrowDisposed();

            lock (this)
            {
                if (_pinCount == 0)
                {
                    _pin = GCHandle.Alloc(_arr, GCHandleType.Pinned);
                }
                checked
                {
                    _pinCount++;
                }

                unsafe
                {
                    var addr = _pin.AddrOfPinnedObject();
                    if (elementIndex != 0)
                    {
                        addr = IntPtr.Add(addr, checked(elementIndex * Unsafe.SizeOf<T>()));
                    }
                    return new(addr.ToPointer(), _pin, this);
                }
            }
        }

        public override void Unpin()
        {
            lock (this)
            {
                switch (_pinCount)
                {
                    case 0:
                        throw new InvalidOperationException("Cannot unpin an object that is not pinned.");
                    case 1:
                        _pin.Free();
                        _pin = default;
                        break;
                }
                checked
                {
                    _pinCount--;
                }
            }
        }
    }
}
