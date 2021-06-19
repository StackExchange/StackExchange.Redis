using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace StackExchange.Redis
{
    /// <summary>
    /// options for a message to be retried onconnection failure
    /// </summary>
    public enum OneConnectionRestoreRetryOption
    {

        /// <summary>
        /// It's the default option and indicates command is never retried on connection restore
        /// </summary>
        NoRetry,

        /// <summary>
        /// Indicates that on connection failure this operation will be retried if it was not yet sent
        /// </summary>
        RetryIfNotYetSent,

        /// <summary>
        /// Indicates always retry command on connection restore 
        /// </summary>
        AlwaysRetry
    }

    internal class MessageRetryManager : IDisposable
    {
        private readonly Queue<Message> queue = new Queue<Message>();
        private readonly ConnectionMultiplexer multiplexer;

        internal MessageRetryManager(ConnectionMultiplexer mux)
        {
            this.multiplexer = mux;
        }

        internal int RetryQueueCount => queue.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool PushMessageForRetry(Message message)
        {
            bool wasEmpty;
            lock (queue)
            {
                int count = queue.Count;
                if (count >= multiplexer.RawConfig.RetryQueueLengthOnConnectionRestore)
                {
                    return false;
                }
                wasEmpty = count == 0;
                queue.Enqueue(message);

                // if this message is a new message set the writetime
                if (message.GetWriteTime() == 0)
                {
                    message.SetEnqueued(null);
                }
                message.ResetStatusToWaitingToBeSent();
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
            Message message = null;
            var timeout = multiplexer.AsyncTimeoutMilliseconds;
            long messageProcessedCount = 0;
            while (true)
            {
                message = null;

                ServerEndPoint server = null;
                lock (queue)
                {
                    if (queue.Count == 0) break; // all done
                    message = queue.Peek();
                    server = multiplexer.SelectServer(message);
                    if (server == null)
                    {
                        break;
                    }
                    message = queue.Dequeue();
                }
                try
                {
                    if (HasTimedOut(Environment.TickCount,
                                    message.ResultBoxIsAsync ? multiplexer.AsyncTimeoutMilliseconds : multiplexer.TimeoutMilliseconds,
                                    message.GetWriteTime()))
                    {
                        var ex = GetTimeoutException(message);
                        HandleException(message, ex);
                    }
                    else
                    {
                        // reset the noredirect flag in order for retry to follow moved exception
                        message.Flags &= ~CommandFlags.NoRedirect;
                        var result = await server.TryWriteAsync(message).ForAwait();
                        if (result != WriteResult.Success)
                        {
                            var ex = multiplexer.GetException(result, message, server);
                            HandleException(message, ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    HandleException(message, ex);
                }
                messageProcessedCount++;
            }
        }
        

        internal void HandleException(Message message, Exception ex)
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
                    if (!HasTimedOut(now,
                        message.ResultBoxIsAsync ? multiplexer.AsyncTimeoutMilliseconds : multiplexer.TimeoutMilliseconds,
                        message.GetWriteTime()))
                    {
                        break; // not a timeout - we can stop looking
                    }
                    queue.Dequeue();
                    RedisTimeoutException ex = GetTimeoutException(message);
                    HandleException(message, ex);
                }
            }
        }

        // I am not using ExceptionFactory.Timeout as it can cause deadlock while trying to lock writtenawaiting response queue for GetHeadMessages
        internal RedisTimeoutException GetTimeoutException(Message message)
        {
            var sb = new StringBuilder();
            sb.Append("Timeout while waiting for connectionrestore ").Append(message.Command).Append(" (").Append(Format.ToString(multiplexer.TimeoutMilliseconds)).Append("ms)");
            var ex = new RedisTimeoutException(sb.ToString(), message?.Status ?? CommandStatus.Unknown);
            return ex;
        }

        private bool HasTimedOut(int now, int timeoutMilliseconds, int writeTickCount)
        {
            int millisecondsTaken = unchecked(now - writeTickCount);
            return millisecondsTaken >= timeoutMilliseconds;
        }

        private bool disposedValue = false;

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

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
