using System;
using System.Threading;

namespace StackExchange.Redis
{
    /// <summary>
    /// Provides a common interface for an object that queue work in the background.
    /// </summary>
    public interface IBackgroundWorkQueue
    {
        /// <summary>
        /// Queues a work item.
        /// </summary>
        void QueueItem(WaitCallback callBack, object state);
    }
}
