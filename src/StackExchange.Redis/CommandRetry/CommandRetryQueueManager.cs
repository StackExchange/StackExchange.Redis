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

        public int RetryQueueLength => queue.Count;

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
            try
            {
                while (true)
                {
                    message = null;
                    lock (queue)
                    {
                        if (queue.Count == 0) break; // all done
                        message = queue.Peek();
                        if (!message.IsEndpointAvailable())
                        {
                            break;
                        }
                        message = queue.Dequeue();
                    }
                    try
                    {
                        if (message.HasTimedOut())
                        {
                            RedisTimeoutException ex = message.GetTimeoutException();
                            message.SetExceptionAndComplete(ex);
                        }
                        else
                        {
                            if(!await message.TryResendAsync())
                            {
                                // this should never happen but just to be safe if connection got dropped again
                                message.SetExceptionAndComplete();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        message.SetExceptionAndComplete(ex);
                    }
                    messageProcessedCount++;
                }
            }
            catch(Exception ex)
            {
                DrainQueue(ex);
            }
        }

        internal void CheckRetryQueueForTimeouts() // check the head of the backlog queue, consuming anything that looks dead
        {
            lock (queue)
            {
                var now = Environment.TickCount;
                while (queue.Count != 0)
                {
                    var message = queue.Peek();
                    if (!message.HasTimedOut())
                    {
                        break; // not a timeout - we can stop looking
                    }
                    queue.Dequeue();
                    RedisTimeoutException ex = message.GetTimeoutException();
                    message.SetExceptionAndComplete(ex);
                }
            }
        }

        private void DrainQueue(Exception ex)
        {
            FailedCommand command;
            lock (queue)
            {
                while (queue.Count != 0)
                {
                    command = queue.Dequeue();
                    command.SetExceptionAndComplete(ex);
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
                    DrainQueue(new Exception("RetryQueue disposed"));
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
