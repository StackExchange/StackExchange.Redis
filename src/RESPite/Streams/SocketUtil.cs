using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace RESPite.Streams;

internal static class SocketUtil
{
    internal static Socket CreateSocket(EndPoint endpoint, bool tcpKeepAlive)
    {
        var addressFamily = endpoint.AddressFamily;
        var protocolType = addressFamily == AddressFamily.Unix ? ProtocolType.Unspecified : ProtocolType.Tcp;

        var socket = addressFamily == AddressFamily.Unspecified
            ? new Socket(SocketType.Stream, protocolType)
            : new Socket(addressFamily, SocketType.Stream, protocolType);
        TrySetNoDelay(socket);
        if (tcpKeepAlive) TryEnableTcpKeepAlive(socket, endpoint);
        return socket;
    }

    internal static bool TrySetNoDelay(Socket socket)
    {
        try
        {
            if (socket.AddressFamily is not AddressFamily.Unix)
            {
                socket.NoDelay = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message, nameof(Socket));
        }

        return false;
    }

    internal static bool TryEnableTcpKeepAlive(Socket socket, EndPoint endPoint)
    {
        // TCP keep-alive; there's a clue in the name
        if (socket.ProtocolType is not ProtocolType.Tcp) return false;

        switch (endPoint)
        {
#if !NET10_0_OR_GREATER
            // Prior to .NET 10, enabling TCP keep-alive on host-based endpoints fails outside of Windows.
            // see https://github.com/StackExchange/StackExchange.Redis/issues/3086
            case DnsEndPoint when !RuntimeInformation.IsOSPlatform(OSPlatform.Windows): return false;
#endif
            case DnsEndPoint:
            case IPEndPoint:
                // fine
                break;
            default:
                // don't enable on unexpected endpoint types (unix domain sockets, for example)
                return false;
        }

        try
        {
            // enable TCP keep-alive (best effort only)
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            return false;
        }
    }
}
