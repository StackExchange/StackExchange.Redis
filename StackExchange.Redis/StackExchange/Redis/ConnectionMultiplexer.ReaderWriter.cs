namespace StackExchange.Redis
{
    public partial class ConnectionMultiplexer
    {
        internal SocketManager SocketManager => socketManager;

        private SocketManager socketManager;
        private bool ownsSocketManager;

        partial void OnCreateReaderWriter(ConfigurationOptions configuration)
        {
            ownsSocketManager = configuration.SocketManager == null;
            socketManager = configuration.SocketManager ?? new SocketManager(ClientName, configuration.HighPrioritySocketThreads);
        }

        partial void OnCloseReaderWriter()
        {
            if (ownsSocketManager) socketManager?.Dispose();
            socketManager = null;
        }
        partial void OnWriterCreated();
    }
}
