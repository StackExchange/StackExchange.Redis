using System.Buffers;
using RESPite.Messages;

namespace RESPite.Internal;

internal partial class BlockBufferSerializer
{
    internal static BlockBufferSerializer Create(bool retainChain = false) =>
        new SynchronizedBlockBufferSerializer(retainChain);

    /// <summary>
    /// Used for things like <see cref="RespBatch"/>.
    /// </summary>
    private sealed class SynchronizedBlockBufferSerializer(bool retainChain) : BlockBufferSerializer
    {
        private bool _discardDuringClear;

        private protected override BlockBuffer? Buffer { get; set; } // simple per-instance auto-prop

        /*
        // use lock-based synchronization
        public override ReadOnlyMemory<byte> Serialize<TRequest>(
            RespCommandMap? commandMap,
            ReadOnlySpan<byte> command,
            in TRequest request,
            IRespFormatter<TRequest> formatter)
        {
            bool haveLock = false;
            try // note that "lock" unrolls to something very similar; we're not adding anything unusual here
            {
                // in reality, we *expect* people to not attempt to use batches concurrently, *and*
                // we expect serialization to be very fast, but: out of an abundance of caution,
                // add a timeout - just to avoid surprises (since people can write their own formatters)
                Monitor.TryEnter(this, LockTimeout, ref haveLock);
                if (!haveLock) ThrowTimeout();
                return base.Serialize(commandMap, command, in request, formatter);
            }
            finally
            {
                if (haveLock) Monitor.Exit(this);
            }

            static void ThrowTimeout() => throw new TimeoutException(
                "It took a long time to get access to the serialization-buffer. This is very odd - please " +
                "ask on GitHub, but *as a guess*, you have a custom RESP formatter that is really slow *and* " +
                "you are using concurrent access to a RESP batch / transaction.");
        }
        */

        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

        private Segment? _head, _tail;

        protected override bool ClaimSegment(ReadOnlyMemory<byte> segment)
        {
            if (retainChain & !_discardDuringClear)
            {
                if (_head is null)
                {
                    _head = _tail = new Segment(segment);
                }
                else
                {
                    _tail = new Segment(segment, _tail);
                }

                // note we don't need to increment the ref-count; because of this "true"
                return true;
            }

            return false;
        }

        internal override ReadOnlySequence<byte> Flush()
        {
            if (_head is null)
            {
                // at worst, single-segment - we can skip the alloc
                return new(BlockBuffer.RetainCurrent(this));
            }

            // otherwise, flush everything *keeping the chain*
            ClearWithDiscard(discard: false);
            ReadOnlySequence<byte> seq = new(_head, 0, _tail!, _tail!.Length);
            _head = _tail = null;
            return seq;
        }

        public override void Clear()
        {
            ClearWithDiscard(discard: true);
            _head = _tail = null;
        }

        private void ClearWithDiscard(bool discard)
        {
            try
            {
                _discardDuringClear = discard;
                base.Clear();
            }
            finally
            {
                _discardDuringClear = false;
            }
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public Segment(ReadOnlyMemory<byte> memory, Segment? previous = null)
            {
                Memory = memory;
                if (previous is not null)
                {
                    previous.Next = this;
                    RunningIndex = previous.RunningIndex + previous.Length;
                }
            }

            public int Length => Memory.Length;
        }
    }
}
