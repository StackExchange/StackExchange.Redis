using System;

namespace StackExchange.Redis
{
    internal class ServerEndPointMaintenanceEventNotifier : IObserver<AzureMaintenanceEvent>
    {
        private readonly ConnectionMultiplexer multiplexer;

        internal ServerEndPointMaintenanceEventNotifier(ConnectionMultiplexer multiplexer)
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
