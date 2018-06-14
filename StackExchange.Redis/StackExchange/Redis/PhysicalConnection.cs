using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal sealed partial class PhysicalConnection : IDisposable, ISocketCallback
    {
        internal readonly byte[] ChannelPrefix;

        private const int DefaultRedisDatabaseCount = 16;

        private static readonly byte[] Crlf = Encoding.ASCII.GetBytes("\r\n");

        private static readonly AsyncCallback endRead = result =>
        {
            PhysicalConnection physical;
            if (result.CompletedSynchronously || (physical = result.AsyncState as PhysicalConnection) == null) return;
            try
            {
                physical.Multiplexer.Trace("Completed asynchronously: processing in callback", physical.physicalName);
                if (physical.EndReading(result)) physical.BeginReading();
            }
            catch (Exception ex)
            {
                physical.RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
            }
        };

        private static readonly byte[] message = Encoding.UTF8.GetBytes("message"), pmessage = Encoding.UTF8.GetBytes("pmessage");

        private static readonly Message[] ReusableChangeDatabaseCommands = Enumerable.Range(0, DefaultRedisDatabaseCount).Select(
            i => Message.Create(i, CommandFlags.FireAndForget, RedisCommand.SELECT)).ToArray();

        private static readonly Message
            ReusableReadOnlyCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.READONLY),
            ReusableReadWriteCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.READWRITE);

        private static int totalCount;

        private readonly ConnectionType connectionType;

        // things sent to this physical, but not yet received
        private readonly Queue<Message> outstanding = new Queue<Message>();

        private readonly string physicalName;

        private volatile int currentDatabase = 0;

        private ReadMode currentReadMode = ReadMode.NotSpecified;

        private int failureReported;

        private int lastWriteTickCount, lastReadTickCount, lastBeatTickCount;
        private int firstUnansweredWriteTickCount;

        IDuplexPipe _ioPipe;

        private SocketToken socketToken;

        public PhysicalConnection(PhysicalBridge bridge)
        {
            lastWriteTickCount = lastReadTickCount = Environment.TickCount;
            lastBeatTickCount = 0;
            connectionType = bridge.ConnectionType;
            Multiplexer = bridge.Multiplexer;
            ChannelPrefix = Multiplexer.RawConfig.ChannelPrefix;
            if (ChannelPrefix?.Length == 0) ChannelPrefix = null; // null tests are easier than null+empty
            var endpoint = bridge.ServerEndPoint.EndPoint;
            physicalName = connectionType + "#" + Interlocked.Increment(ref totalCount) + "@" + Format.ToString(endpoint);
            Bridge = bridge;
            OnCreateEcho();
        }

        public void BeginConnect(TextWriter log)
        {
            Thread.VolatileWrite(ref firstUnansweredWriteTickCount, 0);
            var endpoint = Bridge.ServerEndPoint.EndPoint;

            Multiplexer.Trace("Connecting...", physicalName);
            socketToken = Multiplexer.SocketManager.BeginConnect(endpoint, this, Multiplexer, log);
        }

        private enum ReadMode : byte
        {
            NotSpecified,
            ReadOnly,
            ReadWrite
        }

        public PhysicalBridge Bridge { get; }

        public long LastWriteSecondsAgo => unchecked(Environment.TickCount - Thread.VolatileRead(ref lastWriteTickCount)) / 1000;

        public ConnectionMultiplexer Multiplexer { get; }

        public long SubscriptionCount { get; set; }

        public bool TransactionActive { get; internal set; }

        public void Dispose()
        {
            
            var ioPipe = _ioPipe;
            _ioPipe = null;
            if(ioPipe != null)
            {
                Multiplexer.Trace("Disconnecting...", physicalName);
                try { ioPipe.Input?.CancelPendingRead(); } catch { }
                try { ioPipe.Input?.Complete(); } catch { }
                try { ioPipe.Output?.CancelPendingFlush(); } catch { }
                try { ioPipe.Output?.Complete(); } catch { }
                ioPipe.Output?.Complete();
            }

            if (socketToken.HasValue)
            {
                Multiplexer.SocketManager?.Shutdown(socketToken);
                socketToken = default(SocketToken);
                Multiplexer.Trace("Disconnected", physicalName);
                RecordConnectionFailed(ConnectionFailureType.ConnectionDisposed);
            }
            OnCloseEcho();
        }
        private async Task AwaitedFlush(ValueTask<FlushResult> flush)
        {
            await flush;
            Interlocked.Exchange(ref lastWriteTickCount, Environment.TickCount);
        }
        public Task FlushAsync()
        {
            var tmp = _ioPipe?.Output;
            if (tmp != null)
            {
                var flush = tmp.FlushAsync();
                if (!flush.IsCompletedSuccessfully) return AwaitedFlush(flush);
                Interlocked.Exchange(ref lastWriteTickCount, Environment.TickCount);
            }
            return Task.CompletedTask;
        }

        public void RecordConnectionFailed(ConnectionFailureType failureType, Exception innerException = null, [CallerMemberName] string origin = null)
        {
            var mgrState = SocketManager.ManagerState.CheckForStaleConnections;
            RecordConnectionFailed(failureType, ref mgrState, innerException, origin);
        }

        public void RecordConnectionFailed(ConnectionFailureType failureType, ref SocketManager.ManagerState managerState, Exception innerException = null, [CallerMemberName] string origin = null)
        {
            IdentifyFailureType(innerException, ref failureType);

            managerState = SocketManager.ManagerState.RecordConnectionFailed_OnInternalError;
            if (failureType == ConnectionFailureType.InternalFailure) OnInternalError(innerException, origin);

            // stop anything new coming in...
            Bridge.Trace("Failed: " + failureType);
            int @in = -1, ar = -1;
            managerState = SocketManager.ManagerState.RecordConnectionFailed_OnDisconnected;
            Bridge.OnDisconnected(failureType, this, out bool isCurrent, out PhysicalBridge.State oldState);
            if (oldState == PhysicalBridge.State.ConnectedEstablished)
            {
                try
                {
                    @in = GetAvailableInboundBytes(out ar);
                }
                catch { /* best effort only */ }
            }

            if (isCurrent && Interlocked.CompareExchange(ref failureReported, 1, 0) == 0)
            {
                managerState = SocketManager.ManagerState.RecordConnectionFailed_ReportFailure;
                int now = Environment.TickCount, lastRead = Thread.VolatileRead(ref lastReadTickCount), lastWrite = Thread.VolatileRead(ref lastWriteTickCount),
                    lastBeat = Thread.VolatileRead(ref lastBeatTickCount);
                int unansweredRead = Thread.VolatileRead(ref firstUnansweredWriteTickCount);

                var exMessage = new StringBuilder(failureType.ToString());

                var data = new List<Tuple<string, string>>();
                if (Multiplexer.IncludeDetailInExceptions)
                {
                    exMessage.Append(" on " + Format.ToString(Bridge.ServerEndPoint.EndPoint) + "/" + connectionType);

                    data.Add(Tuple.Create("FailureType", failureType.ToString()));
                    data.Add(Tuple.Create("EndPoint", Format.ToString(Bridge.ServerEndPoint.EndPoint)));

                    void add(string lk, string sk, string v)
                    {
                        data.Add(Tuple.Create(lk, v));
                        exMessage.Append(", ").Append(sk).Append(": ").Append(v);
                    }

                    add("Origin", "origin", origin);
                    // add("Input-Buffer", "input-buffer", _ioPipe.Input);
                    add("Outstanding-Responses", "outstanding", GetSentAwaitingResponseCount().ToString());
                    add("Last-Read", "last-read", (unchecked(now - lastRead) / 1000) + "s ago");
                    add("Last-Write", "last-write", (unchecked(now - lastWrite) / 1000) + "s ago");
                    add("Unanswered-Write", "unanswered-write", (unchecked(now - unansweredRead) / 1000) + "s ago");
                    add("Keep-Alive", "keep-alive", Bridge.ServerEndPoint.WriteEverySeconds + "s");
                    add("Pending", "pending", Bridge.GetPendingCount().ToString());
                    add("Previous-Physical-State", "state", oldState.ToString());

                    if (@in >= 0)
                    {
                        add("Inbound-Bytes", "in", @in.ToString());
                        add("Active-Readers", "ar", ar.ToString());
                    }

                    add("Last-Heartbeat", "last-heartbeat", (lastBeat == 0 ? "never" : ((unchecked(now - lastBeat) / 1000) + "s ago")) + (Bridge.IsBeating ? " (mid-beat)" : ""));
                    add("Last-Multiplexer-Heartbeat", "last-mbeat", Multiplexer.LastHeartbeatSecondsAgo + "s ago");
                    add("Last-Global-Heartbeat", "global", ConnectionMultiplexer.LastGlobalHeartbeatSecondsAgo + "s ago");
#if FEATURE_SOCKET_MODE_POLL
                    var mgr = Bridge.Multiplexer.SocketManager;
                    add("SocketManager-State", "mgr", mgr.State.ToString());
                    add("Last-Error", "err", mgr.LastErrorTimeRelative());
#endif
                }

                var ex = innerException == null
                    ? new RedisConnectionException(failureType, exMessage.ToString())
                    : new RedisConnectionException(failureType, exMessage.ToString(), innerException);

                foreach (var kv in data)
                {
                    ex.Data["Redis-" + kv.Item1] = kv.Item2;
                }

                managerState = SocketManager.ManagerState.RecordConnectionFailed_OnConnectionFailed;
                Bridge.OnConnectionFailed(this, failureType, ex);
            }

            // cleanup
            managerState = SocketManager.ManagerState.RecordConnectionFailed_FailOutstanding;
            lock (outstanding)
            {
                Bridge.Trace(outstanding.Count != 0, "Failing outstanding messages: " + outstanding.Count);
                while (outstanding.Count != 0)
                {
                    var next = outstanding.Dequeue();
                    Bridge.Trace("Failing: " + next);
                    next.Fail(failureType, innerException);
                    Bridge.CompleteSyncOrAsync(next);
                }
            }

            // burn the socket
            managerState = SocketManager.ManagerState.RecordConnectionFailed_ShutdownSocket;
            Multiplexer.SocketManager?.Shutdown(socketToken);
        }

        public override string ToString()
        {
            return physicalName;
        }

        internal static void IdentifyFailureType(Exception exception, ref ConnectionFailureType failureType)
        {
            if (exception != null && failureType == ConnectionFailureType.InternalFailure)
            {
                if (exception is AggregateException) exception = exception.InnerException ?? exception;
                if (exception is AuthenticationException) failureType = ConnectionFailureType.AuthenticationFailure;
                else if (exception is EndOfStreamException) failureType = ConnectionFailureType.SocketClosed;
                else if (exception is SocketException || exception is IOException) failureType = ConnectionFailureType.SocketFailure;
                else if (exception is ObjectDisposedException) failureType = ConnectionFailureType.SocketClosed;
            }
        }

        internal void Enqueue(Message next)
        {
            lock (outstanding)
            {
                outstanding.Enqueue(next);
            }
        }

        internal void GetCounters(ConnectionCounters counters)
        {
            lock (outstanding)
            {
                counters.SentItemsAwaitingResponse = outstanding.Count;
            }
            counters.Subscriptions = SubscriptionCount;
        }

        internal Message GetReadModeCommand(bool isMasterOnly)
        {
            var serverEndpoint = Bridge.ServerEndPoint;
            if (serverEndpoint.RequiresReadMode)
            {
                ReadMode requiredReadMode = isMasterOnly ? ReadMode.ReadWrite : ReadMode.ReadOnly;
                if (requiredReadMode != currentReadMode)
                {
                    currentReadMode = requiredReadMode;
                    switch (requiredReadMode)
                    {
                        case ReadMode.ReadOnly: return ReusableReadOnlyCommand;
                        case ReadMode.ReadWrite: return ReusableReadWriteCommand;
                    }
                }
            }
            else if (currentReadMode == ReadMode.ReadOnly)
            { // we don't need it (because we're not a cluster, or not a slave),
                // but we are in read-only mode; switch to read-write
                currentReadMode = ReadMode.ReadWrite;
                return ReusableReadWriteCommand;
            }
            return null;
        }

        internal Message GetSelectDatabaseCommand(int targetDatabase, Message message)
        {
            if (targetDatabase < 0) return null;
            if (targetDatabase != currentDatabase)
            {
                var serverEndpoint = Bridge.ServerEndPoint;
                int available = serverEndpoint.Databases;

                if (!serverEndpoint.HasDatabases) // only db0 is available on cluster/twemproxy
                {
                    if (targetDatabase != 0)
                    { // should never see this, since the API doesn't allow it; thus not too worried about ExceptionFactory
                        throw new RedisCommandException("Multiple databases are not supported on this server; cannot switch to database: " + targetDatabase);
                    }
                    return null;
                }

                if (message.Command == RedisCommand.SELECT)
                {
                    // this could come from an EVAL/EVALSHA inside a transaction, for example; we'll accept it
                    Bridge.Trace("Switching database: " + targetDatabase);
                    currentDatabase = targetDatabase;
                    return null;
                }

                if (TransactionActive)
                {// should never see this, since the API doesn't allow it; thus not too worried about ExceptionFactory
                    throw new RedisCommandException("Multiple databases inside a transaction are not currently supported: " + targetDatabase);
                }

                if (available != 0 && targetDatabase >= available) // we positively know it is out of range
                {
                    throw ExceptionFactory.DatabaseOutfRange(Multiplexer.IncludeDetailInExceptions, targetDatabase, message, serverEndpoint);
                }
                Bridge.Trace("Switching database: " + targetDatabase);
                currentDatabase = targetDatabase;
                return GetSelectDatabaseCommand(targetDatabase);
            }
            return null;
        }

        internal static Message GetSelectDatabaseCommand(int targetDatabase)
        {
            return targetDatabase < DefaultRedisDatabaseCount
                    ? ReusableChangeDatabaseCommands[targetDatabase] // 0-15 by default
                        : Message.Create(targetDatabase, CommandFlags.FireAndForget, RedisCommand.SELECT);
        }

        internal int GetSentAwaitingResponseCount()
        {
            lock (outstanding)
            {
                return outstanding.Count;
            }
        }

        internal void GetStormLog(StringBuilder sb)
        {
            lock (outstanding)
            {
                if (outstanding.Count == 0) return;
                sb.Append("Sent, awaiting response from server: ").Append(outstanding.Count).AppendLine();
                int total = 0;
                foreach (var item in outstanding)
                {
                    if (++total >= 500) break;
                    item.AppendStormLog(sb);
                    sb.AppendLine();
                }
            }
        }

        internal void OnHeartbeat()
        {
            Interlocked.Exchange(ref lastBeatTickCount, Environment.TickCount);
        }

        internal void OnInternalError(Exception exception, [CallerMemberName] string origin = null)
        {
            Multiplexer.OnInternalError(exception, Bridge.ServerEndPoint.EndPoint, connectionType, origin);
        }

        internal void SetUnknownDatabase()
        { // forces next db-specific command to issue a select
            currentDatabase = -1;
        }

        internal void Write(RedisKey key)
        {
            var val = key.KeyValue;
            if (val is string)
            {
                WriteUnified(_ioPipe.Output, key.KeyPrefix, (string)val);
            }
            else
            {
                WriteUnified(_ioPipe.Output, key.KeyPrefix, (byte[])val);
            }
        }

        internal void Write(RedisChannel channel)
        {
            WriteUnified(_ioPipe.Output, ChannelPrefix, channel.Value);
        }

        internal void Write(RedisValue value)
        {
            if (value.IsInteger)
            {
                WriteUnified(_ioPipe.Output, (long)value);
            }
            else
            {
                WriteUnified(_ioPipe.Output, (byte[])value);
            }
        }

        internal void WriteHeader(RedisCommand command, int arguments)
        {
            var commandBytes = Multiplexer.CommandMap.GetBytes(command);
            if (commandBytes == null)
            {
                throw ExceptionFactory.CommandDisabled(Multiplexer.IncludeDetailInExceptions, command, null, Bridge.ServerEndPoint);
            }
            WriteHeader(commandBytes, arguments);
        }


        internal const int REDIS_MAX_ARGS = 1024 * 1024; // there is a <= 1024*1024 max constraint inside redis itself: https://github.com/antirez/redis/blob/6c60526db91e23fb2d666fc52facc9a11780a2a3/src/networking.c#L1024

        internal void WriteHeader(string command, int arguments)
        {
            if (arguments >= REDIS_MAX_ARGS) // using >= here because we will be adding 1 for the command itself (which is an arg for the purposes of the multi-bulk protocol)
            {
                throw ExceptionFactory.TooManyArgs(Multiplexer.IncludeDetailInExceptions, command, null, Bridge.ServerEndPoint, arguments + 1);
            }
            var commandBytes = Multiplexer.CommandMap.GetBytes(command);
            WriteHeader(commandBytes, arguments);
        }
        private void WriteHeader(byte[] commandBytes, int arguments)
        {


            // remember the time of the first write that still not followed by read
            Interlocked.CompareExchange(ref firstUnansweredWriteTickCount, Environment.TickCount, 0);

            // *{argCount}\r\n      = 3 + MaxInt32TextLen
            // ${cmd-len}\r\n       = 3 + MaxInt32TextLen
            // {cmd}\r\n            = 2 + commandBytes.Length
            var span = _ioPipe.Output.GetSpan(commandBytes.Length + 8 + MaxInt32TextLen + MaxInt32TextLen);
            span[0] = (byte)'*';

            int offset = WriteRaw(span, arguments + 1, offset: 1);

            offset = WriteUnified(span, commandBytes, offset: offset);

            _ioPipe.Output.Advance(offset);
        }
        
        internal const int
            MaxInt32TextLen = 11, // -2,147,483,648 (not including the commas)
            MaxInt64TextLen = 20; // -9,223,372,036,854,775,808 (not including the commas)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int WriteCrlf(Span<byte> span, int offset)
        {
            span[offset++] = (byte)'\r';
            span[offset++] = (byte)'\n';
            return offset;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteCrlf(PipeWriter writer)
        {
            var span = writer.GetSpan(2);
            span[0] = (byte)'\r';
            span[1] = (byte)'\n';
            writer.Advance(2);
        }
        private static int WriteRaw(Span<byte> span, long value, bool withLengthPrefix = false, int offset = 0)
        {
            if (value >= 0 && value <= 9)
            {
                if (withLengthPrefix)
                {
                    span[offset++] = (byte)'1';
                    offset = WriteCrlf(span, offset);
                }
                span[offset++] = (byte)((int)'0' + (int)value);
            }
            else if (value >= 10 && value < 100)
            {
                if (withLengthPrefix)
                {
                    span[offset++] = (byte)'2';
                    offset = WriteCrlf(span, offset);
                }
                span[offset++] = (byte)((int)'0' + ((int)value / 10));
                span[offset++] = (byte)((int)'0' + ((int)value % 10));
            }
            else if (value >= 100 && value < 1000)
            {
                int v = (int)value;
                int units = v % 10;
                v /= 10;
                int tens = v % 10, hundreds = v / 10;
                if (withLengthPrefix)
                {
                    span[offset++] = (byte)'3';
                    offset = WriteCrlf(span, offset);
                }
                span[offset++] = (byte)((int)'0' + hundreds);
                span[offset++] = (byte)((int)'0' + tens);
                span[offset++] = (byte)((int)'0' + units);
            }
            else if (value < 0 && value >= -9)
            {
                if (withLengthPrefix)
                {
                    span[offset++] = (byte)'2';
                    offset = WriteCrlf(span, offset);
                }
                span[offset++] = (byte)'-';
                span[offset++] = (byte)((int)'0' - (int)value);
            }
            else if (value <= -10 && value > -100)
            {
                if (withLengthPrefix)
                {
                    span[offset++] = (byte)'3';
                    offset = WriteCrlf(span, offset);
                }
                value = -value;
                span[offset++] = (byte)'-';
                span[offset++] = (byte)((int)'0' + ((int)value / 10));
                span[offset++] = (byte)((int)'0' + ((int)value % 10));
            }
            else
            {
                // we're going to write it, but *to the wrong place*
                var availableChunk = span.Slice(offset);
                if (!Utf8Formatter.TryFormat(value, availableChunk, out int formattedLength))
                {
                    throw new InvalidOperationException("TryFormat failed");
                }
                if (withLengthPrefix)
                {
                    // now we know how large the prefix is: write the prefix, then write the value
                    if (!Utf8Formatter.TryFormat(formattedLength, availableChunk, out int prefixLength))
                    {
                        throw new InvalidOperationException("TryFormat failed");
                    }
                    offset += prefixLength;
                    offset = WriteCrlf(span, offset);

                    availableChunk = span.Slice(offset);
                    if (!Utf8Formatter.TryFormat(value, availableChunk, out int finalLength))
                    {
                        throw new InvalidOperationException("TryFormat failed");
                    }
                    offset += finalLength;
                    Debug.Assert(finalLength == formattedLength);
                }
                else
                {
                    offset += formattedLength;
                }
            }
            
            return WriteCrlf(span, offset);
        }

        static readonly byte[] NullBulkString = Encoding.ASCII.GetBytes("$-1\r\n"), EmptyBulkString = Encoding.ASCII.GetBytes("$0\r\n\r\n");
        private static void WriteUnified(PipeWriter writer, byte[] value)
        {
            const int MaxQuickSpanSize = 512;

            // ${len}\r\n           = 3 + MaxInt32TextLen
            // {value}\r\n          = 2 + value.Length
            if (value == null)
            {
                // special case:
                writer.Write(NullBulkString);
            }
            else if (value.Length == 0)
            {
                // special case:
                writer.Write(EmptyBulkString);
            }
            else if (value.Length <= MaxQuickSpanSize)
            {
                var span = writer.GetSpan(5 + MaxInt32TextLen + value.Length);
                int bytes = WriteUnified(span, value);
                writer.Advance(bytes);
            }
            else
            {
                // too big to guarantee can do in a single span
                var span = writer.GetSpan(3 + MaxInt32TextLen);
                span[0] = (byte)'$';
                int bytes = WriteRaw(span, value.LongLength, offset: 1);
                writer.Advance(bytes);

                writer.Write(value);

                WriteCrlf(writer);
            }
        }
        private static int WriteUnified(Span<byte> span, byte[] value, int offset = 0)
        {
            span[offset++] = (byte)'$';
            if (value == null)
            {
                offset = WriteRaw(span, -1, offset: offset); // note that not many things like this...
            }
            else
            {
                offset = WriteRaw(span, value.Length, offset: offset);
                new ReadOnlySpan<byte>(value).CopyTo(span.Slice(offset));
                offset = WriteCrlf(span, offset);
            }
            return offset;
        }

        internal void WriteSha1AsHex(byte[] value)
        {
            var writer = _ioPipe.Output;
            if (value == null)
            {
                writer.Write(NullBulkString);
            }
            else if(value.Length == ResultProcessor.ScriptLoadProcessor.Sha1HashLength)
            {
                // $40\r\n              = 5
                // {40 bytes}\r\n       = 42

                var span = writer.GetSpan(47);
                span[0] = (byte)'$';
                span[1] = (byte)'4';
                span[2] = (byte)'0';
                span[3] = (byte)'\r';
                span[4] = (byte)'\n';

                int offset = 5;
                for(int i = 0; i < value.Length; i++)
                {
                    var b = value[i];
                    span[offset++] = ToHexNibble(value[i] >> 4);
                    span[offset++] = ToHexNibble(value[i] & 15);
                }
                span[offset++] = (byte)'\r';
                span[offset++] = (byte)'\n';

                writer.Advance(offset);
            }
            else
            {
                throw new InvalidOperationException("Invalid SHA1 length: " + value.Length);
            }
        }

        internal static byte ToHexNibble(int value)
        {
            return value < 10 ? (byte)('0' + value) : (byte)('a' - 10 + value);
        }

        private void WriteUnified(PipeWriter writer, byte[] prefix, string value)
        {
            if (value == null)
            {
                // special case
                writer.Write(NullBulkString);
            }
            else
            {
                // ${total-len}\r\n         3 + MaxInt32TextLen
                // {prefix}{value}\r\n
                int encodedLength = Encoding.UTF8.GetByteCount(value),
                    prefixLength = prefix == null ? 0 : prefix.Length,
                    totalLength = prefixLength + encodedLength;

                if (totalLength == 0)
                {
                    // special-case
                    writer.Write(EmptyBulkString);
                }
                else
                {
                    var span = writer.GetSpan(3 + MaxInt32TextLen);
                    span[0] = (byte)'$';
                    int bytes = WriteRaw(span, totalLength, offset: 1);
                    writer.Advance(bytes);

                    if (prefixLength != 0) writer.Write(prefix);
                    if (encodedLength != 0) WriteRaw(writer, value, encodedLength);
                    WriteCrlf(writer);
                }
            }
        }

        private unsafe void WriteRaw(PipeWriter writer, string value, int encodedLength)
        {
            const int MaxQuickEncodeSize = 512;

            fixed (char* cPtr = value)
            {
                int totalBytes;
                if (encodedLength <= MaxQuickEncodeSize)
                {
                    // encode directly in one hit
                    var span = writer.GetSpan(encodedLength);
                    fixed (byte* bPtr = &span[0])
                    {
                        totalBytes = Encoding.UTF8.GetBytes(cPtr, value.Length, bPtr, encodedLength);
                    }
                    writer.Advance(encodedLength);
                }
                else
                {
                    // use an encoder in a loop
                    outEncoder.Reset();
                    int charsRemaining = value.Length, charOffset = 0;
                    totalBytes = 0;
                    while (charsRemaining != 0)
                    {
                        // note: at most 4 bytes per UTF8 character, despite what UTF8.GetMaxByteCount says
                        var span = writer.GetSpan(4); // get *some* memory - at least enough for 1 character (but hopefully lots more)
                        int bytesWritten, charsToWrite = span.Length >> 2; // assume worst case, because the API sucks
                        fixed (byte* bPtr = &span[0])
                        {
                            bytesWritten = outEncoder.GetBytes(cPtr + charOffset, charsToWrite, bPtr, span.Length, false);
                        }
                        writer.Advance(bytesWritten);
                        totalBytes += bytesWritten;
                        charOffset += charsToWrite;
                        charsRemaining -= charsRemaining;
                    }
                }
                Debug.Assert(totalBytes == encodedLength);
            }
        }
        private readonly Encoder outEncoder = Encoding.UTF8.GetEncoder();
        private static void WriteUnified(PipeWriter writer, byte[] prefix, byte[] value)
        {
            // ${total-len}\r\n 
            // {prefix}{value}\r\n
            if (prefix == null || prefix.Length == 0 || value == null)
            {   // if no prefix, just use the non-prefixed version;
                // even if prefixed, a null value writes as null, so can use the non-prefixed version
                WriteUnified(writer, value);
            }
            else
            {
                var span = writer.GetSpan(3 + MaxInt32TextLen); // note even with 2 max-len, we're still in same text range
                span[0] = (byte)'$';
                int bytes = WriteRaw(span, prefix.LongLength + value.LongLength, offset: 1);
                writer.Advance(bytes);

                writer.Write(prefix);
                writer.Write(value);

                span = writer.GetSpan(2);
                WriteCrlf(span, 0);
                writer.Advance(2);
            }
        }

        private static void WriteUnified(PipeWriter writer, long value)
        {
            // note from specification: A client sends to the Redis server a RESP Array consisting of just Bulk Strings.
            // (i.e. we can't just send ":123\r\n", we need to send "$3\r\n123\r\n"

            // ${asc-len}\r\n           = 3 + MaxInt32TextLen
            // {asc}\r\n                = MaxInt64TextLen + 2
            var span = writer.GetSpan(5 + MaxInt32TextLen + MaxInt64TextLen);

            span[0] = (byte)'$';
            var bytes = WriteRaw(span, value, withLengthPrefix: true, offset: 1);
            writer.Advance(bytes);
        }
        

        private int haveReader;

        internal int GetAvailableInboundBytes(out int activeReaders)
        {
            activeReaders = Interlocked.CompareExchange(ref haveReader, 0, 0);
            return socketToken.Available;
        }

        private static LocalCertificateSelectionCallback GetAmbientCertificateCallback()
        {
            try
            {
                var pfxPath = Environment.GetEnvironmentVariable("SERedis_ClientCertPfxPath");
                var pfxPassword = Environment.GetEnvironmentVariable("SERedis_ClientCertPassword");
                var pfxStorageFlags = Environment.GetEnvironmentVariable("SERedis_ClientCertStorageFlags");

                X509KeyStorageFlags? flags = null;
                if (!string.IsNullOrEmpty(pfxStorageFlags))
                {
                    flags = Enum.Parse(typeof(X509KeyStorageFlags), pfxStorageFlags) as X509KeyStorageFlags?;
                }

                if (!string.IsNullOrEmpty(pfxPath) && File.Exists(pfxPath))
                {
                    return delegate { return new X509Certificate2(pfxPath, pfxPassword ?? "", flags ?? X509KeyStorageFlags.DefaultKeySet); };
                }
            }
            catch
            { }
            return null;
        }

        async ValueTask<SocketMode> ISocketCallback.ConnectedAsync(IDuplexPipe pipe, TextWriter log)
        {
            try
            {
                var socketMode = SocketManager.DefaultSocketMode;

                // disallow connection in some cases
                OnDebugAbort();

                // the order is important here:
                // [network]<==[ssl]<==[logging]<==[buffered]
                var config = Multiplexer.RawConfig;

                if (config.Ssl)
                {
                    throw new NotImplementedException("TLS");
                    //Multiplexer.LogLocked(log, "Configuring SSL");
                    //var host = config.SslHost;
                    //if (string.IsNullOrWhiteSpace(host)) host = Format.ToStringHostOnly(Bridge.ServerEndPoint.EndPoint);

                    //var ssl = new SslStream(stream, false, config.CertificateValidationCallback,
                    //    config.CertificateSelectionCallback ?? GetAmbientCertificateCallback(),
                    //    EncryptionPolicy.RequireEncryption);
                    //try
                    //{
                    //    ssl.AuthenticateAsClient(host, config.SslProtocols);

                    //    Multiplexer.LogLocked(log, $"SSL connection established successfully using protocol: {ssl.SslProtocol}");
                    //}
                    //catch (AuthenticationException authexception)
                    //{
                    //    RecordConnectionFailed(ConnectionFailureType.AuthenticationFailure, authexception);
                    //    Multiplexer.Trace("Encryption failure");
                    //    return SocketMode.Abort;
                    //}
                    //stream = ssl;
                    //socketMode = SocketMode.Async;
                }
                OnWrapForLogging(ref pipe, physicalName);

                int bufferSize = config.WriteBuffer;

                _ioPipe = pipe;
                Multiplexer.LogLocked(log, "Connected {0}", Bridge);

                await Bridge.OnConnectedAsync(this, log);
                return socketMode;
            }
            catch (Exception ex)
            {
                RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex); // includes a bridge.OnDisconnected
                Multiplexer.Trace("Could not connect: " + ex.Message, physicalName);
                return SocketMode.Abort;
            }
        }

        void ISocketCallback.Error()
        {
            RecordConnectionFailed(ConnectionFailureType.SocketFailure);
        }

        private void MatchResult(RawResult result)
        {
            // check to see if it could be an out-of-band pubsub message
            if (connectionType == ConnectionType.Subscription && result.Type == ResultType.MultiBulk)
            {   // out of band message does not match to a queued message
                var items = result.GetItems();
                if (items.Length >= 3 && items[0].IsEqual(message))
                {
                    // special-case the configuration change broadcasts (we don't keep that in the usual pub/sub registry)
                    var configChanged = Multiplexer.ConfigurationChangedChannel;
                    if (configChanged != null && items[1].IsEqual(configChanged))
                    {
                        EndPoint blame = null;
                        try
                        {
                            if (!items[2].IsEqual(RedisLiterals.ByteWildcard))
                            {
                                blame = Format.TryParseEndPoint(items[2].GetString());
                            }
                        }
                        catch { /* no biggie */ }
                        Multiplexer.Trace("Configuration changed: " + Format.ToString(blame), physicalName);
                        Multiplexer.ReconfigureIfNeeded(blame, true, "broadcast");
                    }

                    // invoke the handlers
                    var channel = items[1].AsRedisChannel(ChannelPrefix, RedisChannel.PatternMode.Literal);
                    Multiplexer.Trace("MESSAGE: " + channel, physicalName);
                    if (!channel.IsNull)
                    {
                        Multiplexer.OnMessage(channel, channel, items[2].AsRedisValue());
                    }
                    return; // AND STOP PROCESSING!
                }
                else if (items.Length >= 4 && items[0].IsEqual(pmessage))
                {
                    var channel = items[2].AsRedisChannel(ChannelPrefix, RedisChannel.PatternMode.Literal);
                    Multiplexer.Trace("PMESSAGE: " + channel, physicalName);
                    if (!channel.IsNull)
                    {
                        var sub = items[1].AsRedisChannel(ChannelPrefix, RedisChannel.PatternMode.Pattern);
                        Multiplexer.OnMessage(sub, channel, items[3].AsRedisValue());
                    }
                    return; // AND STOP PROCESSING!
                }

                // if it didn't look like "[p]message", then we still need to process the pending queue
            }
            Multiplexer.Trace("Matching result...", physicalName);
            Message msg;
            lock (outstanding)
            {
                Multiplexer.Trace(outstanding.Count == 0, "Nothing to respond to!", physicalName);
                msg = outstanding.Dequeue();
            }

            Multiplexer.Trace("Response to: " + msg, physicalName);
            if (msg.ComputeResult(this, result))
            {
                Bridge.CompleteSyncOrAsync(msg);
            }
        }

        partial void OnCloseEcho();

        partial void OnCreateEcho();
        partial void OnDebugAbort();
        void ISocketCallback.OnHeartbeat()
        {
            try
            {
                Bridge.OnHeartbeat(true); // all the fun code is here
            }
            catch (Exception ex)
            {
                OnInternalError(ex);
            }
        }

        partial void OnWrapForLogging(ref IDuplexPipe pipe, string name);

        private async Task ReadFromPipe()
        {
            try
            {
                while (true)
                {
                    var input = _ioPipe.Input;
                    var readResult = await input.ReadAsync();
                    if (readResult.IsCompleted && readResult.Buffer.IsEmpty)
                    {
                        break; // we're all done
                    }
                    var buffer = readResult.Buffer;

                    int handled = ProcessBuffer(ref buffer);
                    Multiplexer.Trace($"Processed {handled} messages", physicalName);
                    input.AdvanceTo(buffer.Start, buffer.End);
                }
                Multiplexer.Trace("EOF", physicalName);
                RecordConnectionFailed(ConnectionFailureType.SocketClosed);
            }
            catch (Exception ex)
            {
                Multiplexer.Trace("Faulted", physicalName);
                RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
            }
        }
        private int ProcessBuffer(ref ReadOnlySequence<byte> buffer)
        {
            int messageCount = 0;
            RawResult result;
            while (!buffer.IsEmpty)
            {
                // we want TryParseResult to be able to mess with these without consequence
                var snapshot = buffer;
                result = TryParseResult(ref buffer);
                if (result.HasValue)
                {
                    messageCount++;

                    Multiplexer.Trace(result.ToString(), physicalName);
                    MatchResult(result);
                }
                else
                {
                    buffer = snapshot; // just in case TryParseResult toyed with it
                    break;
                }
            }
            return messageCount;
        }
        //void ISocketCallback.Read()
        //{
        //    Interlocked.Increment(ref haveReader);
        //    try
        //    {
        //        do
        //        {
        //            int space = EnsureSpaceAndComputeBytesToRead();
        //            int bytesRead = netStream?.Read(ioBuffer, ioBufferBytes, space) ?? 0;

        //            if (!ProcessReadBytes(bytesRead)) return; // EOF
        //        } while (socketToken.Available != 0);
        //        Multiplexer.Trace("Buffer exhausted", physicalName);
        //        // ^^^ note that the socket manager will call us again when there is something to do
        //    }
        //    catch (Exception ex)
        //    {
        //        RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
        //    }
        //    finally
        //    {
        //        Interlocked.Decrement(ref haveReader);
        //    }
        //}

        bool ISocketCallback.IsDataAvailable
        {
            get
            {
                try { return socketToken.Available > 0; }
                catch { return false; }
            }
        }

        private RawResult ReadArray(byte[] buffer, ref int offset, ref int count)
        {
            var itemCount = ReadLineTerminatedString(ResultType.Integer, buffer, ref offset, ref count);
            if (itemCount.HasValue)
            {
                if (!itemCount.TryGetInt64(out long i64)) throw ExceptionFactory.ConnectionFailure(Multiplexer.IncludeDetailInExceptions, ConnectionFailureType.ProtocolFailure, "Invalid array length", Bridge.ServerEndPoint);
                int itemCountActual = checked((int)i64);

                if (itemCountActual < 0)
                {
                    //for null response by command like EXEC, RESP array: *-1\r\n
                    return new RawResult(ResultType.SimpleString, null, 0, 0);
                }
                else if (itemCountActual == 0)
                {
                    //for zero array response by command like SCAN, Resp array: *0\r\n 
                    return RawResult.EmptyArray;
                }

                var arr = new RawResult[itemCountActual];
                for (int i = 0; i < itemCountActual; i++)
                {
                    if (!(arr[i] = TryParseResult(buffer, ref offset, ref count)).HasValue)
                        return RawResult.Nil;
                }
                return new RawResult(arr);
            }
            return RawResult.Nil;
        }

        private RawResult ReadBulkString(byte[] buffer, ref int offset, ref int count)
        {
            var prefix = ReadLineTerminatedString(ResultType.Integer, buffer, ref offset, ref count);
            if (prefix.HasValue)
            {
                if (!prefix.TryGetInt64(out long i64)) throw ExceptionFactory.ConnectionFailure(Multiplexer.IncludeDetailInExceptions, ConnectionFailureType.ProtocolFailure, "Invalid bulk string length", Bridge.ServerEndPoint);
                int bodySize = checked((int)i64);
                if (bodySize < 0)
                {
                    return new RawResult(ResultType.BulkString, null, 0, 0);
                }
                else if (count >= bodySize + 2)
                {
                    if (buffer[offset + bodySize] != '\r' || buffer[offset + bodySize + 1] != '\n')
                    {
                        throw ExceptionFactory.ConnectionFailure(Multiplexer.IncludeDetailInExceptions, ConnectionFailureType.ProtocolFailure, "Invalid bulk string terminator", Bridge.ServerEndPoint);
                    }
                    var result = new RawResult(ResultType.BulkString, buffer, offset, bodySize);
                    offset += bodySize + 2;
                    count -= bodySize + 2;
                    return result;
                }
            }
            return RawResult.Nil;
        }

        private RawResult ReadLineTerminatedString(ResultType type, ref ReadOnlySequence<byte> buffer, ref BufferReader reader)
        {
            int crlf = BufferReader.FindNextCrLf(reader);

            if (crlf < 0) return RawResult.Nil;


            var inner = buffer.Slice(reader.TotalConsumed, crlf);
            reader.Consume(crlf + 2);

            var result = new RawResult(type, inner);
        }

        void ISocketCallback.StartReading()
        {
            BeginReading();
        }

        private RawResult TryParseResult(ref ReadOnlySequence<byte> buffer)
        {
            if (buffer.IsEmpty) return RawResult.Nil;

            // so we have *at least* one byte
            char resultType = (char)buffer.First.Span[0];
            var reader = new BufferReader(buffer);
            reader.Consume(1);

            RawResult result;
            switch (resultType)
            {
                case '+': // simple string
                    result = ReadLineTerminatedString(ResultType.SimpleString, ref buffer, ref reader);
                    break;
                case '-': // error
                    result = ReadLineTerminatedString(ResultType.Error, ref buffer, ref reader);
                    break;
                case ':': // integer
                    result = ReadLineTerminatedString(ResultType.Integer, ref buffer, ref reader);
                    break;
                case '$': // bulk string
                    result = ReadBulkString(buffer, ref reader);
                    break;
                case '*': // array
                    result = ReadArray(buffer, ref reader);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected response prefix: " + (char)resultType);
            }
            buffer = buffer.Slice(reader.TotalConsumed);
            return result;
        }

        ref struct BufferReader
        {
            private ReadOnlySequence<byte>.Enumerator _iterator;
            private ReadOnlySpan<byte> _current;

            public ReadOnlySpan<byte> Span => _current;
            public int OffsetThisSpan { get; private set; }
            public int TotalConsumed { get; private set; }
            public int RemainingThisSpan { get; private set; }
            bool FetchNext()
            {
                if(_iterator.MoveNext())
                {
                    _current = _iterator.Current.Span;
                    OffsetThisSpan = 0;
                    RemainingThisSpan = _current.Length;
                    return true;
                }
                else
                {
                    OffsetThisSpan = RemainingThisSpan = 0;
                    return false;
                }
            }
            public BufferReader(ReadOnlySequence<byte> buffer)
            {
                _iterator = buffer.GetEnumerator();
                _current = default;
                OffsetThisSpan = RemainingThisSpan = TotalConsumed = 0;

                FetchNext();
            }
            static readonly byte[] CRLF = { (byte)'\r', (byte)'\n' };
            internal static int FindNextCrLf(BufferReader reader) // very deliberately not ref; want snapshot
            {
                // is it in the current span? (we need to handle the offsets differently if so)
                
                int totalSkipped = 0;
                bool haveTrailingCR = false;
                do
                {
                    var span = reader.Span;
                    if (reader.OffsetThisSpan != 0) span = span.Slice(reader.OffsetThisSpan);

                    if (span.IsEmpty)
                    {
                        haveTrailingCR = false;
                    }
                    else
                    {
                        if (haveTrailingCR && span[0] == '\n') return totalSkipped - 1;

                        int found = span.IndexOf(CRLF);
                        if (found >= 0) return totalSkipped + found;

                        haveTrailingCR = span[span.Length - 1] == '\r';
                        totalSkipped += span.Length;
                    }
                }
                while (reader.FetchNext());
                return -1;
            }
            public void Consume(int count)
            {
                if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
                while(count != 0)
                {
                    if(count < RemainingThisSpan)
                    {
                        // consume part of this span
                        TotalConsumed += count;                        
                        RemainingThisSpan -= count;
                        OffsetThisSpan += count;                        
                        count = 0;
                    }
                    else
                    {
                        // consume all of this span
                        TotalConsumed += RemainingThisSpan;
                        count -= RemainingThisSpan;
                        if (!FetchNext()) throw new EndOfStreamException();
                    }
                }
            }
        }

        partial void DebugEmulateStaleConnection(ref int firstUnansweredWrite);

        public void CheckForStaleConnection(ref SocketManager.ManagerState state)
        {
            int firstUnansweredWrite = Thread.VolatileRead(ref firstUnansweredWriteTickCount);

            DebugEmulateStaleConnection(ref firstUnansweredWrite);

            int now = Environment.TickCount;

            if (firstUnansweredWrite != 0 && (now - firstUnansweredWrite) > Multiplexer.RawConfig.ResponseTimeout)
            {
                RecordConnectionFailed(ConnectionFailureType.SocketFailure, ref state, origin: "CheckForStaleConnection");
            }
        }
    }
}
