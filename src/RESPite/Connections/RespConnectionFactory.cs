using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace RESPite.Connections;

/// <summary>
/// Controls connection to endpoints. By default, this is TCP streams.
/// </summary>
// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class RespConnectionFactory
{
    private static RespConnectionFactory? _default, _defaultTls;
    public static RespConnectionFactory Default => _default ??= new();
    public static RespConnectionFactory DefaultTls => _defaultTls ??= new(true);
    protected RespConnectionFactory(bool tls = false) => _tls = tls;
    private readonly bool _tls;

    public virtual string DefaultHost => "127.0.0.1";
    public virtual int DefaultPort => _tls ? 6380 : 6379;

    /// <summary>
    /// Connect to the designated endpoint and return an open <see cref="RespConnection"/> for the duplex
    /// connection.
    /// </summary>
    /// <param name="endpoint">The location to connect to; how this is interpreted is implementation-specific,
    /// but will commonly be an IP address or DNS hostname.</param>
    /// <param name="port">The port to connect to, if appropriate.</param>
    /// <param name="configuration">The configuration for the connection.</param>
    /// <param name="cancellationToken">Cancellation for the operation.</param>
    /// <returns>An open <see cref="RespConnection"/> for the duplex connection.</returns>
    public virtual async ValueTask<RespConnection> ConnectAsync(
        string endpoint,
        int port,
        RespConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        var ep = GetEndPoint(endpoint, port);
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;
#if NET6_0_OR_GREATER
        await socket.ConnectAsync(ep, cancellationToken).ConfigureAwait(false);
#else
        // hack together cancellation via dispose
        using (cancellationToken.Register(
                   static state => ((Socket)state).Dispose(), socket))
        {
            try
            {
                await socket.ConnectAsync(ep).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
#endif
        var stream = new NetworkStream(socket);
        var authed = await AuthenticateAsync(stream, cancellationToken).ConfigureAwait(false);
        return RespConnection.Create(authed, configuration);
    }

    protected virtual ValueTask<Stream> AuthenticateAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (_tls) throw new NotImplementedException("TLS");
        return new(stream);
    }

    protected internal virtual EndPoint GetEndPoint(string endpoint, int port)
    {
        if (port == 0) port = DefaultPort;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = DefaultHost;
        }

        return endpoint switch
        {
            "127.0.0.1" => new IPEndPoint(IPAddress.Loopback, port),
            "::1" or "0:0:0:0:0:0:0:1" => new IPEndPoint(IPAddress.IPv6Loopback, port),
            _ when IPAddress.TryParse(endpoint, out var address) => new IPEndPoint(address, port),
            _ => new DnsEndPoint(endpoint, port),
        };
    }

    public virtual bool TryParse(EndPoint endpoint, out string host, out int port)
    {
        if (endpoint is DnsEndPoint dns)
        {
            host = dns.Host switch
            {
                "localhost" or "." => "127.0.0.1",
                _ => dns.Host,
            };
            port = dns.Port;
            return true;
        }

        if (endpoint is IPEndPoint ip)
        {
            host = ip.Address.ToString();
            port = ip.Port;
            return true;
        }

        host = "";
        port = 0;
        return false;
    }

    public virtual bool TryParse(string hostAndPort, out string host, out int port)
    {
        int i = hostAndPort.LastIndexOf(':');
        if (i < 0)
        {
            host = hostAndPort;
            port = 0;
            return true;
        }

        host = hostAndPort.Substring(0, i);
        if (int.TryParse(hostAndPort.Substring(i + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
        {
            return true;
        }

        host = hostAndPort;
        port = 0;
        return false;
    }
}
