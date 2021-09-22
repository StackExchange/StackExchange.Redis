using System;
using static StackExchange.Redis.ConnectionMultiplexer;

namespace StackExchange.Redis
{
    internal class ServerEndPointMaintenanceTopologyRefresher : IObserver<AzureMaintenanceEvent>
    {
        private readonly ConnectionMultiplexer multiplexer;
        private readonly LogProxy logProxy;

        internal ServerEndPointMaintenanceTopologyRefresher(ConnectionMultiplexer multiplexer, LogProxy logProxy)
        {
            this.multiplexer = multiplexer;
            this.logProxy = logProxy;
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
            Console.Out.WriteLine("Event came in.");

            if (StringComparer.OrdinalIgnoreCase.Equals(value.NotificationType, "NodeMaintenanceEnded") || StringComparer.OrdinalIgnoreCase.Equals(value.NotificationType, "NodeMaintenanceFailover"))
            {
                Console.Out.WriteLine("Event came in, about to refresh topology.");
                multiplexer.ReconfigureAsync(first: false, reconfigureAll: true, log: logProxy, blame: null, cause: "server maintenance").Wait();
            }
        }
    }
}
