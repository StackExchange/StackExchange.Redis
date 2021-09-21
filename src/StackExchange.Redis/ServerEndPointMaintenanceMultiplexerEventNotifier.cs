using System;

namespace StackExchange.Redis
{
    internal class ServerEndPointMaintenanceMultiplexerEventNotifier : IObserver<AzureMaintenanceEvent>
    {
        private readonly ConnectionMultiplexer multiplexer;

        internal ServerEndPointMaintenanceMultiplexerEventNotifier(ConnectionMultiplexer multiplexer)
        {
            this.multiplexer = multiplexer;
        }

        public void OnCompleted()
        {
            return;
        }

        public void OnError(Exception error)
        {
            return;
        }

        public void OnNext(AzureMaintenanceEvent value)
        {
            multiplexer.InvokeServerMaintenanceEvent(value);
        }
    }
}
