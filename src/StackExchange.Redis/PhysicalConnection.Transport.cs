using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Buffers;
using RESPite.Streams;
using RESPite.Transports;

namespace StackExchange.Redis
{
    internal sealed partial class PhysicalConnection
    {
        // TRANSPORT MODE: when Tunnel.ConnectTransportAsync yields a DuplexTransport, there is no
        // socket, no Stream and no SslStream — the tunnel owns connect and TLS end-to-end. Outbound
        // rides the existing _output seam via an adapter (the transport IS an IBufferWriter with an
        // explicit Flush, which is the BufferedStreamWriter contract minus the Stream); inbound is
        // PUSH — the transport calls into the same CommitAndParseFrames the pull loops use, on the
        // transport's own threads, so there is no reader thread, no read Task and no read syscall
        // surface at all on this side.
        private DuplexTransport? _transport;

        internal bool HasTransport => _transport is not null;

        private void InitTransportOutput(DuplexTransport transport)
        {
            _output = new TransportWriter(transport, OutputCancel);

            // Same reasoning as InitOutput: nothing awaits WriteComplete in production; observe it so
            // a teardown-time write fault never becomes an UnobservedTaskException.
            _output.WriteComplete.RedisFireAndForget();
#if DEBUG
            if (BridgeCouldBeNull?.Multiplexer?.RawConfig?.OutputLog is { } log)
            {
                _output.DebugSetLog(log);
            }
#endif
        }

        private bool _transportReadingStarted;

        /// <summary>Attach the receiver and begin inbound delivery. Idempotent because it is now called
        /// from TWO places: as soon as the transport is adopted (before the handshake is written, which is
        /// the point), and from <see cref="StartReading"/>, which every connection still goes through and
        /// which must not start a second receiver.</summary>
        private void StartTransportReading(DuplexTransport transport)
        {
            if (_transportReadingStarted) return;
            _transportReadingStarted = true;

            _readStatus = ReadStatus.Init;
            _readState = default;
            _readBuffer = CycleBuffer.Create(pool: ReaderBufferPool);
            transport.Start(new TransportFeed(this));
        }

        /// <summary>Outbound half: the transport is already the writer; this adapter exists only to
        /// satisfy the <see cref="BufferedStreamWriter"/> seam every call site already uses. Staging
        /// forwards straight through; <see cref="Flush"/> hands the batch to the transport; the write
        /// fault/completion surface is a simple once-only task.</summary>
        private sealed class TransportWriter(DuplexTransport transport, CancellationToken cancellationToken)
            : BufferedStreamWriter(Stream.Null, cancellationToken)
        {
            private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _staged; // bytes advanced since the last flush; see HasStagedBytes

            public override Task WriteComplete => _done.Task;

            /// <summary>Whether anything has been advanced but not yet flushed; writes that do not demand
            /// a flush (fire-and-forget) leave bytes here for a later flush to pick up. Deliberately
            /// approximate in the safe direction: it can over-report (a concurrent stage during a flush
            /// stays counted), which costs a redundant flush attempt, never a missed one.</summary>
            public bool HasStagedBytes => Volatile.Read(ref _staged) != 0;

            public override Memory<byte> GetMemory(int sizeHint = 0) => transport.GetMemory(sizeHint);

            public override Span<byte> GetSpan(int sizeHint = 0) => transport.GetSpan(sizeHint);

            public override void Advance(int count)
            {
                transport.Advance(count);
                Interlocked.Add(ref _staged, count);
                OnWritten(count);
            }

            // A false return means the transport is closed; that failure surfaces via the receiver's
            // OnClosed (which records the connection failure), so it is not duplicated here.
            public override void Flush()
            {
                var flushing = Volatile.Read(ref _staged);
                transport.Flush();
                Interlocked.Add(ref _staged, -flushing);
            }

            public override void Complete(Exception? exception = null)
            {
                try { Flush(); }
                catch { }
                if (exception is null)
                {
                    _done.TrySetResult(true);
                }
                else
                {
                    _done.TrySetException(exception);
                }
            }
        }

        /// <summary>Inbound half: transport-owned spans arrive on the transport's schedule and are
        /// committed into the same <see cref="CycleBuffer"/> + <see cref="CommitAndParseFrames"/>
        /// machinery the pull loops use — one copy, zero thread hops, no reader loop.</summary>
        private sealed class TransportFeed(PhysicalConnection connection) : TransportReceiver
        {
            public override bool OnReceived(ReadOnlySpan<byte> payload)
            {
                var conn = connection;
                try
                {
                    while (!payload.IsEmpty)
                    {
                        var space = conn._readBuffer.GetUncommittedMemory().Span;
                        var take = Math.Min(space.Length, payload.Length);
                        payload.Slice(0, take).CopyTo(space);
                        payload = payload.Slice(take);

                        conn._readStatus = ReadStatus.TryParseResult;
                        if (!conn.CommitAndParseFrames(take))
                        {
                            return false;
                        }
                    }
                    conn.UpdateLastReadTime();
                    return !conn.ForceReconnect;
                }
                catch (Exception ex)
                {
                    conn._readStatus = ReadStatus.Faulted;
                    conn.RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
                    return false;
                }
            }

            public override void OnBatchEnd()
            {
                // The contract asks for ONE flush per delivery burst rather than per OnReceived. The only
                // thing that can be staged-but-unflushed here is a write issued by inbound processing
                // that did not demand a flush (typically a fire-and-forget command from a completion
                // continuation running inline on this thread), so check before reaching for the lock -
                // the common case is nothing staged and nothing to do.
                var conn = connection;
                if (conn._output is TransportWriter { HasStagedBytes: true } && conn.BridgeCouldBeNull is { } bridge)
                {
                    bridge.TryFlushStagedWrites(conn);
                }
            }

            public override void OnClosed(Exception? fault)
            {
                var conn = connection;
                conn._readStatus = ReadStatus.RanToCompletion;
                conn._readBuffer.Release();
                conn._readBuffer = default;
                if (fault is null)
                {
                    conn.RecordConnectionFailed(ConnectionFailureType.SocketClosed);
                }
                else
                {
                    conn.RecordConnectionFailed(ConnectionFailureType.InternalFailure, fault);
                }
            }
        }
    }
}
