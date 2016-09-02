using Channels;
using Channels.Networking.Libuv;
using Channels.Text.Primitives;
using System;
using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RedisCore
{
    public class Program
    {
        class MyRedisConnection : RedisConnection
        {
            internal const int ToSend = 5000000;
            int remaining = ToSend;
            Stopwatch timer;

            internal void StartTimer()
            {
                timer = Stopwatch.StartNew();
            }
            protected override void OnReceived(ref RawResult result)
            {
                Program.WriteStatus("Received: " + result.ToString());
                int now = Interlocked.Decrement(ref remaining);
                if (now == 0)
                {
                    timer.Stop();
                    Console.WriteLine();
                    Console.WriteLine($"{ToSend} messages received: {timer.ElapsedMilliseconds}ms; {((ToSend * 1000.0) / timer.ElapsedMilliseconds):F0} ops/s");
                }
                else if (now < 0)
                {
                    Console.WriteLine("you got more messages than you expected! something is ill");
                }
                else if ((now %  10000) == 0)
                {
                    Console.Write('.');
                }
            }
            protected override void OnConnected()
            {
                Console.WriteLine("Connected");
            }
            
        }

        public static void Main()
        {
            Thread.CurrentThread.Name = "Main";
            using (var thread = new UvThread())
            using (var conn = new MyRedisConnection())
            {
                conn.Connect(thread, new IPEndPoint(IPAddress.Loopback, 6379));

                Console.WriteLine("Catching breath...");
                Thread.Sleep(1000);
                if(!conn.IsConnected)
                {
                    Console.WriteLine("Failed to connect; is redis running?");
                    return;
                }
                Console.WriteLine($"Sending {MyRedisConnection.ToSend} pings...");
                conn.StartTimer();
                for(int i = 0; i < MyRedisConnection.ToSend; i++) conn.PingAsync();
                Console.WriteLine($"Sent");
                Console.WriteLine("[press return to exit]");
                Console.ReadLine();
            }
            
        }
        [Conditional("DEBUG")]
        internal static void WriteStatus(string message)
        {
            var thread = Thread.CurrentThread;
            Console.WriteLine($"[{thread.ManagedThreadId}:{thread.Name}] {message}");
        }

        internal static void WriteError(Exception ex, [CallerMemberName] string caller = null)
        {
            Console.Error.WriteLine($"{caller} threw {ex.GetType().Name}");
            Console.Error.WriteLine(ex.Message);
        }
    }
    abstract class RedisConnection : IDisposable
    {
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        private static readonly Task done = Task.FromResult(true);
        internal Task PingAsync()
        {
            Program.WriteStatus("ping requested");
            if (writeLock.Wait(0)) {
                WriteWithLock();
                return done;
            }
            return AquireLockAndWriteAsync();
        }
        private async Task AquireLockAndWriteAsync()
        {
            Program.WriteStatus("awaiting lock...");
            await writeLock.WaitAsync();
            Program.WriteStatus("lock obtained");
            WriteWithLock();
        }
        private void WriteWithLock()
        {
            Program.WriteStatus("writing ping");
            var output = Output.Alloc(ping.Length);
            output.Write(ping, 0, ping.Length);
            var awaiter = output.FlushAsync().GetAwaiter();
            if (!awaiter.IsCompleted)
                throw new InvalidOperationException("Expectation failed; IO thread threatened");
            awaiter.GetResult();
            Program.WriteStatus("flushed");
            writeLock.Release();
        }
        static readonly byte[] ping = Encoding.ASCII.GetBytes("PING\r\n");
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
            } catch(Exception ex)
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
                catch(Exception ex) { Program.WriteError(ex); }
                connection = null;
            }
        }

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
                    while(RawResult.TryParse(ref data, out result))
                    {
                        OnReceived(ref result);
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
        // runs on uv thread
        protected virtual void OnReceived(ref RawResult result) { }
        protected virtual void OnConnected() { }
    }
    internal enum ResultType : byte
    {
        None = 0,
        SimpleString = 1,
        Error = 2,
        Integer = 3,
        BulkString = 4,
        MultiBulk = 5
    }

    struct RawResult
    {
        public override string ToString()
        {
            switch (Type)
            {
                case ResultType.SimpleString:
                case ResultType.Integer:
                case ResultType.Error:
                    return $"{Type}: {Buffer.GetAsciiString()}";
                case ResultType.BulkString:
                    return $"{Type}: {Buffer.Length} bytes";
                case ResultType.MultiBulk:
                    return $"{Type}: {Items.Length} items";
                default:
                    return "(unknown)";
            }
        }
        // runs on uv thread
        public static unsafe bool TryParse(ref ReadableBuffer buffer, out RawResult result)
        {
            if(buffer.Length < 3)
            {
                result = default(RawResult);
                return false;
            }
            var span = buffer.FirstSpan;
            byte resultType = span.Array[span.Offset];
            switch (resultType)
            {
                case (byte)'+': // simple string
                    return TryReadLineTerminatedString(ResultType.SimpleString, ref buffer, out result);
                case (byte)'-': // error
                    return TryReadLineTerminatedString(ResultType.Error, ref buffer, out result);
                case (byte)':': // integer
                    return TryReadLineTerminatedString(ResultType.Integer, ref buffer, out result);
                case (byte)'$': // bulk string
                    throw new NotImplementedException();
                    //return ReadBulkString(buffer, ref offset, ref count);
                case (byte)'*': // array
                    throw new NotImplementedException();
                    //return ReadArray(buffer, ref offset, ref count);
                default:
                    throw new InvalidOperationException("Unexpected response prefix: " + (char)resultType);
            }
        }
        private static Vector<byte>
            _vectorCRs = new Vector<byte>((byte)'\r');
        private static bool TryReadLineTerminatedString(ResultType resultType, ref ReadableBuffer buffer, out RawResult result)
        {
            // look for the CR in the CRLF
            var seekBuffer = buffer;
            while (seekBuffer.Length >= 2)
            {
                var cr = seekBuffer.IndexOf(ref _vectorCRs);
                if (cr.IsEnd)
                {
                    result = default(RawResult);
                    return false;
                }
                // confirm that the LF in the CRLF
                var tmp = seekBuffer.Slice(cr).Slice(1);
                if (tmp.Peek() == (byte)'\n')
                {
                    result = new RawResult(resultType, buffer.Slice(1, cr));
                    buffer = tmp.Slice(1); // skip the \n next time
                    return true;
                }
                seekBuffer = tmp;
            }
            result = default(RawResult);
            return false;
        }

        public RawResult(RawResult[] items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            Type = ResultType.MultiBulk;
            Items = items;
            Buffer = default(ReadableBuffer);
        }
        private RawResult(ResultType resultType, ReadableBuffer buffer)
        {
            switch (resultType)
            {
                case ResultType.SimpleString:
                case ResultType.Error:
                case ResultType.Integer:
                case ResultType.BulkString:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(resultType));
            }
            Type = resultType;
            Buffer = buffer;
            Items = null;
        }
        public readonly ResultType Type;
        public
            #if DEBUG
            readonly
            #endif
            ReadableBuffer Buffer;
        private readonly RawResult[] Items;
    }
}
