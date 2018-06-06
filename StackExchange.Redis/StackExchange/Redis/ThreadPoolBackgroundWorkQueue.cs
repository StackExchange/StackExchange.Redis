using System;
using System.Threading;

namespace StackExchange.Redis
{
    /// <summary>
    /// Represents an object that queues work items to execute on the <see cref="ThreadPool"/>.
    /// </summary>
    /// <seealso cref="Object" />
    /// <seealso cref="IBackgroundWorkQueue" />
    public class ThreadPoolBackgroundWorkQueue : IBackgroundWorkQueue
    {
        /// <summary>
        /// Gets the singleton instance of the <see cref="ThreadPoolBackgroundWorkQueue"/>.
        /// </summary>
        public static ThreadPoolBackgroundWorkQueue Instance { get; } = new ThreadPoolBackgroundWorkQueue();

        private ThreadPoolBackgroundWorkQueue() { }

        /// <inheritdoc />
        public void QueueItem(WaitCallback callBack, object state)
        {
            ThreadPool.QueueUserWorkItem(callBack, state);
        }
    }
}
