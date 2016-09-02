using Channels;
using Channels.Networking.Libuv;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RedisCore
{
    abstract class ResultParser
    {
        public abstract void Process(ref RawResult response, object source);
    }
    abstract class ResultParser<T> : ResultParser
    {
        public sealed override void Process(ref RawResult response, object source)
        {
            T result = default(T);
            Exception error = null;
            try
            {
                Validate(ref response);
                result = GetResult(ref response);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            if (source is ResultBox<T>)
            {
                ((ResultBox<T>)source).SetResult(error, result);
            }
            else if (source is TaskCompletionSource<T>)
            {
                if (error != null) ((TaskCompletionSource<T>)source).TrySetException(error);
                else ((TaskCompletionSource<T>)source).TrySetResult(result);
            }
        }
        protected virtual void Validate(ref RawResult response) { }
        protected virtual T GetResult(ref RawResult response) => default(T);
    }
    interface IMessageBox
    {
        SentMessage GetMessage();
        void Write(ref WritableBuffer output);
    }
    public class RedisConnection : IDisposable
    {
        struct PingMessage : IMessage
        {
            public override string ToString() => "PING";

            public void Write(ref WritableBuffer output) => output.Write(ping, 0, ping.Length);

            static readonly byte[] ping = Encoding.ASCII.GetBytes("PING\r\n");

            public int MinimumSize => ping.Length;

            public static readonly ResultParser<bool> Parser = new PingParser();
            sealed class PingParser : ResultParser<bool> { }
        }
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

        private readonly Queue<SentMessage> expectedReplies = new Queue<SentMessage>(1024);
        private static readonly Task done = Task.FromResult(true);
        internal Task PingAsync(bool fireAndForget = false)
        {
            var ping = new PingMessage();
            return WriteAsync(ref ping, PingMessage.Parser, fireAndForget);
        }
        internal void Ping(bool fireAndForget = false)
        {
            var ping = new PingMessage();
            WriteSync(ref ping, PingMessage.Parser, fireAndForget);
        }

        public class Batch
        {

            sealed class MessageBox<T> : IMessageBox where T : struct, IMessage
            {
                private T message;
                private ResultParser parser;
                object source;
                void IMessageBox.Write(ref WritableBuffer output) => message.Write(ref output);
                SentMessage IMessageBox.GetMessage() => new SentMessage(source, parser);
                public MessageBox(T message, object source, ResultParser parser)
                {
                    this.message = message;
                    this.source = source;
                    this.parser = parser;
                }
            }
            private List<IMessageBox> messages = new List<IMessageBox>();
            internal List<IMessageBox> Messages => messages;
            private RedisConnection connection;

            internal Batch(RedisConnection connection)
            {
                this.connection = connection;
            }
            public Task PingAysnc(bool fireAndForget)
            {
                var ping = new PingMessage();
                return Add(ref ping, PingMessage.Parser, fireAndForget);
            } 
            private Task<TResult> Add<TMessage, TResult>(ref TMessage message, ResultParser<TResult> parser, bool fireAndForget)
                where TMessage : struct, IMessage
            {
                TaskCompletionSource<TResult> source = fireAndForget ? null :
                    new TaskCompletionSource<TResult>(TaskContinuationOptions.RunContinuationsAsynchronously);
                var expectedResult = new SentMessage(source, parser);
                messages.Add(new MessageBox<TMessage>(message, source, parser));
                return source?.Task ?? DefaultTask<TResult>.Instance;
            }
            public void Execute() => connection.WriteBatch(this);
            public Task ExecuteAsync() => connection.WriteBatchAsync(this);

        }
        internal void WriteBatch(Batch batch)
        {
            var messages = batch.Messages;
            if (messages.Count == 0) return;
            writeLock.Wait();
            WriteBatchWithLock(batch);
        }
        internal Task WriteBatchAsync(Batch batch)
        {
            if (writeLock.Wait(0))
            {
                WriteBatchWithLock(batch);
                return DefaultTask<bool>.Instance;
            }
            return AquireLockAndWriteBatchAsync(batch);
        }
        private async Task AquireLockAndWriteBatchAsync(Batch batch)
        {
            await writeLock.WaitAsync();
            WriteBatchWithLock(batch);
        }
        private void WriteBatchWithLock(Batch batch)
        {
            try
            {
                var output = Output.Alloc();
                foreach (var message in batch.Messages)
                {
                    lock(expectedReplies)
                    {
                        expectedReplies.Enqueue(message.GetMessage());
                    }
                    Interlocked.Increment(ref outCount);
                    message.Write(ref output);
                }
                var awaiter = output.FlushAsync().GetAwaiter();
                if (!awaiter.IsCompleted)
                    throw new InvalidOperationException("Expectation failed; IO thread threatened");
                awaiter.GetResult();
                Interlocked.Increment(ref flushCount);
            }
            finally
            {
                writeLock.Release();
            }
        }

        public Batch CreateBatch()
        {
            return new Batch(this);
        }
        private TResult WriteSync<TMessage, TResult>(ref TMessage message, ResultParser<TResult> parser, bool fireAndForget)
            where TMessage : struct, IMessage
        {
            writeLock.Wait();
            if (fireAndForget)
            {
                WriteWithLockSync(ref message, parser, null);
                return default(TResult);
            }
            
            ResultBox<TResult> source = ResultBox<TResult>.Get();
            TResult result;
            lock(source.SyncLock)
            {
                WriteWithLockSync(ref message, parser, source);
                result = source.WaitLocked();
            }
            ResultBox<TResult>.Put(source); // oinly gets put back if we successfully exit etc
            return result;
        }
        private Task<TResult> WriteAsync<TMessage, TResult>(ref TMessage message, ResultParser<TResult> parser, bool fireAndForget)
            where TMessage : struct, IMessage
        {
            if (writeLock.Wait(0))
            {
                return WriteWithLockAsync<TMessage, TResult>(ref message, parser, fireAndForget);
            }
            return AquireLockAndWriteAsync<TMessage, TResult>(message, parser, fireAndForget);
        }
        private async Task<TResult> AquireLockAndWriteAsync<TMessage, TResult>(TMessage message, ResultParser<TResult> parser, bool fireAndForget)
            where TMessage : struct, IMessage
        {
            await writeLock.WaitAsync();
            return await WriteWithLockAsync<TMessage, TResult>(ref message, parser, fireAndForget);
        }
        private void WriteWithLockSync<TMessage, TResult>(ref TMessage message, ResultParser<TResult> parser, object source)
            where TMessage : struct, IMessage
        {
            try
            {
                var expectedReply = new SentMessage(source, parser);

                lock (expectedReplies)
                {
                    expectedReplies.Enqueue(expectedReply);
                }
                Interlocked.Increment(ref outCount);
                var output = Output.Alloc(message.MinimumSize);
                message.Write(ref output);
                var awaiter = output.FlushAsync().GetAwaiter();
                if (!awaiter.IsCompleted)
                    throw new InvalidOperationException("Expectation failed; IO thread threatened");
                awaiter.GetResult();
                Interlocked.Increment(ref flushCount);
            }
            finally
            {
                writeLock.Release();
            }
        }
        static class DefaultTask<T>
        {
            public static readonly Task<T> Instance = Task.FromResult(default(T));
        }
        private Task<TResult> WriteWithLockAsync<TMessage, TResult>(ref TMessage message, ResultParser<TResult> parser, bool fireAndForget)
            where TMessage : struct, IMessage
        {
            TaskCompletionSource<TResult> source = fireAndForget ? null :
                new TaskCompletionSource<TResult>(TaskContinuationOptions.RunContinuationsAsynchronously);
            WriteWithLockSync(ref message, parser, source);
            return source?.Task ?? DefaultTask<TResult>.Instance;
        }
        
        protected IWritableChannel Output => connection.Output;
        protected IReadableChannel Input => connection.Input;
        public void Dispose() => Shutdown(ref connection, null);
        private UvTcpConnection connection;

        public bool IsConnected => connection != null;
        public async void Connect(UvThread thread, IPEndPoint endpoint)
        {
            try
            {
                UvTcpClient client = new UvTcpClient(thread, endpoint);
                connection = await client.ConnectAsync();
                OnConnected();
                ReadLoop(connection);
            }
            catch (Exception ex)
            {
                Program.WriteError(ex);
            }
        }
        private static void Shutdown(ref UvTcpConnection connection, Exception error)
        {
            if (connection != null)
            {
                try
                {
                    if (!connection.Input.Completion.IsCompleted) connection.Input.CompleteReading(error);
                    if (!connection.Output.Completion.IsCompleted) connection.Output.CompleteWriting(error);
                }
                catch (Exception ex) { Program.WriteError(ex); }
                connection = null;
            }
        }
        private int outCount, inCount, flushCount;
        public int InCount => Volatile.Read(ref inCount);
        public int OutCount => Volatile.Read(ref outCount);
        public int FlushCount => Volatile.Read(ref flushCount);
        private async void ReadLoop(UvTcpConnection connection)
        {
            try
            {
                while (true)
                {
                    Program.WriteStatus("Awaiting input");
                    var data = await connection.Input.ReadAsync();
                    if (data.IsEmpty && connection.Input.Completion.IsCompleted) break;
                    Program.WriteStatus($"Processing {data.Length} bytes...");
                    RawResult result;
                    SentMessage next;
                    while (RawResult.TryParse(ref data, out result))
                    {
                        lock (expectedReplies)
                        {
                            next = expectedReplies.Dequeue();
                        }
                        Interlocked.Increment(ref inCount);
                        next.OnReceived(ref result);
                    }
                    data.Consumed(data.Start, data.End);
                }
                Shutdown(ref connection, null);
            }
            catch (Exception ex)
            {
                Program.WriteError(ex);
                Shutdown(ref connection, ex);
            }
        }
        protected virtual void OnConnected() { }
    }
}
