using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Internal;
using Xunit;

namespace RESP.Core.Tests;

internal sealed class TestServer : IDisposable
{
    private readonly TestRespServerStream _stream = new();
    public RespConnection Connection { get; }

    public TestServer(RespConfiguration? configuration = null)
    {
        Connection = new StreamConnection(
            RespContext.Null.WithCancellationToken(TestContext.Current.CancellationToken),
            configuration ?? RespConfiguration.Default,
            _stream);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        Connection?.Dispose();
    }

    public static ValueTask<T> Execute<T>(
        Func<RespContext, ValueTask<T>> operation,
        ReadOnlySpan<byte> request,
        ReadOnlySpan<byte> response)
        => ExecuteCore(operation, request, response);

    // intended for use with [InlineData("...")] scenarios
    public static ValueTask<T> Execute<T>(
        Func<RespContext, ValueTask<T>> operation,
        string request,
        string response)
    {
        var lease = Encode(request, response, out var reqSpan, out var respSpan);
        return ExecuteCore(operation, reqSpan, respSpan, lease);
    }

    private static byte[] Encode(
        string request,
        string response,
        out ReadOnlySpan<byte> requestSpan,
        out ReadOnlySpan<byte> responseSpan)
    {
        var byteCount = Encoding.UTF8.GetByteCount(request) + Encoding.UTF8.GetByteCount(response);
        var lease = ArrayPool<byte>.Shared.Rent(byteCount);
        var reqLen = Encoding.UTF8.GetBytes(request.AsSpan(), lease.AsSpan());
        var respLen = Encoding.UTF8.GetBytes(response.AsSpan(), lease.AsSpan(reqLen));
        requestSpan = lease.AsSpan(0, reqLen);
        responseSpan = lease.AsSpan(reqLen, respLen);
        return lease;
    }

    private static ValueTask<T> ExecuteCore<T>(
        Func<RespContext, ValueTask<T>> operation,
        ReadOnlySpan<byte> request,
        ReadOnlySpan<byte> response,
        byte[]? lease = null)
    {
        bool disposeServer = true;
        TestServer? server = null;
        try
        {
            server = new TestServer();
            var pending = operation(server.Context);
            server.AssertSent(request);
            Assert.False(pending.IsCompleted);
            server.Respond(response);
            disposeServer = false;
            return AwaitAndDispose(server, pending);
        }
        finally
        {
            if (disposeServer) server?.Dispose();
            if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
        }

        static async ValueTask<T> AwaitAndDispose(TestServer server, ValueTask<T> pending)
        {
            using (server)
            {
                return await pending.ConfigureAwait(false);
            }
        }
    }

    public static ValueTask Execute<T>(
        Func<RespContext, ValueTask<T>> operation,
        ReadOnlySpan<byte> request,
        ReadOnlySpan<byte> response,
        T expected)
        => AwaitAndValidate(Execute<T>(operation, request, response), expected);

    // intended for use with [InlineData("...")] scenarios
    public static ValueTask Execute<T>(
        Func<RespContext, ValueTask<T>> operation,
        string request,
        string response,
        T expected)
        => AwaitAndValidate(Execute<T>(operation, request, response), expected);

    public static ValueTask Execute(
        Func<RespContext, ValueTask> operation,
        ReadOnlySpan<byte> request,
        ReadOnlySpan<byte> response)
        => ExecuteCore(operation, request, response);

    // intended for use with [InlineData("...")] scenarios
    public static ValueTask Execute(
        Func<RespContext, ValueTask> operation,
        string request,
        string response)
    {
        var lease = Encode(request, response, out var reqSpan, out var respSpan);
        return ExecuteCore(operation, reqSpan, respSpan, lease);
    }

