namespace StackExchange.Redis
{
    public partial class ConnectionMultiplexer
    {
        internal SocketManager SocketManager { get; private set; }

        partial void OnCreateReaderWriter(ConfigurationOptions configuration)
        {
            SocketManager = configuration.SocketManager ?? SocketManager.Shared;
        }

        partial void OnCloseReaderWriter()
        {
            SocketManager = null;
        }
        partial void OnWriterCreated();
    }
}
