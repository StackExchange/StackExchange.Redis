﻿using System;
using System.Buffers;
using System.IO;

namespace StackExchange.Redis
{
    internal enum ConsumeResult
    {
        Failure,
        Success,
        NeedMoreData,
    }
    internal ref struct BufferReader
    {
        private ReadOnlySequence<byte>.Enumerator _iterator;
        private ReadOnlySpan<byte> _current;

        public ReadOnlySpan<byte> OversizedSpan => _current;

        public ReadOnlySpan<byte> SlicedSpan => _current.Slice(OffsetThisSpan, RemainingThisSpan);
        public int OffsetThisSpan { get; private set; }
        private int TotalConsumed { get; set; } // hide this; callers should use the snapshot-aware methods instead
        public int RemainingThisSpan { get; private set; }

        public bool IsEmpty => RemainingThisSpan == 0;

        private bool FetchNextSegment()
        {
            do
            {
                if (!_iterator.MoveNext())
                {
                    OffsetThisSpan = RemainingThisSpan = 0;
                    return false;
                }

                _current = _iterator.Current.Span;
                OffsetThisSpan = 0;
                RemainingThisSpan = _current.Length;
            } while (IsEmpty); // skip empty segments, they don't help us!

            return true;
        }

        public BufferReader(ReadOnlySequence<byte> buffer)
        {
            _buffer = buffer;
            _lastSnapshotPosition = buffer.Start;
            _lastSnapshotBytes = 0;
            _iterator = buffer.GetEnumerator();
            _current = default;
            OffsetThisSpan = RemainingThisSpan = TotalConsumed = 0;

            FetchNextSegment();
        }

        private static readonly byte[] CRLF = { (byte)'\r', (byte)'\n' };

        /// <summary>
        /// Note that in results other than success, no guarantees are made about final state; if you care: snapshot
        /// </summary>
        public ConsumeResult TryConsumeCRLF()
        {
            switch (RemainingThisSpan)
            {
                case 0:
                    return ConsumeResult.NeedMoreData;
                case 1:
                    if (_current[OffsetThisSpan] != (byte)'\r') return ConsumeResult.Failure;
                    Consume(1);
                    if (IsEmpty) return ConsumeResult.NeedMoreData;
                    var next = _current[OffsetThisSpan];
                    Consume(1);
                    return next == '\n' ? ConsumeResult.Success : ConsumeResult.Failure;
                default:
                    var offset = OffsetThisSpan;
                    var result = _current[offset++] == (byte)'\r' && _current[offset] == (byte)'\n'
                        ? ConsumeResult.Success : ConsumeResult.Failure;
                    Consume(2);
                    return result;
            }
        }
        public bool TryConsume(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            do
            {
                var available = RemainingThisSpan;
                if (count <= available)
                {
                    // consume part of this span
                    TotalConsumed += count;
                    RemainingThisSpan -= count;
                    OffsetThisSpan += count;

                    if (count == available) FetchNextSegment(); // burned all of it; fetch next
                    return true;
                }

                // consume all of this span
                TotalConsumed += available;
                count -= available;
            } while (FetchNextSegment());
            return false;
        }

        private readonly ReadOnlySequence<byte> _buffer;
        private SequencePosition _lastSnapshotPosition;
        private long _lastSnapshotBytes;

        // makes an internal note of where we are, as a SequencePosition; useful
        // to avoid having to use buffer.Slice on huge ranges
        private SequencePosition SnapshotPosition()
        {
            var consumed = TotalConsumed;
            var delta = consumed - _lastSnapshotBytes;
            if (delta == 0) return _lastSnapshotPosition;

            var pos = _buffer.GetPosition(delta, _lastSnapshotPosition);
            _lastSnapshotBytes = consumed;
            return _lastSnapshotPosition = pos;
        }
        public ReadOnlySequence<byte> ConsumeAsBuffer(int count)
        {
            if (!TryConsumeAsBuffer(count, out var buffer)) throw new EndOfStreamException();
            return buffer;
        }
        public ReadOnlySequence<byte> ConsumeToEnd()
        {
            var from = SnapshotPosition();
            var result = _buffer.Slice(from);
            while (FetchNextSegment()) { } // consume all
            return result;
        }
        public bool TryConsumeAsBuffer(int count, out ReadOnlySequence<byte> buffer)
        {
            var from = SnapshotPosition();
            if (!TryConsume(count))
            {
                buffer = default;
                return false;
            }
            var to = SnapshotPosition();
            buffer = _buffer.Slice(from, to);
            return true;
        }
        public void Consume(int count)
        {
            if (!TryConsume(count)) throw new EndOfStreamException();
        }

        internal static int FindNext(BufferReader reader, byte value) // very deliberately not ref; want snapshot
        {
            int totalSkipped = 0;
            do
            {
                if (reader.RemainingThisSpan == 0) continue;

                var span = reader.SlicedSpan;
                int found = span.IndexOf(value);
                if (found >= 0) return totalSkipped + found;

                totalSkipped += span.Length;
            } while (reader.FetchNextSegment());
            return -1;
        }
        internal static int FindNextCrLf(BufferReader reader) // very deliberately not ref; want snapshot
        {
            // is it in the current span? (we need to handle the offsets differently if so)

            int totalSkipped = 0;
            bool haveTrailingCR = false;
            do
            {
                if (reader.RemainingThisSpan == 0) continue;

                var span = reader.SlicedSpan;
                if (haveTrailingCR)
                {
                    if (span[0] == '\n') return totalSkipped - 1;
                    haveTrailingCR = false;
                }

                int found = span.IndexOf(CRLF);
                if (found >= 0) return totalSkipped + found;

                haveTrailingCR = span[span.Length - 1] == '\r';
                totalSkipped += span.Length;
            }
            while (reader.FetchNextSegment());
            return -1;
        }

        //internal static bool HasBytes(BufferReader reader, int count) // very deliberately not ref; want snapshot
        //{
        //    if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        //    do
        //    {
        //        var available = reader.RemainingThisSpan;
        //        if (count <= available) return true;
        //        count -= available;
        //    } while (reader.FetchNextSegment());
        //    return false;
        //}

        public int ConsumeByte()
        {
            if (IsEmpty) return -1;
            var value = _current[OffsetThisSpan];
            Consume(1);
            return value;
        }
        public int PeekByte() => IsEmpty ? -1 : _current[OffsetThisSpan];

        public ReadOnlySequence<byte> SliceFromCurrent()
        {
            var from = SnapshotPosition();
            return _buffer.Slice(from);
        }
    }
}
