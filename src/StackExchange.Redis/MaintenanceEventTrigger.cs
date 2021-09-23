using System;

namespace StackExchange.Redis
{
    internal class MaintenanceEventTrigger : IObserver<AzureMaintenanceEvent>
    {
        private readonly ConnectionMultiplexer multiplexer;

        internal MaintenanceEventTrigger(ConnectionMultiplexer multiplexer)
        {
            this.multiplexer = multiplexer;
        }

        public void OnCompleted()
        { }

        public void OnError(Exception error)
        { }

        public void OnNext(AzureMaintenanceEvent value)
            => multiplexer.InvokeServerMaintenanceEvent(value);
    }
}
