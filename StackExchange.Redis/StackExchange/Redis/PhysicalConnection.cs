using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
#if CORE_CLR
using System.Threading.Tasks;
#endif

namespace StackExchange.Redis
{

    internal sealed partial class PhysicalConnection : IDisposable, ISocketCallback
    {

        internal readonly byte[] ChannelPrefix;

        private const int DefaultRedisDatabaseCount = 16;

        private static readonly byte[] Crlf = Encoding.ASCII.GetBytes("\r\n");

#if CORE_CLR
        readonly Action<Task<int>> endRead;
        private static Action<Task<int>> EndReadFactory(PhysicalConnection physical)
        {
            return result =>
            {   // can't capture AsyncState on SocketRead, so we'll do it once per physical instead
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
        }
#else
        static readonly AsyncCallback endRead = result =>
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
#endif

        private static readonly byte[] message = Encoding.UTF8.GetBytes("message"), pmessage = Encoding.UTF8.GetBytes("pmessage");

        static readonly Message[] ReusableChangeDatabaseCommands = Enumerable.Range(0, DefaultRedisDatabaseCount).Select(
            i => Message.Create(i, CommandFlags.FireAndForget, RedisCommand.SELECT)).ToArray();

        private static readonly Message
            ReusableReadOnlyCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.READONLY),
            ReusableReadWriteCommand = Message.Create(-1, CommandFlags.FireAndForget, RedisCommand.READWRITE);

        private static int totalCount;

        private readonly ConnectionType connectionType;

        // things sent to this physical, but not yet received
        private readonly Queue<Message> outstanding = new Queue<Message>();

        readonly string physicalName;

        volatile int currentDatabase = 0;

        ReadMode currentReadMode = ReadMode.NotSpecified;

        int failureReported;

        byte[] ioBuffer = new byte[512];

        int ioBufferBytes = 0;

        int lastWriteTickCount, lastReadTickCount, lastBeatTickCount;
        int firstUnansweredWriteTickCount;

        private Stream netStream, outStream;

        private SocketToken socketToken;

        public PhysicalConnection(PhysicalBridge bridge)
        {
            lastWriteTickCount = lastReadTickCount = Environment.TickCount;
            lastBeatTickCount = 0;
            this.connectionType = bridge.ConnectionType;
            this.Multiplexer = bridge.Multiplexer;
            this.ChannelPrefix = Multiplexer.RawConfig.ChannelPrefix;
            if (this.ChannelPrefix != null && this.ChannelPrefix.Length == 0) this.ChannelPrefix = null; // null tests are easier than null+empty
            var endpoint = bridge.ServerEndPoint.EndPoint;
            physicalName = connectionType + "#" + Interlocked.Increment(ref totalCount) + "@" + Format.ToString(endpoint);
            this.Bridge = bridge;
#if CORE_CLR
            endRead = EndReadFactory(this);
#endif
            OnCreateEcho();
        }

        public void BeginConnect(TextWriter log)
        {
            VolatileWrapper.Write(ref firstUnansweredWriteTickCount, 0);
            var endpoint = this.Bridge.ServerEndPoint.EndPoint;

            Multiplexer.Trace("Connecting...", physicalName);
            this.socketToken = Multiplexer.SocketManager.BeginConnect(endpoint, this, Multiplexer, log);
        }

        private enum ReadMode : byte
        {
            NotSpecified,
            ReadOnly,
            ReadWrite
        }

        public PhysicalBridge Bridge { get; }

        public long LastWriteSecondsAgo => unchecked(Environment.TickCount - VolatileWrapper.Read(ref lastWriteTickCount)) / 1000;

        public ConnectionMultiplexer Multiplexer { get; }

        public long SubscriptionCount { get; set; }

        public bool TransactionActive { get; internal set; }

