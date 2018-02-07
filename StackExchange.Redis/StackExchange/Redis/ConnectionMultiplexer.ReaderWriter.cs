namespace StackExchange.Redis
{
    partial class ConnectionMultiplexer
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

        internal void RequestWrite(PhysicalBridge bridge, bool forced)
        {
            if (bridge == null) return;
            var tmp = SocketManager;
            if (tmp != null)
            {
                Trace("Requesting write: " + bridge.Name);
                tmp.RequestWrite(bridge, forced);
            }
        }
        partial void OnWriterCreated();
    }
}
