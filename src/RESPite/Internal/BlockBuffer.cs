using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RESPite.Internal;

internal abstract partial class BlockBufferSerializer
{
    private protected sealed class BlockBuffer : IDisposable
    {
        private BlockBuffer(BlockBufferSerializer parent, int minCapacity)
        {
            _arrayPool = parent._arrayPool;
            _buffer = _arrayPool.Rent(minCapacity);
            DebugCounters.OnBufferCapacity(_buffer.Length);
#if DEBUG
            _parent = parent;
            parent.DebugBufferCreated();
#endif
        }

        private int _refCount = 1;
        private int _finalizedOffset, _writeOffset;
        private readonly ArrayPool<byte> _arrayPool;
        private byte[] _buffer;
#if DEBUG
        private int _finalizedCount;
        private BlockBufferSerializer _parent;
#endif

        public override string ToString() =>
#if DEBUG
            $"{_finalizedCount} messages; " +
#endif
            $"{_finalizedOffset} finalized bytes; writing: {NonFinalizedData.Length} bytes, {Available} available; observers: {_refCount}";

        private int Available => _buffer.Length - _writeOffset;
        public Memory<byte> UncommittedMemory => _buffer.AsMemory(_writeOffset);
        public Span<byte> UncommittedSpan => _buffer.AsSpan(_writeOffset);

        // decrease ref-count; dispose if necessary
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (Interlocked.Decrement(ref _refCount) <= 0) Recycle();
        }

        [MethodImpl(MethodImplOptions.NoInlining)] // called rarely vs Dispose
        private void Recycle()
        {
            var count = Volatile.Read(ref _refCount);
            if (count == 0)
            {
                _arrayPool.Return(_buffer);
#if DEBUG
                GC.SuppressFinalize(this);
                _parent.DebugBufferRecycled();
#endif
            }

            Debug.Assert(count == 0, $"over-disposal? count={count}");
        }

#if DEBUG
        ~BlockBuffer()
        {
            _parent.DebugBufferLeaked();
        }
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

            return _buffer.Length - _writeOffset;
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
            buffer.MarkComplete();
            return newBuffer;
        }

        // used for elective reset (rather than "because we ran out of space")
        public static void Clear(BlockBufferSerializer parent)
        {
            if (parent.Buffer is { } buffer)
            {
                parent.Buffer = null;
                buffer.MarkComplete();
            }
        }

        private void MarkComplete()
        {
            // record that the old buffer no longer logically has any non-committed bytes (mostly just for ToString())
            _writeOffset = _finalizedOffset;
            Debug.Assert(IsNonCommittedEmpty);
            Dispose(); // decrement the observer
            #if DEBUG
            DebugCounters.OnBufferCompleted(_finalizedCount, _finalizedOffset);
            #endif
        }

        private void CopyFrom(Span<byte> source)
        {
            source.CopyTo(UncommittedSpan);
            _writeOffset += source.Length;
        }

        private Span<byte> NonFinalizedData => _buffer.AsSpan(
            _finalizedOffset, _writeOffset - _finalizedOffset);

        private bool TryResizeFor(int extraBytes)
        {
            if (_finalizedOffset == 0 & // we can only do this if there are no other messages in the buffer
                Volatile.Read(ref _refCount) == 1) // and no-one else is looking (we already tried reset)
            {
                // we're already on the boundary - don't scrimp; just do the math from the end of the buffer
                byte[] newArray = _arrayPool.Rent(_buffer.Length + extraBytes);
                DebugCounters.OnBufferCapacity(newArray.Length - _buffer.Length); // account for extra only

                // copy the existing data (we always expect some, since we've clamped extraBytes to be
                // much smaller than the default buffer size)
                NonFinalizedData.CopyTo(newArray);
                _arrayPool.Return(_buffer);
                _buffer = newArray;
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

        private ReadOnlyMemory<byte> Finalize(out IDisposable? block)
        {
            var length = _writeOffset - _finalizedOffset;
            Debug.Assert(length > 0, "already checked this in FinalizeMessage!");
            ReadOnlyMemory<byte> chunk = new(_buffer, _finalizedOffset, length);
            _finalizedOffset = _writeOffset; // move the write head
#if DEBUG
            _finalizedCount++;
            _parent.DebugMessageFinalized(length);
#endif
            Interlocked.Increment(ref _refCount); // add an observer
            block = this;
            return chunk;
        }

        private bool IsNonCommittedEmpty => _finalizedOffset == _writeOffset;

        public static ReadOnlyMemory<byte> FinalizeMessage(BlockBufferSerializer parent, out IDisposable? block)
        {
            var buffer = parent.Buffer;
            if (buffer is null || buffer.IsNonCommittedEmpty)
            {
#if DEBUG // still count it for logging purposes
                if (buffer is not null) buffer._finalizedCount++;
                parent.DebugMessageFinalized(0);
#endif
                return DefaultFinalize(out block);
            }

            return buffer.Finalize(out block);
        }

        // very rare: means either no buffer *ever*, or we're finalizing an empty message (which isn't valid RESP!)
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ReadOnlyMemory<byte> DefaultFinalize(out IDisposable? block)
        {
            block = null;
            return default;
        }
    }
}
