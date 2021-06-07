using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace StackExchange.Redis
{
    /// <summary>
    /// options for a message to be retried onconnection retry
    /// </summary>
    public enum MessageRetry
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

    /// <summary>
    /// 
    /// </summary>
    public class ConnectionFailureRequestRetry
    {
        private MessageRetryManager retry;
        private MessageRetry retryFlag;
        /// <summary>
        /// Default to always retry 
        /// </summary>
        /// <param name="mux"></param>
        public ConnectionFailureRequestRetry(ConnectionMultiplexer mux) : this(mux, MessageRetry.AlwaysRetry)
        {
            
        }

        
        /// <summary>
        /// Queues and then retry requests on connection restore
        /// </summary>
        /// <param name="mux"></param>
        /// <param name="retryFlag"></param>
        public ConnectionFailureRequestRetry(ConnectionMultiplexer mux, MessageRetry retryFlag)
        {
            retry = new MessageRetryManager(mux);
            this.retryFlag = retryFlag;
            mux.RequestFailed += Mux_RequestFailed;
            mux.ConnectionRestored += Mux_ConnectionRestored;
        }

        private void Mux_ConnectionRestored(object sender, ConnectionFailedEventArgs e)
        {
            retry.StartRetryQueueProcessor();
        }

        private void Mux_RequestFailed(object sender, Request e)
        {
            if((retryFlag & MessageRetry.AlwaysRetry) != 0)
                retry.PushMessageForRetry(e);
        }
    }

    internal class MessageRetryManager : IDisposable
    {
        private readonly Queue<Request> queue = new Queue<Request>();
        private readonly ConnectionMultiplexer multiplexer;

        internal MessageRetryManager(ConnectionMultiplexer mux)
        {
            this.multiplexer = mux;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool PushMessageForRetry(Request message)
        {
            bool wasEmpty;
            lock (queue)
            {
                int count = queue.Count;
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
            Request message = null;
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
                        //break;
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
                    else if(server != null)
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
                await Task.Delay(ConnectionMultiplexer.MillisecondsPerHeartbeat);
                messageProcessedCount++;
            }
        }

        private void HandleException(Request message, Exception ex)
        {
            var inner = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Failed while retrying on connection restore", ex);
            message.SetExceptionAndComplete(inner, null);
        }

        // I am not using ExceptionFactory.Timeout as it can cause deadlock while trying to lock writtenawaiting response queue for GetHeadMessages
        private RedisTimeoutException GetTimeoutException(Request message)
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
