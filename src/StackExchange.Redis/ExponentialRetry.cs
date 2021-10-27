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
        private readonly double exponentialBase = DefaultExponentialBase;
        /// <summary>
        /// 
        /// </summary>
        public static const double DefaultExponentialBase = 1.1;
        /// <summary>
        /// 
        /// </summary>
        public static const int DefaultDeltaBackOffMiliseconds = 1000;

        [ThreadStatic]
        private static Random r;

        /// <summary>
        /// Initializes a new instance using the specified back off interval with default maxDeltaBackOffMilliseconds of 10 seconds
        /// </summary>
        /// <param name="deltaBackOffMilliseconds">time in milliseconds for the back-off interval between retries</param>
        public ExponentialRetry(int deltaBackOffMilliseconds) : this(deltaBackOffMilliseconds, (int)TimeSpan.FromSeconds(10).TotalMilliseconds) {}

        /// <summary>
        /// Initializes a new instance using the specified back off interval.
        /// </summary>
        /// <param name="deltaBackOffMilliseconds">time in milliseconds for the back-off interval between retries</param>
        /// <param name="maxDeltaBackOffMilliseconds">time in milliseconds for the maximum value that the back-off interval can exponentially grow up to</param>
        public ExponentialRetry(int deltaBackOffMilliseconds, int maxDeltaBackOffMilliseconds) : this(deltaBackOffMilliseconds, maxDeltaBackOffMilliseconds, DefaultExponentialBase) {}


        /// <summary>
        /// Initializes a new instance using the specified back off interval.
        /// </summary>
        /// <param name="deltaBackOffMilliseconds">time in milliseconds for the back-off interval between retries</param>
        /// <param name="maxDeltaBackOffMilliseconds">time in milliseconds for the maximum value that the back-off interval can exponentially grow up to</param>
        /// <param name="exponentialBase">base of the exponential function. The higher the base the faster the growth of the exponential</param>
        public ExponentialRetry(int deltaBackOffMilliseconds, int maxDeltaBackOffMilliseconds, double exponentialBase)
        {
            this.deltaBackOffMilliseconds = deltaBackOffMilliseconds;
            this.maxDeltaBackOffMilliseconds = maxDeltaBackOffMilliseconds;
            this.exponentialBase = exponentialBase;
        }

        /// <summary>
        /// This method is called by the ConnectionMultiplexer to determine if a reconnect operation can be retried now.
        /// </summary>
        /// <param name="currentRetryCount">The number of times reconnect retries have already been made by the ConnectionMultiplexer while it was in the connecting state</param>
        /// <param name="timeElapsedMillisecondsSinceLastRetry">Total elapsed time in milliseconds since the last reconnect retry was made</param>
        public bool ShouldRetry(long currentRetryCount, int timeElapsedMillisecondsSinceLastRetry)
        {
            var exponential = (int)Math.Min(maxDeltaBackOffMilliseconds, deltaBackOffMilliseconds * Math.Pow(this.exponentialBase, currentRetryCount));
            int random;
            r ??= new Random();
            random = r.Next((int)deltaBackOffMilliseconds, exponential);
            return timeElapsedMillisecondsSinceLastRetry >= random;
            //exponential backoff with deltaBackOff of 1000ms and a base of 1.5
            //deltabackoff  exponential
            //1000	        1500
            //1000	        2250
            //1000	        3375
            //1000	        5063
            //1000	        7594
            //1000	        11391
            //1000	        17086
            //1000	        25629
            //
            //exponential backoff with deltaBackOff of 5000ms and a base of 1.1
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
