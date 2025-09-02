using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RESPite.Internal;

internal abstract partial class BlockBufferSerializer
{
    internal sealed class BlockBuffer : MemoryManager<byte>
    {
        private BlockBuffer(BlockBufferSerializer parent, int minCapacity)
        {
            _arrayPool = parent._arrayPool;
            _array = _arrayPool.Rent(minCapacity);
            DebugCounters.OnBufferCapacity(_array.Length);
#if DEBUG
            _parent = parent;
            parent.DebugBufferCreated();
#endif
        }

        private int _refCount = 1;
        private int _finalizedOffset, _writeOffset;
        private readonly ArrayPool<byte> _arrayPool;
        private byte[] _array;
#if DEBUG
        private int _finalizedCount;
        private BlockBufferSerializer _parent;
#endif

        public override string ToString() =>
#if DEBUG
            $"{_finalizedCount} messages; " +
#endif
            $"{_finalizedOffset} finalized bytes; writing: {NonFinalizedData.Length} bytes, {Available} available; observers: {_refCount}";

        // only used when filling; _buffer should be non-null
        private int Available => _array.Length - _writeOffset;
        public Memory<byte> UncommittedMemory => _array.AsMemory(_writeOffset);
        public Span<byte> UncommittedSpan => _array.AsSpan(_writeOffset);

        // decrease ref-count; dispose if necessary
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) <= 0) Recycle();
        }

        public void AddRef()
        {
            if (!TryAddRef()) Throw();
            static void Throw() => throw new ObjectDisposedException(nameof(BlockBuffer));
        }

        public bool TryAddRef()
        {
            int count;
            do
            {
                count = Volatile.Read(ref _refCount);
                if (count <= 0) return false;
            }
            // repeat until we can successfully swap/incr
            while (Interlocked.CompareExchange(ref _refCount, count + 1, count) != count);

            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)] // called rarely vs Dispose
        private void Recycle()
        {
            var count = Volatile.Read(ref _refCount);
            if (count == 0)
            {
                _array.DebugScramble();
#if DEBUG
                GC.SuppressFinalize(this); // only have a finalizer in debug
                _parent.DebugBufferRecycled(_array.Length);
#endif
                _arrayPool.Return(_array);
                _array = [];
            }

            Debug.Assert(count == 0, $"over-disposal? count={count}");
        }

#if DEBUG
#pragma warning disable CA2015 // Adding a finalizer to a type derived from MemoryManager<T> may permit memory to be freed while it is still in use by a Span<T>
        // (the above is fine because we don't actually release anything - just a counter)
        ~BlockBuffer()
        {
            _parent.DebugBufferLeaked();
            DebugCounters.OnBufferLeaked();
        }