    private static ValueTask ExecuteCore(
        Func<RespContext, ValueTask> operation,
        ReadOnlySpan<byte> request,
        ReadOnlySpan<byte> response,
        byte[]? lease = null)
    {
        bool disposeServer = true;
        TestServer? server = null;
        try
        {
            server = new TestServer();
            var pending = operation(server.Context);
            server.AssertSent(request);
            Assert.False(pending.IsCompleted);
            server.Respond(response);
            disposeServer = false;
            return AwaitAndDispose(server, pending);
        }
        finally
        {
            if (disposeServer) server?.Dispose();
            if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
        }

        static async ValueTask AwaitAndDispose(TestServer server, ValueTask pending)
        {
            using (server)
            {
                await pending.ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask AwaitAndValidate<T>(ValueTask<T> pending, T expected)
    {
        var actual = await pending.ConfigureAwait(false);
        Assert.Equal(expected, actual);
    }

    public ref readonly RespContext Context => ref Connection.Context;

    public void Respond(ReadOnlySpan<byte> serverToClient) => _stream.Respond(serverToClient);
    public void AssertSent(ReadOnlySpan<byte> clientToServer) => _stream.AssertSent(clientToServer);

    private sealed class TestRespServerStream : Stream
    {
        private bool _disposed, _closed;

        public override void Close()
        {
            _closed = true;
            lock (InboundLock)
            {
                Monitor.PulseAll(InboundLock);
            }
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            if (disposing)
            {
                lock (InboundLock)
                {
                    Monitor.PulseAll(InboundLock);
                }
            }
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadCore(buffer.AsSpan(offset, count));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = ReadCore(buffer.AsSpan(offset, count));
            return Task.FromResult(read);
        }

        public void Respond(ReadOnlySpan<byte> serverToClient)
        {
            lock (InboundLock)
            {
                if (!(_disposed | _disposed))
                {
                    _inbound.Write(serverToClient);
                }

                Monitor.PulseAll(InboundLock);
            }
        }

        private int ReadCore(Span<byte> destination)
        {
            ThrowIfDisposed();
            lock (InboundLock)
            {
                while (_inbound.CommittedIsEmpty)
                {
                    if (_closed) return 0;
                    Monitor.Wait(InboundLock);
                    ThrowIfDisposed();
                }

                if (destination.IsEmpty) return 0; // zero-length read
                Assert.True(_inbound.TryGetFirstCommittedSpan(1, out var span));
                Assert.False(span.IsEmpty);
                if (span.Length > destination.Length) span = span.Slice(0, destination.Length);
                span.CopyTo(destination);
                return span.Length;
            }
        }

#if NET
        public override int Read(Span<byte> buffer) => ReadCore(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = ReadCore(buffer.Span);
            return new(read);
        }
#endif

        private readonly object OutboundLock = new object(), InboundLock = new object();

        private CycleBuffer _outbound = CycleBuffer.Create(MemoryPool<byte>.Shared),
            _inbound = CycleBuffer.Create(MemoryPool<byte>.Shared);

        private void WriteCore(ReadOnlySpan<byte> source)
        {
            lock (OutboundLock)
            {
                _outbound.Write(source);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
            => WriteCore(buffer.AsSpan(offset, count));

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCore(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

#if NET
        public override void Write(ReadOnlySpan<byte> buffer) => WriteCore(buffer);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCore(buffer.Span);
            return default;
        }
#endif

        /// <summary>
        /// Verifies and discards outbound data.
        /// </summary>
        public void AssertSent(ReadOnlySpan<byte> clientToServer)
        {
            lock (OutboundLock)
            {
                var available = _outbound.GetCommittedLength();
                Assert.True(
                    available >= clientToServer.Length,
                    $"expected {clientToServer.Length} bytes, {available} available");
                while (!clientToServer.IsEmpty)
                {
                    Assert.True(_outbound.TryGetFirstCommittedSpan(1, out var received), "should have data available");
                    var take = Math.Min(received.Length, clientToServer.Length);
                    Assert.True(take > 0, "should have some data to compare");
                    var xBytes = clientToServer.Slice(0, take);
                    var yBytes = received.Slice(0, take);
                    if (!xBytes.SequenceEqual(yBytes))
                    {
                        var xText = Encoding.UTF8.GetString(xBytes).Replace("\r\n", "\\r\\n");
                        var yText = Encoding.UTF8.GetString(yBytes).Replace("\r\n", "\\r\\n");
                        Assert.Equal(xText, yText);
                    }

                    _outbound.DiscardCommitted(take);
                    clientToServer = clientToServer.Slice(take);
                }
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
