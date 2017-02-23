using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a retry policy that performs retries at a fixed interval. The retries are performed upto a maximum allowed time.
    /// </summary>
    public class LinearRetry : IReconnectRetryPolicy
    {
        private int maxRetryElapsedTimeAllowedMilliseconds;

        /// <summary>
        /// Initializes a new instance using the specified maximum retry elapsed time allowed.
        /// </summary>
        /// <param name="maxRetryElapsedTimeAllowedMilliseconds">maximum elapsed time in milliseconds to be allowed for it to perform retries</param>
        public LinearRetry(int maxRetryElapsedTimeAllowedMilliseconds)
        {
            this.maxRetryElapsedTimeAllowedMilliseconds = maxRetryElapsedTimeAllowedMilliseconds;
        }

        /// <summary>
        /// This method is called by the ConnectionMultiplexer to determine if a reconnect operation can be retried now.
        /// </summary>
        /// <param name="currentRetryCount">The number of times reconnect retries have already been made by the ConnectionMultiplexer while it was in the connecting state</param>
        /// <param name="timeElapsedMillisecondsSinceLastRetry">Total elapsed time in milliseconds since the last reconnect retry was made</param>
        public bool ShouldRetry(long currentRetryCount, int timeElapsedMillisecondsSinceLastRetry)
        {
            return timeElapsedMillisecondsSinceLastRetry >= maxRetryElapsedTimeAllowedMilliseconds;
        }
    }
}