#pragma warning restore CA2015
#endif

        public static BlockBuffer GetBuffer(BlockBufferSerializer parent, int sizeHint)
        {
            // note this isn't an actual "max", just a max of what we guarantee; we give the caller
            // whatever is left in the buffer; the clamped hint just decides whether we need a *new* buffer
            const int MinSize = 16, MaxSize = 128;
            sizeHint = Math.Min(Math.Max(sizeHint, MinSize), MaxSize);

            var buffer = parent.Buffer; // most common path is "exists, with enough data"
            return buffer is not null && buffer.AvailableWithResetIfUseful() >= sizeHint
                ? buffer
                : GetBufferSlow(parent, sizeHint);
        }

        // would it be useful and possible to reset? i.e. if all finalized chunks have been returned,
        private int AvailableWithResetIfUseful()
        {
            if (_finalizedOffset != 0 // at least some chunks have been finalized
                && Volatile.Read(ref _refCount) == 1 // all finalized chunks returned
                & _writeOffset == _finalizedOffset) // we're not in the middle of serializing something new
            {
                _writeOffset = _finalizedOffset = 0; // swipe left
            }

            return _array.Length - _writeOffset;
        }

        private static BlockBuffer GetBufferSlow(BlockBufferSerializer parent, int minBytes)
        {
            // note clamp on size hint has already been applied
            const int DefaultBufferSize = 2048;
            var buffer = parent.Buffer;
            if (buffer is null)
            {
                // first buffer
                return parent.Buffer = new BlockBuffer(parent, DefaultBufferSize);
            }

            Debug.Assert(minBytes > buffer.Available, "existing buffer has capacity - why are we here?");

            if (buffer.TryResizeFor(minBytes))
            {
                Debug.Assert(buffer.Available >= minBytes);
                return buffer;
            }

            // We've tried reset and resize - no more tricks; we need to move to a new buffer, starting with a
            // capacity for any existing data in this message, plus the new chunk we're adding.
            var nonFinalizedBytes = buffer.NonFinalizedData;
            var newBuffer = new BlockBuffer(parent, Math.Max(nonFinalizedBytes.Length + minBytes, DefaultBufferSize));

            // copy the existing message data, if any (the previous message might have finished near the
            // boundary, in which case we might not have written anything yet)
            newBuffer.CopyFrom(nonFinalizedBytes);
            Debug.Assert(newBuffer.Available >= minBytes, "should have requested extra capacity");

            // the ~emperor~ buffer is dead; long live the ~emperor~ buffer
            parent.Buffer = newBuffer;
            buffer.MarkComplete(parent);
            return newBuffer;
        }

        // used for elective reset (rather than "because we ran out of space")
        public static void Clear(BlockBufferSerializer parent)
        {
            if (parent.Buffer is { } buffer)
            {
                parent.Buffer = null;
                buffer.MarkComplete(parent);
            }
        }

        public static ReadOnlyMemory<byte> RetainCurrent(BlockBufferSerializer parent)
        {
            if (parent.Buffer is { } buffer && buffer._finalizedOffset != 0)
            {
                parent.Buffer = null;
                buffer.AddRef();
                return buffer.CreateMemory(0, buffer._finalizedOffset);
            }
            // nothing useful to detach!
            return default;
        }

        private void MarkComplete(BlockBufferSerializer parent)
        {
            // record that the old buffer no longer logically has any non-committed bytes (mostly just for ToString())
            _writeOffset = _finalizedOffset;
            Debug.Assert(IsNonCommittedEmpty);

            // see if the caller wants to take ownership of the segment
            if (_finalizedOffset != 0 && !parent.ClaimSegment(CreateMemory(0, _finalizedOffset)))
            {
                Release(); // decrement the observer
            }
#if DEBUG
            DebugCounters.OnBufferCompleted(_finalizedCount, _finalizedOffset);
#endif
        }

        private void CopyFrom(Span<byte> source)
        {
            source.CopyTo(UncommittedSpan);
            _writeOffset += source.Length;
        }

        private Span<byte> NonFinalizedData => _array.AsSpan(
            _finalizedOffset, _writeOffset - _finalizedOffset);

        private bool TryResizeFor(int extraBytes)
        {
            if (_finalizedOffset == 0 & // we can only do this if there are no other messages in the buffer
                Volatile.Read(ref _refCount) == 1) // and no-one else is looking (we already tried reset)
            {
                // we're already on the boundary - don't scrimp; just do the math from the end of the buffer
                byte[] newArray = _arrayPool.Rent(_array.Length + extraBytes);
                DebugCounters.OnBufferCapacity(newArray.Length - _array.Length); // account for extra only

                // copy the existing data (we always expect some, since we've clamped extraBytes to be
                // much smaller than the default buffer size)
                NonFinalizedData.CopyTo(newArray);
                _array.DebugScramble();
                _arrayPool.Return(_array);
                _array = newArray;
                return true;
            }

            return false;
        }

        public static void Advance(BlockBufferSerializer parent, int count)
        {
            if (count == 0) return;
            if (count < 0) ThrowOutOfRange();
            var buffer = parent.Buffer;
            if (buffer is null || buffer.Available < count) ThrowOutOfRange();
            buffer._writeOffset += count;

            [DoesNotReturn]
            static void ThrowOutOfRange() => throw new ArgumentOutOfRangeException(nameof(count));
        }

        public void RevertUnfinalized(BlockBufferSerializer parent)
        {
            // undo any writes (something went wrong during serialize)
            _finalizedOffset = _writeOffset;
        }

        private ReadOnlyMemory<byte> FinalizeBlock()
        {
            var length = _writeOffset - _finalizedOffset;
            Debug.Assert(length > 0, "already checked this in FinalizeMessage!");
            var chunk = CreateMemory(_finalizedOffset, length);
            _finalizedOffset = _writeOffset; // move the write head
#if DEBUG
            _finalizedCount++;
            _parent.DebugMessageFinalized(length);
#endif
            Interlocked.Increment(ref _refCount); // add an observer
            return chunk;
        }

        private bool IsNonCommittedEmpty => _finalizedOffset == _writeOffset;

        public static ReadOnlyMemory<byte> FinalizeMessage(BlockBufferSerializer parent)
        {
            var buffer = parent.Buffer;
            if (buffer is null || buffer.IsNonCommittedEmpty)
            {
#if DEBUG // still count it for logging purposes
                if (buffer is not null) buffer._finalizedCount++;
                parent.DebugMessageFinalized(0);
#endif
                return default;
            }

            return buffer.FinalizeBlock();
        }

        // MemoryManager<byte> pieces
        protected override void Dispose(bool disposing)
        {
            if (disposing) Release();
        }

        public override Span<byte> GetSpan() => _array;
        public int Length => _array.Length;

        // base version is CreateMemory(GetSpan().Length); avoid that GetSpan()
        public override Memory<byte> Memory => CreateMemory(_array.Length);

        public override unsafe MemoryHandle Pin(int elementIndex = 0)
        {
            // We *could* be cute and use a shared pin - but that's a *lot*
            // of work (synchronization), requires extra storage, and for an
            // API that is very unlikely; hence: we'll use per-call GC pins.
            GCHandle handle = GCHandle.Alloc(_array, GCHandleType.Pinned);
            DebugCounters.OnBufferPinned(); // prove how unlikely this is
            byte* ptr = (byte*)handle.AddrOfPinnedObject();
            // note no IPinnable in the MemoryHandle;
            return new MemoryHandle(ptr + elementIndex, handle);
        }

        // This would only be called if we passed out a MemoryHandle with ourselves
        // as IPinnable (in Pin), which: we don't.
        public override void Unpin() => throw new NotSupportedException();

        protected override bool TryGetArray(out ArraySegment<byte> segment)
        {
            segment = new ArraySegment<byte>(_array);
            return true;
        }

        internal static void Release(in ReadOnlySequence<byte> request)
        {
            if (request.IsSingleSegment)
            {
                if (MemoryMarshal.TryGetMemoryManager<byte, BlockBuffer>(
                        request.First, out var block))
                {
                    block.Release();
                }
            }
            else
            {
                ReleaseMultiBlock(in request);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            static void ReleaseMultiBlock(in ReadOnlySequence<byte> request)
            {
                foreach (var segment in request)
                {
                    if (MemoryMarshal.TryGetMemoryManager<byte, BlockBuffer>(
                            segment, out var block))
                    {
                        block.Release();
                    }
                }
            }
        }
    }
}