        public void Dispose()
        {
            if (outStream != null)
            {
                Multiplexer.Trace("Disconnecting...", physicalName);
#if !CORE_CLR
                try { outStream.Close(); } catch { }
#endif
                try { outStream.Dispose(); } catch { }
                outStream = null;
            }
            if (netStream != null)
            {
#if !CORE_CLR
                try { netStream.Close(); } catch { }
#endif
                try { netStream.Dispose(); } catch { }
                netStream = null;
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

        public void Flush()
        {
            var tmp = outStream;
            if (tmp != null)
            {
                tmp.Flush();
                Interlocked.Exchange(ref lastWriteTickCount, Environment.TickCount);
            }
        }
        public void RecordConnectionFailed(ConnectionFailureType failureType, Exception innerException = null, [CallerMemberName] string origin = null)
        {
            SocketManager.ManagerState mgrState = SocketManager.ManagerState.CheckForStaleConnections;
            RecordConnectionFailed(failureType, ref mgrState, innerException, origin);
        }
        public void RecordConnectionFailed(ConnectionFailureType failureType, ref SocketManager.ManagerState managerState, Exception innerException = null, [CallerMemberName] string origin = null)
        {
            IdentifyFailureType(innerException, ref failureType);

            managerState = SocketManager.ManagerState.RecordConnectionFailed_OnInternalError;
            if (failureType == ConnectionFailureType.InternalFailure) OnInternalError(innerException, origin);

            // stop anything new coming in...
            Bridge.Trace("Failed: " + failureType);
            bool isCurrent;
            PhysicalBridge.State oldState;
            int @in = -1, ar = -1;
            managerState = SocketManager.ManagerState.RecordConnectionFailed_OnDisconnected;
            Bridge.OnDisconnected(failureType, this, out isCurrent, out oldState);
            if(oldState == PhysicalBridge.State.ConnectedEstablished)
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
                int now = Environment.TickCount, lastRead = VolatileWrapper.Read(ref lastReadTickCount), lastWrite = VolatileWrapper.Read(ref lastWriteTickCount),
                    lastBeat = VolatileWrapper.Read(ref lastBeatTickCount);
                int unansweredRead = VolatileWrapper.Read(ref firstUnansweredWriteTickCount);

                var exMessage = new StringBuilder(failureType + " on " + Format.ToString(Bridge.ServerEndPoint.EndPoint) + "/" + connectionType);
                var data = new List<Tuple<string, string>>
                {
                    Tuple.Create("FailureType", failureType.ToString()),
                    Tuple.Create("EndPoint", Format.ToString(Bridge.ServerEndPoint.EndPoint))
                };
                Action<string, string, string> add = (lk, sk, v) =>
                {
                    data.Add(Tuple.Create(lk, v));
                    exMessage.Append(", " + sk + ": " + v);
                };

                add("Origin", "origin", origin);
                add("Input-Buffer", "input-buffer", ioBufferBytes.ToString());
                add("Outstanding-Responses", "outstanding", GetSentAwaitingResponseCount().ToString());
                add("Last-Read", "last-read", unchecked(now - lastRead) / 1000 + "s ago");
                add("Last-Write", "last-write", unchecked(now - lastWrite) / 1000 + "s ago");
                add("Unanswered-Write", "unanswered-write", unchecked(now - unansweredRead) / 1000 + "s ago");
                add("Keep-Alive", "keep-alive", Bridge.ServerEndPoint.WriteEverySeconds + "s");
                add("Pending", "pending", Bridge.GetPendingCount().ToString());
                add("Previous-Physical-State", "state", oldState.ToString());

                if(@in >= 0)
                {
                    add("Inbound-Bytes", "in", @in.ToString());
                    add("Active-Readers", "ar", ar.ToString());
                }

                add("Last-Heartbeat", "last-heartbeat", (lastBeat == 0 ? "never" : (unchecked(now - lastBeat)/1000 + "s ago"))+ (Bridge.IsBeating ? " (mid-beat)" : "") );
                add("Last-Multiplexer-Heartbeat", "last-mbeat", Multiplexer.LastHeartbeatSecondsAgo + "s ago");
                add("Last-Global-Heartbeat", "global", ConnectionMultiplexer.LastGlobalHeartbeatSecondsAgo + "s ago");
#if FEATURE_SOCKET_MODE_POLL
                var mgr = Bridge.Multiplexer.SocketManager;
                add("SocketManager-State", "mgr", mgr.State.ToString());
                add("Last-Error", "err", mgr.LastErrorTimeRelative());
#endif

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
                else if (exception is SocketException || exception is IOException) failureType = ConnectionFailureType.SocketFailure;
                else if (exception is EndOfStreamException) failureType = ConnectionFailureType.SocketClosed;
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

                if(message.Command == RedisCommand.SELECT)
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
                WriteUnified(outStream, key.KeyPrefix, (string)val);
            }
            else
            {
                WriteUnified(outStream, key.KeyPrefix, (byte[])val);
            }
        }

        internal void Write(RedisChannel channel)
        {
            WriteUnified(outStream, ChannelPrefix, channel.Value);
        }

        internal void Write(RedisValue value)
        {
            if (value.IsInteger)
            {
                WriteUnified(outStream, (long)value);
            }
            else
            {
                WriteUnified(outStream, (byte[])value);
            }
        }

        internal void WriteHeader(RedisCommand command, int arguments)
        {
            var commandBytes = Multiplexer.CommandMap.GetBytes(command);
            if (commandBytes == null)
            {
                throw ExceptionFactory.CommandDisabled(Multiplexer.IncludeDetailInExceptions, command, null, Bridge.ServerEndPoint);
            }
            outStream.WriteByte((byte)'*');

            // remember the time of the first write that still not followed by read
            Interlocked.CompareExchange(ref firstUnansweredWriteTickCount, Environment.TickCount, 0);

            WriteRaw(outStream, arguments + 1);
            WriteUnified(outStream, commandBytes);
        }

        static void WriteRaw(Stream stream, long value, bool withLengthPrefix = false)
        {
            if (value >= 0 && value <= 9)
            {
                if (withLengthPrefix)
                {
                    stream.WriteByte((byte)'1');
                    stream.Write(Crlf, 0, 2);
                }
                stream.WriteByte((byte)((int)'0' + (int)value));
            }
            else if (value >= 10 && value < 100)
            {
                if (withLengthPrefix)
                {
                    stream.WriteByte((byte)'2');
                    stream.Write(Crlf, 0, 2);
                }
                stream.WriteByte((byte)((int)'0' + (int)value / 10));
                stream.WriteByte((byte)((int)'0' + (int)value % 10));
            }
            else if (value >= 100 && value < 1000)
            {
                int v = (int)value;
                int units = v % 10;
                v /= 10;
                int tens = v % 10, hundreds = v / 10;
                if (withLengthPrefix)
                {
                    stream.WriteByte((byte)'3');
                    stream.Write(Crlf, 0, 2);
                }
                stream.WriteByte((byte)((int)'0' + hundreds));
                stream.WriteByte((byte)((int)'0' + tens));
                stream.WriteByte((byte)((int)'0' + units));
            }
            else if (value < 0 && value >= -9)
            {
                if (withLengthPrefix)
                {
                    stream.WriteByte((byte)'2');
                    stream.Write(Crlf, 0, 2);
                }
                stream.WriteByte((byte)'-');
                stream.WriteByte((byte)((int)'0' - (int)value));
            }
            else if (value <= -10 && value > -100)
            {
                if (withLengthPrefix)
                {
                    stream.WriteByte((byte)'3');
                    stream.Write(Crlf, 0, 2);
                }
                value = -value;
                stream.WriteByte((byte)'-');
                stream.WriteByte((byte)((int)'0' + (int)value / 10));
                stream.WriteByte((byte)((int)'0' + (int)value % 10));
            }
            else
            {
                var bytes = Encoding.ASCII.GetBytes(Format.ToString(value));
                if (withLengthPrefix)
                {
                    WriteRaw(stream, bytes.Length, false);
                }
                stream.Write(bytes, 0, bytes.Length);
            }
            stream.Write(Crlf, 0, 2);
        }

        static void WriteUnified(Stream stream, byte[] value)
        {
            stream.WriteByte((byte)'$');
            if (value == null)
            {
                WriteRaw(stream, -1); // note that not many things like this...
            }
            else
            {
                WriteRaw(stream, value.Length);
                stream.Write(value, 0, value.Length);
                stream.Write(Crlf, 0, 2);
            }
        }

        internal void WriteAsHex(byte[] value)
        {
            var stream = outStream;
            stream.WriteByte((byte)'$');
            if (value == null)
            {
                WriteRaw(stream, -1);
            } else
            {
                WriteRaw(stream, value.Length * 2);
                for(int i = 0; i < value.Length; i++)
                {
                    stream.WriteByte(ToHexNibble(value[i] >> 4));
                    stream.WriteByte(ToHexNibble(value[i] & 15));
                }
                stream.Write(Crlf, 0, 2);
            }
        }
        internal static byte ToHexNibble(int value)
        {
            return value < 10 ? (byte)('0' + value) : (byte)('a' - 10 + value);
        }

        void WriteUnified(Stream stream, byte[] prefix, string value)
        {
            stream.WriteByte((byte)'$');
            if (value == null)
            {
                WriteRaw(stream, -1); // note that not many things like this...
            }
            else
            {
                int encodedLength = Encoding.UTF8.GetByteCount(value);
                if (prefix == null)
                {
                    WriteRaw(stream, encodedLength);
                    WriteRaw(stream, value, encodedLength);
                    stream.Write(Crlf, 0, 2);
                }
                else
                {
                    WriteRaw(stream, prefix.Length + encodedLength);
                    stream.Write(prefix, 0, prefix.Length);
                    WriteRaw(stream, value, encodedLength);
                    stream.Write(Crlf, 0, 2);
                }
            }

        }
        unsafe void WriteRaw(Stream stream, string value, int encodedLength)
        {
            if (encodedLength <= ScratchSize)
            {
                int bytes = Encoding.UTF8.GetBytes(value, 0, value.Length, outScratch, 0);
                stream.Write(outScratch, 0, bytes);
            }
            else
            {
#if !CORE_CLR
                fixed (char* c = value)
                fixed (byte* b = outScratch)
                {
                    int charsRemaining = value.Length, charOffset = 0, bytesWritten;
                    while (charsRemaining > Scratch_CharsPerBlock)
                    {
                        bytesWritten = outEncoder.GetBytes(c + charOffset, Scratch_CharsPerBlock, b, ScratchSize, false);
                        stream.Write(outScratch, 0, bytesWritten);
                        charOffset += Scratch_CharsPerBlock;
                        charsRemaining -= Scratch_CharsPerBlock;
                    }
                    bytesWritten = outEncoder.GetBytes(c + charOffset, charsRemaining, b, ScratchSize, true);
                    if (bytesWritten != 0) stream.Write(outScratch, 0, bytesWritten);
                }
#else
                int charsRemaining = value.Length, charOffset = 0, bytesWritten;
                var valueCharArray = value.ToCharArray();
                while (charsRemaining > Scratch_CharsPerBlock)
                {
                    bytesWritten = outEncoder.GetBytes(valueCharArray, charOffset, Scratch_CharsPerBlock, outScratch, 0, false);
                    stream.Write(outScratch, 0, bytesWritten);
                    charOffset += Scratch_CharsPerBlock;
                    charsRemaining -= Scratch_CharsPerBlock;
                }
                bytesWritten = outEncoder.GetBytes(valueCharArray, charOffset, charsRemaining, outScratch, 0, true);
                if (bytesWritten != 0) stream.Write(outScratch, 0, bytesWritten);
#endif
            }
        }
        const int ScratchSize = 512;
        static readonly int Scratch_CharsPerBlock = ScratchSize / Encoding.UTF8.GetMaxByteCount(1);
        private readonly byte[] outScratch = new byte[ScratchSize];
        private readonly Encoder outEncoder = Encoding.UTF8.GetEncoder();
        static void WriteUnified(Stream stream, byte[] prefix, byte[] value)
        {
            stream.WriteByte((byte)'$');
            if (value == null)
            {
                WriteRaw(stream, -1); // note that not many things like this...
            }
            else if (prefix == null)
            {
                WriteRaw(stream, value.Length);
                stream.Write(value, 0, value.Length);
                stream.Write(Crlf, 0, 2);
            }
            else
            {
                WriteRaw(stream, prefix.Length + value.Length);
                stream.Write(prefix, 0, prefix.Length);
                stream.Write(value, 0, value.Length);
                stream.Write(Crlf, 0, 2);
            }
        }

        static void WriteUnified(Stream stream, long value)
        {
            // note from specification: A client sends to the Redis server a RESP Array consisting of just Bulk Strings.
            // (i.e. we can't just send ":123\r\n", we need to send "$3\r\n123\r\n"
            stream.WriteByte((byte)'$');
            WriteRaw(stream, value, withLengthPrefix: true);
        }

        void BeginReading()
        {
            bool keepReading;
            try
            {
                do
                {
                    keepReading = false;
                    int space = EnsureSpaceAndComputeBytesToRead();
                    Multiplexer.Trace("Beginning async read...", physicalName);
#if CORE_CLR
                    var result = netStream.ReadAsync(ioBuffer, ioBufferBytes, space);
                    switch(result.Status)
                    {
                        case TaskStatus.RanToCompletion:
                        case TaskStatus.Faulted:
                            Multiplexer.Trace("Completed synchronously: processing immediately", physicalName);
                            keepReading = EndReading(result);
                            break;
                        default:
                            result.ContinueWith(endRead);
                            break;
                    }
#else
                    var result = netStream.BeginRead(ioBuffer, ioBufferBytes, space, endRead, this);
                    if (result.CompletedSynchronously)
                    {
                        Multiplexer.Trace("Completed synchronously: processing immediately", physicalName);
                        keepReading = EndReading(result);
                    }
#endif
                } while (keepReading);
            }
#if CORE_CLR
            catch (AggregateException ex)
            {
                throw ex.InnerException;
            }
#endif
            catch (System.IO.IOException ex)
            {
                Multiplexer.Trace("Could not connect: " + ex.Message, physicalName);
            }
        }
        int haveReader;

        internal int GetAvailableInboundBytes(out int activeReaders)
        {
            activeReaders = Interlocked.CompareExchange(ref haveReader, 0, 0);
            return this.socketToken.Available;
        }

        static LocalCertificateSelectionCallback GetAmbientCertificateCallback()
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
            } catch
            { }
            return null;
        }
        SocketMode ISocketCallback.Connected(Stream stream, TextWriter log)
        {
            try
            {
                var socketMode = SocketManager.DefaultSocketMode;

                // disallow connection in some cases
                OnDebugAbort();

                // the order is important here:
                // [network]<==[ssl]<==[logging]<==[buffered]
                var config = Multiplexer.RawConfig;

                if(config.Ssl)
                {
                    Multiplexer.LogLocked(log, "Configuring SSL");
                    var host = config.SslHost;
                    if (string.IsNullOrWhiteSpace(host)) host = Format.ToStringHostOnly(Bridge.ServerEndPoint.EndPoint);

                    var ssl = new SslStream(stream, false, config.CertificateValidationCallback,
                        config.CertificateSelectionCallback ?? GetAmbientCertificateCallback()
#if !__MonoCS__
                        , EncryptionPolicy.RequireEncryption
#endif
                        );
                    try
                    {
#if CORE_CLR
                        ssl.AuthenticateAsClientAsync(host).GetAwaiter().GetResult();
#else
                        ssl.AuthenticateAsClient(host);
#endif
                    }
                    catch (AuthenticationException authexception)
                    {
                        RecordConnectionFailed(ConnectionFailureType.AuthenticationFailure, authexception);
                        Multiplexer.Trace("Encryption failure");
                        return SocketMode.Abort;
                    }
                    stream = ssl;
                    socketMode = SocketMode.Async;
                }
                OnWrapForLogging(ref stream, physicalName);

                int bufferSize = config.WriteBuffer;
                this.netStream = stream;
                this.outStream = bufferSize <= 0 ? stream : new BufferedStream(stream, bufferSize);
                Multiplexer.LogLocked(log, "Connected {0}", Bridge);

                Bridge.OnConnected(this, log);
                return socketMode;
            }
            catch (Exception ex)
            {
                RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex); // includes a bridge.OnDisconnected
                Multiplexer.Trace("Could not connect: " + ex.Message, physicalName);
                return SocketMode.Abort;
            }
        }

