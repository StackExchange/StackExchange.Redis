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
            Console.Out.WriteLine("Event came in, about to invoke user function.");
            multiplexer.InvokeServerMaintenanceEvent(value);

            // TODO(ansoedal): Use constants
            if (StringComparer.OrdinalIgnoreCase.Equals(value.NotificationType, "NodeMaintenanceEnded") || StringComparer.OrdinalIgnoreCase.Equals(value.NotificationType, "NodeMaintenanceFailover"))
            {
                // Need to get all the params for reconfigure async
                //multiplexer.ReconfigureAsync().Wait();
            }
        }
    }
}
