using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis.Configuration
{
    /// <summary>
    /// Allows interception of the transport used to communicate with Redis.
    /// </summary>
    public abstract class Tunnel
    {
        /// <summary>
        /// Gets the underlying socket endpoint to use when connecting to a logical endpoint.
        /// </summary>
        /// <remarks><c>null</c> should be returned if a socket is not required for this endpoint.</remarks>
        public virtual ValueTask<EndPoint?> GetSocketConnectEndpointAsync(EndPoint endpoint, CancellationToken cancellationToken) => new(endpoint);

        internal virtual bool IsInbuilt => false; // only inbuilt tunnels get added to config strings

        /// <summary>
        /// Allows modification of a <see cref="Socket"/> between creation and connection.
        /// Passed in is the endpoint we're connecting to, which type of connection it is, and the socket itself.
        /// For example, a specific local IP endpoint could be bound, linger time altered, etc.
        /// </summary>
        public virtual ValueTask BeforeSocketConnectAsync(EndPoint endPoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken) => default;

        /// <summary>
        /// Invoked on a connected endpoint before server authentication and other handshakes occur, allowing pre-redis handshakes. By returning a custom <see cref="Stream"/>,
        /// the entire data flow can be intercepted, providing entire custom transports.
        /// </summary>
        public virtual ValueTask<Stream?> BeforeAuthenticateAsync(EndPoint endpoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken) => default;

        private sealed class HttpProxyTunnel : Tunnel
        {
            public EndPoint Proxy { get; }
            public HttpProxyTunnel(EndPoint proxy) => Proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));

            public override ValueTask<EndPoint?> GetSocketConnectEndpointAsync(EndPoint endpoint, CancellationToken cancellationToken) => new(Proxy);

            public override async ValueTask<Stream?> BeforeAuthenticateAsync(EndPoint endpoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken)
            {
                if (socket is not null)
                {
                    var encoding = Encoding.ASCII;
                    var ep = Format.ToString(endpoint);
                    const string Prefix = "CONNECT ", Suffix = " HTTP/1.1\r\n\r\n", ExpectedResponse1 = "HTTP/1.1 200 OK\r\n\r\n", ExpectedResponse2 = "HTTP/1.1 200 Connection established\r\n\r\n";
                    byte[] chunk = ArrayPool<byte>.Shared.Rent(Math.Max(
                        encoding.GetByteCount(Prefix) + encoding.GetByteCount(ep) + encoding.GetByteCount(Suffix),
                        Math.Max(encoding.GetByteCount(ExpectedResponse1), encoding.GetByteCount(ExpectedResponse2))
                    ));
                    var offset = 0;
                    offset += encoding.GetBytes(Prefix, 0, Prefix.Length, chunk, offset);
                    offset += encoding.GetBytes(ep, 0, ep.Length, chunk, offset);
                    offset += encoding.GetBytes(Suffix, 0, Suffix.Length, chunk, offset);

                    static void SafeAbort(object? obj)
                    {
                        try
                        {
                            (obj as SocketAwaitableEventArgs)?.Abort(SocketError.TimedOut);
                        }
                        catch { } // best effort only
                    }

                    using (var args = new SocketAwaitableEventArgs())
                    using (cancellationToken.Register(static s => SafeAbort(s), args))
                    {
                        args.SetBuffer(chunk, 0, offset);
                        if (!socket.SendAsync(args)) args.Complete();
                        await args;

                        // we expect to see: "HTTP/1.1 200 OK\n"; note our buffer is definitely big enough already
                        int toRead = Math.Max(encoding.GetByteCount(ExpectedResponse1), encoding.GetByteCount(ExpectedResponse2)), read;
                        offset = 0;

                        var actualResponse = "";
                        while (toRead > 0 && !actualResponse.EndsWith("\r\n\r\n"))
                        {
                            args.SetBuffer(chunk, offset, toRead);
                            if (!socket.ReceiveAsync(args)) args.Complete();
                            read = await args;

                            if (read <= 0) break; // EOF (since we're never doing zero-length reads)
                            toRead -= read;
                            offset += read;

                            actualResponse = encoding.GetString(chunk, 0, offset);
                        }
                        if (toRead != 0 && !actualResponse.EndsWith("\r\n\r\n")) throw new EndOfStreamException("EOF negotiating HTTP tunnel");
                        // lazy
                        if (ExpectedResponse1 != actualResponse && ExpectedResponse2 != actualResponse)
                        {
                            throw new InvalidOperationException("Unexpected response negotiating HTTP tunnel");
                        }
                        ArrayPool<byte>.Shared.Return(chunk);
                    }
                }
                return default; // no need for custom stream wrapper here
            }

            internal override bool IsInbuilt => true;
            public override string ToString() => "http:" + Format.ToString(Proxy);
        }

        /// <summary>
        /// Create a tunnel via an HTTP proxy server.
        /// </summary>
        /// <param name="proxy">The endpoint to use as an HTTP proxy server.</param>
        public static Tunnel HttpProxy(EndPoint proxy) => new HttpProxyTunnel(proxy);
    }
}
