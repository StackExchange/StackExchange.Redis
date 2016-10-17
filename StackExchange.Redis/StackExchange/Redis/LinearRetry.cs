using System;

namespace StackExchange.Redis
{
    public class LinearRetry : IReconnectRetryPolicy
    {
        private int deltaBackOffMilliseconds;

        public LinearRetry(int deltaBackOffMilliseconds)
        {
            this.deltaBackOffMilliseconds = deltaBackOffMilliseconds;
        }

        public bool ShouldRetry(long currentRetryCount, int timeElapsedMillisecondsSinceLastRetry)
        {
            return timeElapsedMillisecondsSinceLastRetry >= deltaBackOffMilliseconds;
        }
    }
}