using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
#nullable enable

namespace StackExchange.Redis.Connections
{
    internal class MessageWriter : IBufferWriter<byte>
    {
        private readonly List<ReadOnlyMemory<byte>> _buffers = new();
        private readonly RefCountedMemoryPool<byte> _pool;

        public MessageWriter(RefCountedMemoryPool<byte>? pool = null)
            => _pool = pool ?? RefCountedMemoryPool<byte>.Shared;

        public WrittenMessage Write(Message message, PhysicalConnection connection)
        {
            message.WriteTo(connection, this);
            return new WrittenMessage(Flush(), message);
        }

        private Memory<byte> _current;
        private int _committed;

        void IBufferWriter<byte>.Advance(int count)
        {
            if ((count < 0) || (_committed + count > _current.Length)) Throw();
            _committed += count;

            static void Throw() => throw new ArgumentOutOfRangeException(nameof(count));
        }
        public Memory<byte> GetMemory(int sizeHint)
        {
            // the IBufferWriter API is a bit... woolly; we need to fudge things a bit, because: often
            // they'll ask for something humble (or even -ve/zero), and hope for more; we need to facilitate that
            const int REASONABLE_MIN_LENGTH = 128, REASONABLE_MAX_LENGTH = 16 * 1024;
            sizeHint = Math.Min(Math.Max(sizeHint, REASONABLE_MIN_LENGTH), REASONABLE_MAX_LENGTH);

            if (_current.Length < _committed + sizeHint)
            {
                FlushCurrent();
                _current = _pool.RentMemory(sizeHint);
            }
            return _current.Slice(_committed);
        }
        Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint) => GetMemory(sizeHint).Span;

        void FlushCurrent()
        {
            if (_committed == 0)
            {
                _current.Release();
            }
            else
            {
                _buffers.Add(_current.Slice(0, _committed));
                _committed = 0;
            }
            _current = default;
        }

        public ReadOnlySequence<byte> Flush()
        {
            FlushCurrent();
            var result = CreateSequence(_buffers);
            Debug.Assert(result.Length == _buffers.Sum(x => x.Length), $"MessageWriter length mismatch: {result.Length} vs {_buffers.Sum(x => x.Length)}; {_buffers.Count} buffers");
            _buffers.Clear();
            return result;
        }

        private static ReadOnlySequence<byte> CreateSequence(List<ReadOnlyMemory<byte>> buffers)
        {
            switch (buffers.Count)
            {
                case 0:
                    return default;
                case 1:
                    var buffer = buffers[0];
                    if (buffer.IsEmpty)
                    {
                        buffer.Release();
                        return default;
                    }
                    return buffer.AsReadOnlySequence();
            }

            var iter = buffers.GetEnumerator();
            iter.MoveNext();
            FrameSequenceSegment? first = null, last = null;
            foreach (var buffer in buffers)
            {
                if (buffer.IsEmpty)
                {
                    buffer.Release();
                }
                else
                {
                    // since there are headers between payloads, we are never
                    // able to merge frames to make a larger payload buffer; that's fine
                    last = new FrameSequenceSegment(last, buffer);
                    if (first is null) first = last;
                }
            }

            if (first is null) return default;
#if !NET472 // avoid this optimization on netfx; due to the ROS bug, this might end up allocating a second ReadOnlySequenceSegment (if no array support)
            if (ReferenceEquals(first, last)) return first.Memory.AsReadOnlySequence();
#endif
            return new ReadOnlySequence<byte>(first, 0, last!, last!.Memory.Length);
        }

        private sealed class FrameSequenceSegment : ReadOnlySequenceSegment<byte>
        {
            public FrameSequenceSegment(FrameSequenceSegment? previous, ReadOnlyMemory<byte> memory) : base()
            {
                Memory = memory;
                Next = null;
                if (previous is not null)
                {
                    previous.Next = this;
                    RunningIndex = previous.RunningIndex + previous.Memory.Length;
                }
                else
                {
                    RunningIndex = 0;
                }
            }
        }
    }
}
