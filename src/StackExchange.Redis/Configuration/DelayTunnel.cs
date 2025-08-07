using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.Configuration;

public partial class Tunnel
{
    /// <summary>
    /// Provides an IO tunnel with intentional latency, for testing purposes.
    /// </summary>
    [Experimental(Experiments.DelayTunnel, UrlFormat = Experiments.UrlFormat)]
    public static void AddLatency(ConfigurationOptions options, TimeSpan latency)
    {
        if (latency > TimeSpan.Zero)
        {
            // expected scenario
            options.Tunnel = new DelayTunnel(latency, options.Tunnel);
        }
        else if (latency < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(latency));
        }
        // for zero: nothing to do, don't wrap
    }
}

/// <summary>
/// Provides an IO tunnel with intentional latency, for testing purposes.
/// </summary>
internal sealed class DelayTunnel : Tunnel
{
    private readonly Tunnel? _tail;
    private readonly TimeSpan _latency;

    internal DelayTunnel(TimeSpan latency, Tunnel? tail = null)
    {
        _tail = tail;
        _latency = latency;
    }

    /// <inheritdoc />
    public override ValueTask<Stream?> BeforeAuthenticateAsync(
        EndPoint endpoint,
        ConnectionType connectionType,
        Socket? socket,
        CancellationToken cancellationToken)
    {
        Stream? stream = null;
        if (_tail is not null)
        {
            var pending = _tail.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken);
            if (!pending.IsCompletedSuccessfully) return Awaited(pending);
            stream = pending.GetAwaiter().GetResult();
        }

        return new(WrapStream());

        async ValueTask<Stream?> Awaited(ValueTask<Stream?> pending)
        {
            stream = await pending.ForAwait();
            return WrapStream();
        }

        Stream WrapStream()
        {
            stream ??= new NetworkStream(
                socket ?? throw new InvalidOperationException("No stream or socket available"));
            return new DelayStream(_latency, stream);
        }
    }

    private sealed class DelayStream(TimeSpan latency, Stream tail) : TunnelStream(tail)
    {
        private void Hold() => Thread.Sleep(latency);

        private ConfiguredTaskAwaitable HoldAsync(CancellationToken cancellationToken)
            => Task.Delay(latency, cancellationToken).ForAwait();

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await HoldAsync(cancellationToken);
            return await base.ReadAsync(buffer, offset, count, cancellationToken).ForAwait();
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await HoldAsync(cancellationToken);
            await base.CopyToAsync(destination, bufferSize, cancellationToken).ForAwait();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Hold();
            return base.Read(buffer, offset, count);
        }

#if NETCOREAPP3_0_OR_GREATER
        public override int Read(Span<byte> buffer)
        {
            Hold();
            return base.Read(buffer);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await HoldAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken).ForAwait();
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            Hold();
            base.CopyTo(destination, bufferSize);
        }
#endif

        public override int ReadByte()
        {
            Hold();
            return base.ReadByte();
        }

        // we're mostly interested in read latency, but we'll add some flush latency too
        public override void Flush()
        {
            Hold();
            base.Flush();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await HoldAsync(cancellationToken);
            await base.FlushAsync(cancellationToken).ForAwait();
        }
    }
}
