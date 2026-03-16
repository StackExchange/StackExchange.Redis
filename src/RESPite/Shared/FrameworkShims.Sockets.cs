// ReSharper disable once CheckNamespace
namespace System.Net.Sockets;

internal static class SocketExtensions
{
#if !NET
    internal static ValueTask ConnectAsync(this Socket socket, EndPoint remoteEP, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    internal static ValueTask<int> SendAsync(this Socket socket, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    internal static ValueTask<int> ReceiveAsync(this Socket socket, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
#endif
}
