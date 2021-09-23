using System;
using static StackExchange.Redis.ConnectionMultiplexer;

namespace StackExchange.Redis
{
    internal class ReconfigureAfterMaintenance : IObserver<AzureMaintenanceEvent>
    {
        private readonly ConnectionMultiplexer multiplexer;
        private readonly LogProxy logProxy;

        internal ReconfigureAfterMaintenance(ConnectionMultiplexer multiplexer, LogProxy logProxy)
        {
            this.multiplexer = multiplexer;
            this.logProxy = logProxy;
        }

        public void OnCompleted()
        { }

        public void OnError(Exception error)
        { }

        public void OnNext(AzureMaintenanceEvent value)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(value.NotificationType, "NodeMaintenanceEnded") || StringComparer.OrdinalIgnoreCase.Equals(value.NotificationType, "NodeMaintenanceFailover"))
            {
                multiplexer.ReconfigureAsync(first: false, reconfigureAll: true, log: logProxy, blame: null, cause: "server maintenance").Wait();
            }
        }
    }
}
