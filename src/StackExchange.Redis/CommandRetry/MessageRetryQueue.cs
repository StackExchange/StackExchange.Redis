using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal class MessageRetryQueue : IDisposable
    {
        private readonly Queue<Message> _queue = new Queue<Message>();
        private readonly IMessageRetryHelper _messageRetryHelper;
        private readonly int? _maxRetryQueueLength;
        private int _isRunning = 0;
        private BacklogStatus _backlogStatus = BacklogStatus.Inactive;

        internal enum BacklogStatus : byte
        {
            Inactive,
            Activating,
            Starting,
            Started,
            CheckingForWork,
            CheckingForTimeout,
            RecordingTimeout,
            WritingMessage,
            Flushing,
            MarkingInactive,
            RecordingWriteFailure,
            RecordingFault,
            SettingIdle,
            Faulted,
        }

        internal MessageRetryQueue(IMessageRetryHelper messageRetryHelper, int? maxRetryQueueLength = null)
        {
            _maxRetryQueueLength = maxRetryQueueLength;
            _messageRetryHelper = messageRetryHelper;
        }

        public int CurrentRetryQueueLength => _queue.Count;
        public bool IsRunning => _isRunning == 0;
        public string StatusDescription => _backlogStatus.ToString();

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
            if (wasEmpty)
            {
                StartRetryQueueProcessor();
            }
            return true;
        }

        internal void StartRetryQueueProcessor()
        {
            bool startProcessor = false;
            lock (_queue)
            {
                startProcessor = _queue.Count > 0;
            }
            if (!startProcessor)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0)
            {
                _backlogStatus = BacklogStatus.Activating;
                // We're explicitly using a thread here because this needs to not be subject to thread pol starvation.
                // In the problematic case of sync-of-async thread pool starvation from sync awaiters especially,
                // We need to start this queue and flush is out to recover.
                var thread = new Thread(s => ((MessageRetryQueue)s).ProcessRetryQueueAsync().RedisFireAndForget())
                {
                    IsBackground = true, // don't keep process alive
                    Name = "Redis-MessageRetryQueue" // help anyone looking at thread-dumps
                };
                thread.Start(this);
            }
        }

        internal async Task ProcessRetryQueueAsync()
        {
            _backlogStatus = BacklogStatus.Starting;
            // TODO: Look at exclusive write locks

            try
            {
                _backlogStatus = BacklogStatus.Started;
                while (true)
                {
                    _backlogStatus = BacklogStatus.CheckingForWork;
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
                        _backlogStatus = BacklogStatus.CheckingForTimeout;
                        if (_messageRetryHelper.HasTimedOut(message))
                        {
                            _backlogStatus = BacklogStatus.RecordingTimeout;
                            var ex = _messageRetryHelper.GetTimeoutException(message);
                            _messageRetryHelper.SetExceptionAndComplete(message, ex);
                        }
                        else
                        {
                            _backlogStatus = BacklogStatus.WritingMessage;
                            if (!await _messageRetryHelper.TryResendAsync(message))
                            {
                                // this should never happen but just to be safe if connection got dropped again
                                _messageRetryHelper.SetExceptionAndComplete(message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _backlogStatus = BacklogStatus.RecordingFault;
                        _messageRetryHelper.SetExceptionAndComplete(message, ex);
                    }
                }
            }
            catch
            {
                _backlogStatus = BacklogStatus.Faulted;
            }
            finally
            {
                Interlocked.CompareExchange(ref _isRunning, 0, 1);
                _backlogStatus = BacklogStatus.Inactive;
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
