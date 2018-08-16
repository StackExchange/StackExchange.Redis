using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Describes retry policy functionality that can be provided to the multiplexer to be used for connection reconnects
    /// </summary>
    public interface IReconnectRetryPolicy
    {
        /// <summary>
        /// This method is called by the multiplexer to determine if a reconnect operation can be retried now.
        /// </summary>
        /// <param name="currentRetryCount">The number of times reconnect retries have already been made by the multiplexer while it was in connecting state</param>
        /// <param name="timeElapsedMillisecondsSinceLastRetry">Total time elapsed in milliseconds since the last reconnect retry was made</param>
        bool ShouldRetry(long currentRetryCount, int timeElapsedMillisecondsSinceLastRetry);
    }
}