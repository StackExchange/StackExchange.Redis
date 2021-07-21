using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace StackExchange.Redis
{
    /// <summary>
    /// 
    /// </summary>
    internal class CommandRetryQueueManager : IDisposable
    { 
        readonly Queue<FailedCommand> queue = new Queue<FailedCommand>();
        int? maxRetryQueueLength;

       /// <summary>
       /// 
       /// </summary>
       /// <param name="maxRetryQueueLength"></param>
        internal CommandRetryQueueManager(int? maxRetryQueueLength = null)
        {
            this.maxRetryQueueLength = maxRetryQueueLength;
        }

        internal int RetryQueueCount => queue.Count;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryHandleFailedCommand(FailedCommand message)
        {
            bool wasEmpty;
            lock (queue)
            {
                int count = queue.Count;
                if (maxRetryQueueLength.HasValue && count >= maxRetryQueueLength)
                {
                    return false;
                }
                wasEmpty = count == 0;
                queue.Enqueue(message);
            }
            if (wasEmpty) StartRetryQueueProcessor();
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void StartRetryQueueProcessor()
        {
            bool startProcessor = false;
            lock (queue)
            {
                startProcessor = queue.Count > 0;
            }
            if (startProcessor) Task.Run(ProcessRetryQueueAsync);
        }

        private async Task ProcessRetryQueueAsync()
        {
            FailedCommand message = null;
            long messageProcessedCount = 0;
            bool shouldWait = false;
            while (true)
            {
                message = null;
                CheckRetryQueueForTimeouts();
                if (shouldWait) await Task.Delay(1000);
                shouldWait = false;

                lock (queue)
                {
                    if (queue.Count == 0) break; // all done
                    message = queue.Peek();
                    if (!message.IsEndpointAvailable())
                    {
                        shouldWait = true;
                        continue;
                    }
                    message = queue.Dequeue();
                }
                try
                {
                    await message.TryResendAsync();
                }
                catch (Exception ex)
                {
                    HandleException(message, ex);
                }
                messageProcessedCount++;
            }
        }

        internal void HandleException(FailedCommand message, Exception ex)
        {
            var inner = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Failed while retrying on connection restore", ex);
            message.SetExceptionAndComplete(inner, null, onConnectionRestoreRetry: false);
        }

        internal void CheckRetryQueueForTimeouts() // check the head of the backlog queue, consuming anything that looks dead
        {
            lock (queue)
            {
                var now = Environment.TickCount;
                while (queue.Count != 0)
                {
                    var message = queue.Peek();
                    if (!message.HasTimedOut)
                    {
                        break; // not a timeout - we can stop looking
                    }
                    queue.Dequeue();
                    RedisTimeoutException ex = message.GetTimeoutException();
                    HandleException(message, ex);
                }
            }
        }

        private bool disposedValue = false;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    lock (queue)
                    {
                        queue.Clear();
                    }
                }
                disposedValue = true;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }
    }
}
