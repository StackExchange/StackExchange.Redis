using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.Configuration
{
    /// <summary>
    /// Allows interception of the transport used to communicate with redis.
    /// </summary>
    public abstract class Tunnel
    {
        /// <summary>
        /// Gets the underlying endpoint to use when connecting to a logical endpoint.
        /// </summary>
        /// <remarks><c>null</c> should be returned if a socket is not required for this endpoint.</remarks>
        public virtual ValueTask<EndPoint?> GetConnectEndpoint(EndPoint endpoint, CancellationToken cancellationToken) => new(endpoint);

        /// <summary>
        /// Invoked on a connected endpoint before server authentication and other handshakes occur, allowing pre-redis handshakes.
        /// </summary>
        public virtual ValueTask<Stream?> BeforeAuthenticate(EndPoint endpoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken) => new(default(Stream));

        /// <inheritdoc/>
        public abstract override string ToString();

        private sealed class HttpProxyTunnel : Tunnel
        {
            public EndPoint Proxy { get; }
            public HttpProxyTunnel(EndPoint proxy) => Proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));


            public override ValueTask<EndPoint?> GetConnectEndpoint(EndPoint endpoint, CancellationToken cancellationToken) => new(Proxy);

            public override ValueTask<Stream?> BeforeAuthenticate(EndPoint endpoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken)
            {
                if (socket is not null)
                {

                    // TODO: make write+read async
                    // TODO: compare the response more appropriately?

                    var encoding = Encoding.ASCII;
                    var ep = Format.ToString(endpoint);
                    const string Prefix = "CONNECT ", Suffix = " HTTP/1.1\r\n\r\n", ExpectedResponse = "HTTP/1.1 200 OK\r\n\r\n";
                    byte[] chunk = ArrayPool<byte>.Shared.Rent(Math.Max(
                        encoding.GetByteCount(Prefix) + encoding.GetByteCount(ep) + encoding.GetByteCount(Suffix),
                        encoding.GetByteCount(ExpectedResponse)
                    ));
                    var offset = 0;
                    offset += encoding.GetBytes(Prefix, 0, Prefix.Length, chunk, offset);
                    offset += encoding.GetBytes(ep, 0, ep.Length, chunk, offset);
                    offset += encoding.GetBytes(Suffix, 0, Suffix.Length, chunk, offset);
                    socket.Send(chunk, offset, SocketFlags.None);

                    // we expect to see: "HTTP/1.1 200 OK\n"; note our buffer is definitely big enough already
                    int toRead = encoding.GetByteCount(ExpectedResponse), read;
                    offset = 0;
                    while (toRead > 0 && (read = socket.Receive(chunk, offset, toRead, SocketFlags.None)) > 0)
                    {
                        toRead -= read;
                        offset += read;
                    }
                    if (toRead != 0) throw new EndOfStreamException("EOF negotiating HTTP tunnel");
                    // lazy
                    var actualResponse = encoding.GetString(chunk, 0, offset);
                    if (ExpectedResponse != actualResponse)
                    {
                        throw new InvalidOperationException("Unexpected response negotiating HTTP tunnel");
                    }
                    ArrayPool<byte>.Shared.Return(chunk);
                }
                return new(default(Stream));
            }

            public override string ToString() => "http:" + Format.ToString(Proxy);
        }

        /// <summary>
        /// Create a tunnel via an HTTP proxy server.
        /// </summary>
        /// <param name="proxy">The endpoint to use as an HTTP proxy server.</param>
        public static Tunnel HttpProxy(EndPoint proxy) => new HttpProxyTunnel(proxy);
    }

    
}
