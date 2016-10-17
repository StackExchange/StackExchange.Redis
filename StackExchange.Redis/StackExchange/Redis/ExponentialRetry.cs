using System;

namespace StackExchange.Redis
{

    public class ExponentialRetry : IReconnectRetryPolicy
    {
        private int deltaBackOffMilliseconds;
        private int maxDeltaBackOffMilliseconds = (int)TimeSpan.FromSeconds(10).TotalMilliseconds;
        [ThreadStatic]
        static private Random r;

        public ExponentialRetry(int deltaBackOffMilliseconds) : this(deltaBackOffMilliseconds, (int)TimeSpan.FromSeconds(10).TotalMilliseconds)
        {
        }

        public ExponentialRetry(int deltaBackOffMilliseconds, int maxDeltaBackOffMilliseconds)
        {
            this.deltaBackOffMilliseconds = deltaBackOffMilliseconds;
            this.maxDeltaBackOffMilliseconds = maxDeltaBackOffMilliseconds;
        }

        public bool ShouldRetry(long currentRetryCount, int timeElapsedMillisecondsSinceLastRetry)
        {
            var exponential = (int)Math.Min(maxDeltaBackOffMilliseconds, deltaBackOffMilliseconds * Math.Pow(1.1, currentRetryCount));
            int random;
            r = r ?? new Random();
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