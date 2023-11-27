using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    internal SocketManager? SocketManager { get; private set; }

    [MemberNotNull(nameof(SocketManager))]
    private void OnCreateReaderWriter(ConfigurationOptions configuration)
    {
        SocketManager = configuration.SocketManager ?? SocketManager.Shared;
    }

    private void OnCloseReaderWriter()
    {
        SocketManager = null;
    }
}
