using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using RESPite.Resp;
using RESPite.Transports;

namespace BasicTest;

internal class RespiteConnect
{
    internal static async Task<IRequestResponseTransport> ConnectAsync(
    string host,
    int port,
    bool tls,
    IFrameScanner<RespFrameScanner.RespFrameState> frameScanner = null)
    {
        Socket socket = null;
        Stream conn = null;
        try
        {
            var ep = BuildEndPoint(host, port);
            socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            await socket.ConnectAsync(ep);

            conn = new NetworkStream(socket);
            if (tls)
            {
                var ssl = new SslStream(conn);
                conn = ssl;
#if NET5_0_OR_GREATER
                var options = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true,
                    TargetHost = host,
                };
                await ssl.AuthenticateAsClientAsync(options);
#else
                await ssl.AuthenticateAsClientAsync(host);
#endif
            }

            return conn.CreateTransport().RequestResponse(frameScanner ?? RespFrameScanner.Default);
        }
        catch
        {
            conn?.Dispose();
            socket?.Dispose();

            return null;
        }
    }

    public static EndPoint BuildEndPoint(string host, int port)
    {
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return new IPEndPoint(ipAddress, port);
        }
        else
        {
            return new DnsEndPoint(host, port);
        }
    }
}
