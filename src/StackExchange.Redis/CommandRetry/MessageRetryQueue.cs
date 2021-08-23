using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal class MessageRetryQueue : IDisposable
    {
        private readonly Queue<Message> _queue = new Queue<Message>();
        private readonly IMessageRetryHelper _messageRetryHelper;
        private readonly int? _maxRetryQueueLength;
        private readonly bool _runRetryLoopAsync;

        internal MessageRetryQueue(IMessageRetryHelper messageRetryHelper, int? maxRetryQueueLength = null, bool runRetryLoopAsync = true)
        {
            _maxRetryQueueLength = maxRetryQueueLength;
            _runRetryLoopAsync = runRetryLoopAsync;
            _messageRetryHelper = messageRetryHelper;
        }

        public int CurrentRetryQueueLength => _queue.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryHandleFailedCommand(Message message)
        {
            bool wasEmpty;
            lock (_queue)
            {
                int count = _queue.Count;
                if (_maxRetryQueueLength.HasValue && count >= _maxRetryQueueLength)
                {
                    return false;
                }
                wasEmpty = count == 0;
                _queue.Enqueue(message);
            }
            if (wasEmpty) StartRetryQueueProcessor();
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void StartRetryQueueProcessor()
        {
            bool startProcessor = false;
            lock (_queue)
            {
                startProcessor = _queue.Count > 0;
            }
            if (startProcessor)
            {
                if (_runRetryLoopAsync)
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
            while (true)
            {
                Message message = null;
                Exception failedEndpointException = null;

                lock (_queue)
                {
                    if (_queue.Count == 0) break; // all done
                    message = _queue.Peek();
                    try
                    {
                        if (!_messageRetryHelper.IsEndpointAvailable(message))
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        failedEndpointException = ex;
                    }
                    message = _queue.Dequeue();
                }

                if (failedEndpointException != null)
                {
                    _messageRetryHelper.SetExceptionAndComplete(message, failedEndpointException);
                    continue;
                }

                try
                {
                    if (_messageRetryHelper.HasTimedOut(message))
                    {
                        var ex = _messageRetryHelper.GetTimeoutException(message);
                        _messageRetryHelper.SetExceptionAndComplete(message, ex);
                    }
                    else
                    {
                        if (!await _messageRetryHelper.TryResendAsync(message))
                        {
                            // this should never happen but just to be safe if connection got dropped again
                            _messageRetryHelper.SetExceptionAndComplete(message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _messageRetryHelper.SetExceptionAndComplete(message, ex);
                }
            }
        }

        internal void CheckRetryQueueForTimeouts() // check the head of the backlog queue, consuming anything that looks dead
        {
            lock (_queue)
            {
                while (_queue.Count != 0)
                {
                    var message = _queue.Peek();
                    if (!_messageRetryHelper.HasTimedOut(message))
                    {
                        break; // not a timeout - we can stop looking
                    }
                    _queue.Dequeue();
                    RedisTimeoutException ex = _messageRetryHelper.GetTimeoutException(message);
                    _messageRetryHelper.SetExceptionAndComplete(message, ex);
                }
            }
        }

        private void DrainQueue(Exception ex)
        {
            Message message;
            lock (_queue)
            {
                while (_queue.Count != 0)
                {
                    message = _queue.Dequeue();
                    _messageRetryHelper.SetExceptionAndComplete(message, ex);
                }
            }
        }

        private bool _disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                _disposedValue = true;

                if (disposing)
                {
                    DrainQueue(new Exception($"{nameof(MessageRetryQueue)} disposed"));
                }
            }
        }

        public void Dispose() => Dispose(true);
    }
}