#if CORE_CLR
        private bool EndReading(Task<int> result)
        {
            try
            {
                var tmp = netStream;
                int bytesRead = tmp == null ? 0 : result.Result; // note we expect this to be completed
                return ProcessReadBytes(bytesRead);
            }
            catch (Exception ex)
            {
                RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
                return false;
            }
        }
#else
        private bool EndReading(IAsyncResult result)
        {
            try
            {
                int bytesRead = netStream?.EndRead(result) ?? 0;
                return ProcessReadBytes(bytesRead);
            }
            catch (Exception ex)
            {
                RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
                return false;
            }
        }
#endif
        int EnsureSpaceAndComputeBytesToRead()
        {
            int space = ioBuffer.Length - ioBufferBytes;
            if (space == 0)
            {
                Array.Resize(ref ioBuffer, ioBuffer.Length * 2);
                space = ioBuffer.Length - ioBufferBytes;
            }
            return space;
        }

        void ISocketCallback.Error()
        {
            RecordConnectionFailed(ConnectionFailureType.SocketFailure);
        }
        void MatchResult(RawResult result)
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

            Multiplexer.Trace("Response to: " + msg.ToString(), physicalName);
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

        partial void OnWrapForLogging(ref Stream stream, string name);
        private int ProcessBuffer(byte[] underlying, ref int offset, ref int count)
        {
            int messageCount = 0;
            RawResult result;
            do
            {
                int tmpOffset = offset, tmpCount = count;
                // we want TryParseResult to be able to mess with these without consequence
                result = TryParseResult(underlying, ref tmpOffset, ref tmpCount);
                if (result.HasValue)
                {
                    messageCount++;
                    // entire message: update the external counters
                    offset = tmpOffset;
                    count = tmpCount;

                    Multiplexer.Trace(result.ToString(), physicalName);
                    MatchResult(result);
                }
            } while (result.HasValue);
            return messageCount;
        }
        private bool ProcessReadBytes(int bytesRead)
        {
            if (bytesRead <= 0)
            {
                Multiplexer.Trace("EOF", physicalName);
                RecordConnectionFailed(ConnectionFailureType.SocketClosed);
                return false;
            }

            Interlocked.Exchange(ref lastReadTickCount, Environment.TickCount);

            // reset unanswered write timestamp
            VolatileWrapper.Write(ref firstUnansweredWriteTickCount, 0);

            ioBufferBytes += bytesRead;
            Multiplexer.Trace("More bytes available: " + bytesRead + " (" + ioBufferBytes + ")", physicalName);
            int offset = 0, count = ioBufferBytes;
            int handled = ProcessBuffer(ioBuffer, ref offset, ref count);
            Multiplexer.Trace("Processed: " + handled, physicalName);
            if (handled != 0)
            {
                // read stuff
                if (count != 0)
                {
                    Multiplexer.Trace("Copying remaining bytes: " + count, physicalName);
                    //  if anything was left over, we need to copy it to
                    // the start of the buffer so it can be used next time
                    Buffer.BlockCopy(ioBuffer, offset, ioBuffer, 0, count);
                }
                ioBufferBytes = count;
            }
            return true;
        }

        void ISocketCallback.Read()
        {
            Interlocked.Increment(ref haveReader);
            try
            {
                do
                {
                    int space = EnsureSpaceAndComputeBytesToRead();
                    int bytesRead = netStream?.Read(ioBuffer, ioBufferBytes, space) ?? 0;

                    if (!ProcessReadBytes(bytesRead)) return; // EOF
                } while (socketToken.Available != 0);
                Multiplexer.Trace("Buffer exhausted", physicalName);
                // ^^^ note that the socket manager will call us again when there is something to do
            }
            catch (Exception ex)
            {
                RecordConnectionFailed(ConnectionFailureType.InternalFailure, ex);
            }finally
            {
                Interlocked.Decrement(ref haveReader);
            }
        }

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
                long i64;
                if (!itemCount.TryGetInt64(out i64)) throw ExceptionFactory.ConnectionFailure(Multiplexer.IncludeDetailInExceptions, ConnectionFailureType.ProtocolFailure, "Invalid array length", Bridge.ServerEndPoint);
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
                long i64;
                if (!prefix.TryGetInt64(out i64)) throw ExceptionFactory.ConnectionFailure(Multiplexer.IncludeDetailInExceptions, ConnectionFailureType.ProtocolFailure, "Invalid bulk string length", Bridge.ServerEndPoint);
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

        private RawResult ReadLineTerminatedString(ResultType type, byte[] buffer, ref int offset, ref int count)
        {
            int max = offset + count - 2;
            for (int i = offset; i < max; i++)
            {
                if (buffer[i + 1] == '\r' && buffer[i + 2] == '\n')
                {
                    int len = i - offset + 1;
                    var result = new RawResult(type, buffer, offset, len);
                    count -= (len + 2);
                    offset += (len + 2);
                    return result;
                }
            }
            return RawResult.Nil;
        }

        void ISocketCallback.StartReading()
        {
            BeginReading();
        }
        RawResult TryParseResult(byte[] buffer, ref int offset, ref int count)
        {
            if(count == 0) return RawResult.Nil;

            char resultType = (char)buffer[offset++];
            count--;
            switch(resultType)
            {
                case '+': // simple string
                    return ReadLineTerminatedString(ResultType.SimpleString, buffer, ref offset, ref count);
                case '-': // error
                    return ReadLineTerminatedString(ResultType.Error, buffer, ref offset, ref count);
                case ':': // integer
                    return ReadLineTerminatedString(ResultType.Integer, buffer, ref offset, ref count);
                case '$': // bulk string
                    return ReadBulkString(buffer, ref offset, ref count);
                case '*': // array
                    return ReadArray(buffer, ref offset, ref count);
                default:
                    throw new InvalidOperationException("Unexpected response prefix: " + (char)resultType);
            }
        }

        partial void DebugEmulateStaleConnection(ref int firstUnansweredWrite);

        public void CheckForStaleConnection(ref SocketManager.ManagerState managerState)
        {
            int firstUnansweredWrite = VolatileWrapper.Read(ref firstUnansweredWriteTickCount);

            DebugEmulateStaleConnection(ref firstUnansweredWrite);

            int now = Environment.TickCount;

            if (firstUnansweredWrite != 0 && (now - firstUnansweredWrite) > this.Multiplexer.RawConfig.ResponseTimeout)
            {
                this.RecordConnectionFailed(ConnectionFailureType.SocketFailure, ref managerState, origin: "CheckForStaleConnection");
            }
        }
    }


}
