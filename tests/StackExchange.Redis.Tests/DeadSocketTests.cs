using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.Configuration;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A half-open connection - writes accepted, nothing ever read back - is declared dead and replaced, as v3's
/// heartbeat did, with event 90.
/// </summary>
/// <remarks>
/// TCP keep-alive cannot catch this: it probes an idle connection, and this one has requests outstanding, so
/// it is retransmission that governs it - about fifteen minutes on Linux before anything fails. v3 declared the
/// connection dead when commands were timing out and nothing had arrived for four timeouts' worth of time; v4
/// had lost the rule with the old core's bridge, and would have kept timing commands out on a dead socket.
/// </remarks>
public class DeadSocketTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AHalfOpenConnectionIsDeclaredDeadAndReplaced()
    {
        using var server = new InProcessTestServer(output);
        var tunnel = new MutingTunnel(server.Tunnel);
        var events = new EventIdLogger();

        var config = server.GetClientConfig(withPubSub: false);
        config.Protocol = RedisProtocol.Resp3; // one connection, so the muted one is the one being asked
        config.AsyncTimeout = 200;
        config.SyncTimeout = 200;
        config.ReconnectRetryPolicy = new LinearRetry(100);
        config.Tunnel = tunnel;
        config.LoggerFactory = events;

        await using var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var failures = 0;
        conn.ConnectionFailed += (_, e) => Interlocked.Increment(ref failures);
        var db = conn.GetDatabase();
        await db.StringSetAsync("k", "v");

        tunnel.MuteCurrent(); // from here the live stream accepts writes and never returns a byte

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!events.Saw(90) && DateTime.UtcNow < deadline)
        {
            _ = db.StringGetAsync("k").ContinueWith(static t => _ = t.Exception, TaskScheduler.Default); // times out
            await Task.Delay(100);
            conn.OnHeartbeat();
        }

        Assert.True(events.Saw(90), "the dead socket was never detected (event 90)");
        for (var i = 0; i < 50 && Volatile.Read(ref failures) == 0; i++) await Task.Delay(100);
        Assert.True(Volatile.Read(ref failures) > 0, "the dead connection was not failed");

        // and the replacement - a fresh, unmuted stream - works
        string? value = null;
        deadline = DateTime.UtcNow.AddSeconds(10);
        while (value is null && DateTime.UtcNow < deadline)
        {
            try
            {
                value = await db.StringGetAsync("k");
            }
            catch (RedisException)
            {
                await Task.Delay(100); // still reconnecting
            }
        }

        Assert.Equal("v", value);
    }

    /// <summary>Wraps a tunnel's streams so the current ones can be muted: writes accepted, reads never complete.</summary>
    private sealed class MutingTunnel(Tunnel inner) : Tunnel
    {
        private readonly List<MutableStream> _streams = [];

        public void MuteCurrent()
        {
            lock (_streams)
            {
                foreach (var stream in _streams) stream.Mute();
            }
        }

        public override ValueTask<EndPoint?> GetSocketConnectEndpointAsync(EndPoint endpoint, CancellationToken cancellationToken)
            => inner.GetSocketConnectEndpointAsync(endpoint, cancellationToken);

        public override async ValueTask<Stream?> BeforeAuthenticateAsync(EndPoint endpoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken)
        {
            var stream = await inner.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken);
            if (stream is null) return null;
            var wrapped = new MutableStream(stream);
            lock (_streams) _streams.Add(wrapped);
            return wrapped;
        }
    }

    private sealed class MutableStream(Stream inner) : Stream
    {
        private readonly TaskCompletionSource<int> _never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _muted;

        public void Mute() => _muted = true;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
            => _muted ? _never.Task.GetAwaiter().GetResult() : inner.Read(buffer, offset, count);

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_muted) return await ReadNeverAsync(cancellationToken).ConfigureAwait(false);
            var read = await inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            return _muted ? await ReadNeverAsync(cancellationToken).ConfigureAwait(false) : read; // half-open: nothing arrives
        }

#if NET
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_muted) return await ReadNeverAsync(cancellationToken).ConfigureAwait(false);
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            return _muted ? await ReadNeverAsync(cancellationToken).ConfigureAwait(false) : read;
        }
#endif

        private async Task<int> ReadNeverAsync(CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => _never.TrySetCanceled()))
            {
                return await _never.Task.ConfigureAwait(false);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!_muted) inner.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _muted ? Task.CompletedTask : inner.WriteAsync(buffer, offset, count, cancellationToken);

#if NET
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => _muted ? default : inner.WriteAsync(buffer, cancellationToken);
#endif

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _never.TrySetCanceled();
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class EventIdLogger : ILoggerFactory, ILogger
    {
        private readonly HashSet<int> _seen = [];

        public bool Saw(int id)
        {
            lock (_seen) return _seen.Contains(id);
        }

        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Dispose() { }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_seen) _seen.Add(eventId.Id);
        }
    }
}
