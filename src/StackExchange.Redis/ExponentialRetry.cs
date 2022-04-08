using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a retry policy that performs retries, using a randomized exponential back off scheme to determine the interval between retries.
    /// </summary>
    public class ExponentialRetry : IReconnectRetryPolicy
    {
        private readonly int deltaBackOffMilliseconds;
        private readonly int maxDeltaBackOffMilliseconds = (int)TimeSpan.FromSeconds(10).TotalMilliseconds;
        [ThreadStatic]
        private static Random? r;

        /// <summary>
        /// Initializes a new instance using the specified back off interval with default maxDeltaBackOffMilliseconds of 10 seconds.
        /// </summary>
        /// <param name="deltaBackOffMilliseconds">time in milliseconds for the back-off interval between retries</param>
        public ExponentialRetry(int deltaBackOffMilliseconds) : this(deltaBackOffMilliseconds, Math.Max(deltaBackOffMilliseconds, (int)TimeSpan.FromSeconds(10).TotalMilliseconds)) { }

        /// <summary>
        /// Initializes a new instance using the specified back off interval.
        /// </summary>
        /// <param name="deltaBackOffMilliseconds">time in milliseconds for the back-off interval between retries.</param>
        /// <param name="maxDeltaBackOffMilliseconds">time in milliseconds for the maximum value that the back-off interval can exponentially grow up to.</param>
        public ExponentialRetry(int deltaBackOffMilliseconds, int maxDeltaBackOffMilliseconds)
        {
            if (deltaBackOffMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaBackOffMilliseconds), $"{nameof(deltaBackOffMilliseconds)} must be greater than or equal to zero");
            }
            if (maxDeltaBackOffMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDeltaBackOffMilliseconds), $"{nameof(maxDeltaBackOffMilliseconds)} must be greater than or equal to zero");
            }
            if (maxDeltaBackOffMilliseconds < deltaBackOffMilliseconds)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDeltaBackOffMilliseconds), $"{nameof(maxDeltaBackOffMilliseconds)} must be greater than or equal to {nameof(deltaBackOffMilliseconds)}");
            }

            this.deltaBackOffMilliseconds = deltaBackOffMilliseconds;
            this.maxDeltaBackOffMilliseconds = maxDeltaBackOffMilliseconds;
        }

        /// <summary>
        /// This method is called by the ConnectionMultiplexer to determine if a reconnect operation can be retried now.
        /// </summary>
        /// <param name="currentRetryCount">The number of times reconnect retries have already been made by the ConnectionMultiplexer while it was in the connecting state.</param>
        /// <param name="timeElapsedMillisecondsSinceLastRetry">Total elapsed time in milliseconds since the last reconnect retry was made.</param>
        public bool ShouldRetry(long currentRetryCount, int timeElapsedMillisecondsSinceLastRetry)
        {
            var exponential = (int)Math.Min(maxDeltaBackOffMilliseconds, deltaBackOffMilliseconds * Math.Pow(1.1, currentRetryCount));
            int random;
            r ??= new Random();
            random = r.Next((int)deltaBackOffMilliseconds, exponential);
            return timeElapsedMillisecondsSinceLastRetry >= random;
            //exponential backoff with deltaBackOff of 5000ms
            //deltabackoff  exponential 
            //5000	        5500	   
            //5000	        6050	   
            //5000	        6655	   
            //5000	        8053	   
            //5000	        10718	   
            //5000	        17261	   
            //5000	        37001	   
            //5000	        127738	   
        }
    }
}
