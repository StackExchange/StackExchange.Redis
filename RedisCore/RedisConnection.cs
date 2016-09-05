using Channels;
using Channels.Text.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RedisCore
{

    abstract class ResultParser
    {
        public abstract void Process(ref RawResult response, object source, bool getResult);
    }
    abstract class ResultParser<T> : ResultParser
    {
        public sealed override void Process(ref RawResult response, object source, bool getResult)
        {
            T result = default(T);
            Exception error = null;
            try
            {
                error = Validate(ref response);
                if (error == null && getResult)
                {
                    result = GetResult(ref response);
                }
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
        protected virtual Exception Validate(ref RawResult response)
            => response.Type == ResultType.Error ? new RedisException(response.GetAsciiString()) : null;
        protected abstract T GetResult(ref RawResult response);
    }
    class RedisException : Exception
    {
        public RedisException(string message) : base(message) { }
    }
    interface IMessageBox
    {
        SentMessage GetMessage();
        void Write(ref WritableBuffer output);
    }
    public class RedisConnection : IDisposable
    {
        internal static readonly byte[] CRLF = { (byte)'\r', (byte)'\n' };
        struct PingMessage : IMessage
        {
            public override string ToString() => "PING";

            public void Write(ref WritableBuffer output) => output.Write(ping, 0, ping.Length);

            static readonly byte[] ping = Encoding.ASCII.GetBytes("PING\r\n"),
                PONG = Encoding.ASCII.GetBytes("PONG");

            public int MinimumSize => ping.Length;

            public static readonly ResultParser<bool> Parser = new PingParser();
            sealed class PingParser : ResultParser<bool>
            {
                protected override Exception Validate(ref RawResult response)
                {
                    return base.Validate(ref response) ?? (response.Buffer.Equals(PONG, 0, PONG.Length) ? null
                        : new InvalidOperationException("Unexpected pong response: " + response.GetUtf8String()));
                }
                protected override bool GetResult(ref RawResult response) => true;
            }
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
                private bool getResult;
                object source;
                void IMessageBox.Write(ref WritableBuffer output) => message.Write(ref output);
                SentMessage IMessageBox.GetMessage() => new SentMessage(source, parser, getResult);
                public MessageBox(T message, object source, ResultParser parser, bool getResult)
                {
                    this.message = message;
                    this.source = source;
                    this.parser = parser;
                    this.getResult = getResult;
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
                messages.Add(new MessageBox<TMessage>(message, source, parser, !fireAndForget));
                return source?.Task ?? DefaultTask<TResult>.Instance;
            }
            public void Execute() => connection.WriteBatch(this);
            public Task ExecuteAsync() => connection.WriteBatchAsync(this);

        }
        public struct RedisValue
        {
            internal bool IsString => content is string;

            internal object Content => content;

            private object content;

            private RedisValue(object content)
            {
                this.content = content;
            }
            public override string ToString()
            {
                if (content is byte[]) return Encoding.UTF8.GetString((byte[])content);
                return (string)content;
            }
            public static implicit operator RedisValue(string value) => new RedisValue(value);
            public static implicit operator string(RedisValue value) => value.ToString();
        }
        public RedisValue Echo(RedisValue value)
        {
            var msg = new EchoMessage(value);
            return WriteSync(ref msg, EchoMessage.GetParser(ref value), false);
        }
        public Task<RedisValue> EchoAsync(RedisValue value)
        {
            var msg = new EchoMessage(value);
            return WriteAsync(ref msg, EchoMessage.GetParser(ref value), false);
        }
        struct EchoMessage : IMessage
        {
            private RedisValue value;

            public EchoMessage(RedisValue value) { this.value = value; }

            class ByteArrayParser : ResultParser<RedisValue>
            {
                public static readonly ResultParser<RedisValue> Instance = new ByteArrayParser();
                private ByteArrayParser() { }
                protected override RedisValue GetResult(ref RawResult response)
                {
                    throw new NotImplementedException();
                }
            }
            class StringParser : ResultParser<RedisValue>
            {
                public static readonly ResultParser<RedisValue> Instance = new StringParser();
                private StringParser() { }
                protected override RedisValue GetResult(ref RawResult response)
                    => response.GetUtf8String();

            }
            internal static ResultParser<RedisValue> GetParser(ref RedisValue value)
                => value.IsString ? StringParser.Instance : ByteArrayParser.Instance;

            int IMessage.MinimumSize => 0;
            static readonly byte[] prefix =
                Encoding.ASCII.GetBytes("*2\r\n$4\r\nECHO\r\n"),
                Nil = Encoding.ASCII.GetBytes("$-1\r\n"),
                Empty = Encoding.ASCII.GetBytes("$0\r\n\r\n");

            static void WriteString(ref WritableBuffer output, object content)
            {
                if (content == null)
                {
                    output.Write(Nil, 0, Nil.Length);
                }
                else if (content is byte[])
                {
                    var tmp = (byte[])content;
                    if (tmp.Length == 0)
                    {
                        output.Write(Empty, 0, Empty.Length);
                    }
                    else
                    {
                        WriteLengthPrefix(ref output, (uint)tmp.Length);
                        output.Write(tmp, 0, tmp.Length);
                        output.Write(CRLF, 0, CRLF.Length);
                    }
                }
                else
                {
                    string tmp = (string)content;
                    if (tmp.Length == 0)
                    {
                        output.Write(Empty, 0, Empty.Length);
                    }
                    else
                    {
                        int byteCount = Encoding.UTF8.GetByteCount(tmp);
                        WriteLengthPrefix(ref output, (uint)byteCount);
                        // note: I've checked, and the UTF-8 encoder is faster than the
                        // ASCII encoder, even when all the data is 7-bit, so just use that
                        WritableBufferExtensions.WriteUtf8String(ref output, tmp);
                        output.Write(CRLF, 0, CRLF.Length);
                    }
                }
            }
            void IMessage.Write(ref WritableBuffer output)
            {
                output.Write(prefix, 0, prefix.Length);
                WriteString(ref output, value.Content);
            }

            private static unsafe void WriteLengthPrefix(ref WritableBuffer output, uint len)
            {
                output.Ensure(4); // "$x\r\n" is best case
                *(byte*)output.Memory.UnsafePointer = (byte)'$';
                output.CommitBytes(1);
                WritableBufferExtensions.WriteUInt64(ref output, len);
                output.Write(RedisConnection.CRLF, 0, RedisConnection.CRLF.Length);
            }
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
                    message.Write(ref output);
                }
                var awaiter = output.FlushAsync().GetAwaiter();
                if (!awaiter.IsCompleted)
                    throw new InvalidOperationException("Expectation failed; IO thread threatened");
                awaiter.GetResult();
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
                WriteWithLockSync(ref message, parser, null, fireAndForget);
                return default(TResult);
            }
            
            ResultBox<TResult> source = ResultBox<TResult>.Get();
            TResult result;
            lock(source.SyncLock)
            {
                WriteWithLockSync(ref message, parser, source, fireAndForget);
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
        private void WriteWithLockSync<TMessage, TResult>(ref TMessage message, ResultParser<TResult> parser, object source, bool fireAndForget)
            where TMessage : struct, IMessage
        {
            try
            {
                var expectedReply = new SentMessage(source, parser, !fireAndForget);

                lock (expectedReplies)
                {
                    expectedReplies.Enqueue(expectedReply);
                }
                var output = Output.Alloc(message.MinimumSize);
                message.Write(ref output);
                var awaiter = output.FlushAsync().GetAwaiter();
                if (!awaiter.IsCompleted)
                    throw new InvalidOperationException("Expectation failed; IO thread threatened");
                awaiter.GetResult();
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
            WriteWithLockSync(ref message, parser, source, fireAndForget);
            return source?.Task ?? DefaultTask<TResult>.Instance;
        }
        
        protected IWritableChannel Output => connection.Output;
        protected IReadableChannel Input => connection.Input;
        public void Dispose()
        {
            connection?.Dispose();
            connection = null;
        }
        
        public bool IsConnected => connection != null;
        internal async void Connect(ClientChannelFactory factory, string location)
        {
            try
            {
                connection = await factory.ConnectAsync(location);
                OnConnected();
                ReadLoop();
            }
            catch (Exception ex)
            {
                Program.WriteError(ex);
            }
        }
        private IClientChannel connection;

        private async void ReadLoop()
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
                        next.OnReceived(ref result);
                    }
                    data.Consumed(data.Start, data.End);
                }
                connection?.Dispose();
            }
            catch (Exception ex)
            {
                Program.WriteError(ex);
                connection?.Dispose();
            }
        }
        protected virtual void OnConnected() { }
    }
}
