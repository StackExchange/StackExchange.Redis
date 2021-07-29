using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace StackExchange.Redis
{

    internal class MessageRetryQueue : IDisposable
    { 
        readonly Queue<Message> queue = new Queue<Message>();
        readonly IMessageRetryHelper messageRetryHelper;
        int? maxRetryQueueLength;
        bool runRetryLoopAsync;

        internal MessageRetryQueue(IMessageRetryHelper messageRetryHelper, int? maxRetryQueueLength = null, bool runRetryLoopAsync = true)
        {
            this.maxRetryQueueLength = maxRetryQueueLength;
            this.runRetryLoopAsync = runRetryLoopAsync;
            this.messageRetryHelper = messageRetryHelper;
        }

        public int RetryQueueLength => queue.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryHandleFailedCommand(Message message)
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
            Message message = null;
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
                        if (!messageRetryHelper.IsEndpointAvailable(message))
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
                    messageRetryHelper.SetExceptionAndComplete(message, failedEndpointex);
                    continue;
                }

                try
                {
                    if (messageRetryHelper.HasTimedOut(message))
                    {
                        RedisTimeoutException ex = messageRetryHelper.GetTimeoutException(message);
                        messageRetryHelper.SetExceptionAndComplete(message,ex);
                    }
                    else
                    {
                        if (!await messageRetryHelper.TryResendAsync(message))
                        {
                            // this should never happen but just to be safe if connection got dropped again
                            messageRetryHelper.SetExceptionAndComplete(message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    messageRetryHelper.SetExceptionAndComplete(message, ex);
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
                    if (!messageRetryHelper.HasTimedOut(message))
                    {
                        break; // not a timeout - we can stop looking
                    }
                    queue.Dequeue();
                    RedisTimeoutException ex = messageRetryHelper.GetTimeoutException(message);
                    messageRetryHelper.SetExceptionAndComplete(message,ex);
                }
            }
        }

        private void DrainQueue(Exception ex)
        {
            Message message;
            lock (queue)
            {
                while (queue.Count != 0)
                {
                    message = queue.Dequeue();
                    messageRetryHelper.SetExceptionAndComplete(message, ex);
                }
            }
        }

        private bool disposedValue = false;

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

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
