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
        readonly Queue<IInternalFailedCommand> queue = new Queue<IInternalFailedCommand>();
        int? maxRetryQueueLength;
        bool runRetryLoopAsync;

        internal CommandRetryQueueManager(int? maxRetryQueueLength = null, bool runRetryLoopAsync = true)
        {
            this.maxRetryQueueLength = maxRetryQueueLength;
            this.runRetryLoopAsync = runRetryLoopAsync;
        }

        public int RetryQueueLength => queue.Count;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryHandleFailedCommand(IInternalFailedCommand message)
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
            if (startProcessor)
            {
                if (runRetryLoopAsync)
                {
                    var task = Task.Run(ProcessRetryQueueAsync);
                    if (task.IsFaulted)
                        throw task.Exception;
                }
                else
                {
                    ProcessRetryQueueAsync().Wait();
                }
            }
        }

        private async Task ProcessRetryQueueAsync()
        {
            IInternalFailedCommand message = null;
            while (true)
            {
                message = null;
                Exception failedEndpointex = null;
                lock (queue)
                {
                    if (queue.Count == 0) break; // all done
                    message = queue.Peek();
                    try
                    {
                        if (!message.IsEndpointAvailable())
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        failedEndpointex = ex;
                    }
                    message = queue.Dequeue();
                }

                if (failedEndpointex != null)
                {
                    message.SetExceptionAndComplete(failedEndpointex);
                    continue;
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
                        if (!await message.TryResendAsync())
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
            IInternalFailedCommand command;
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
