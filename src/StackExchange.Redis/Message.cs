using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Profiling;
using static StackExchange.Redis.ConnectionMultiplexer;

namespace StackExchange.Redis
{
    internal sealed class LoggingMessage : Message
    {
        public readonly LogProxy log;
        private readonly Message tail;

        public static Message Create(LogProxy log, Message tail)
        {
            return log == null ? tail : new LoggingMessage(log, tail);
        }

        private LoggingMessage(LogProxy log, Message tail) : base(tail.Db, tail.Flags, tail.Command)
        {
            this.log = log;
            this.tail = tail;
            Flags = tail.Flags;
        }

        public override string CommandAndKey => tail.CommandAndKey;

        public override void AppendStormLog(StringBuilder sb) => tail.AppendStormLog(sb);

        public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => tail.GetHashSlot(serverSelectionStrategy);

        protected override void WriteImpl(PhysicalConnection physical)
        {
            try
            {
                var bridge = physical.BridgeCouldBeNull;
                log?.WriteLine($"Writing to {bridge}: {tail.CommandAndKey}");
            }
            catch { }
            tail.WriteTo(physical);
        }
        public override int ArgCount => tail.ArgCount;

        public LogProxy Log => log;
    }

    internal abstract class Message : ICompletable
    {
        public readonly int Db;

#if DEBUG
        internal int QueuePosition { get; private set; }
        internal PhysicalConnection.WriteStatus ConnectionWriteState { get; private set; }
#endif
        [Conditional("DEBUG")]
        internal void SetBacklogState(int position, PhysicalConnection physical)
        {
#if DEBUG
            QueuePosition = position;
            ConnectionWriteState = physical?.GetWriteStatus() ?? PhysicalConnection.WriteStatus.NA;
#endif
        }

        internal const CommandFlags InternalCallFlag = (CommandFlags)128;

        protected RedisCommand command;

        private const CommandFlags AskingFlag = (CommandFlags)32,
                                   ScriptUnavailableFlag = (CommandFlags)256,
                                   NeedsAsyncTimeoutCheckFlag = (CommandFlags)1024;

        private const CommandFlags MaskMasterServerPreference = CommandFlags.DemandMaster
                                                              | CommandFlags.DemandSlave
                                                              | CommandFlags.PreferMaster
                                                              | CommandFlags.PreferSlave;

        private const CommandFlags UserSelectableFlags = CommandFlags.None
                                                       | CommandFlags.DemandMaster
                                                       | CommandFlags.DemandSlave
                                                       | CommandFlags.PreferMaster
                                                       | CommandFlags.PreferSlave
#pragma warning disable CS0618
                                                       | CommandFlags.HighPriority
#pragma warning restore CS0618
                                                       | CommandFlags.FireAndForget
                                                       | CommandFlags.NoRedirect
                                                       | CommandFlags.NoScriptCache;
        private IResultBox resultBox;

        private ResultProcessor resultProcessor;

        // All for profiling purposes
        private ProfiledCommand performance;
        internal DateTime createdDateTime;
        internal long createdTimestamp;

        protected Message(int db, CommandFlags flags, RedisCommand command)
        {
            bool dbNeeded = RequiresDatabase(command);
            if (command == RedisCommand.UNKNOWN)
            {
                // all bets are off here
            }
            else if (db < 0)
            {
                if (dbNeeded)
                {
                    throw ExceptionFactory.DatabaseRequired(false, command);
                }
            }
            else
            {
                if (!dbNeeded)
                {
                    throw ExceptionFactory.DatabaseNotRequired(false, command);
                }
            }

            bool masterOnly = IsMasterOnly(command);
            Db = db;
            this.command = command;
            Flags = flags & UserSelectableFlags;
            if (masterOnly) SetMasterOnly();

            createdDateTime = DateTime.UtcNow;
            createdTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            Status = CommandStatus.WaitingToBeSent;
        }

        internal void SetMasterOnly()
        {
            switch (GetMasterSlaveFlags(Flags))
            {
                case CommandFlags.DemandSlave:
                    throw ExceptionFactory.MasterOnly(false, command, null, null);
                case CommandFlags.DemandMaster:
                    // already fine as-is
                    break;
                case CommandFlags.PreferMaster:
                case CommandFlags.PreferSlave:
                default: // we will run this on the master, then
                    Flags = SetMasterSlaveFlags(Flags, CommandFlags.DemandMaster);
                    break;
            }
        }

        internal void SetProfileStorage(ProfiledCommand storage)
        {
            performance = storage;
            performance.SetMessage(this);
        }

        internal void PrepareToResend(ServerEndPoint resendTo, bool isMoved)
        {
            if (performance == null) return;

            var oldPerformance = performance;

            oldPerformance.SetCompleted();
            performance = null;

            createdDateTime = DateTime.UtcNow;
            createdTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            performance = ProfiledCommand.NewAttachedToSameContext(oldPerformance, resendTo, isMoved);
            performance.SetMessage(this);
            Status = CommandStatus.WaitingToBeSent;
        }

        public CommandFlags Flags { get; internal set; }
        internal CommandStatus Status { get; private set; }
        public RedisCommand Command => command;
        public virtual string CommandAndKey => Command.ToString();

        /// <summary>
        /// Things with the potential to cause harm, or to reveal configuration information
        /// </summary>
        public bool IsAdmin
        {
            get
            {
                switch (Command)
                {
                    case RedisCommand.BGREWRITEAOF:
                    case RedisCommand.BGSAVE:
                    case RedisCommand.CLIENT:
                    case RedisCommand.CLUSTER:
                    case RedisCommand.CONFIG:
                    case RedisCommand.DEBUG:
                    case RedisCommand.FLUSHALL:
                    case RedisCommand.FLUSHDB:
                    case RedisCommand.INFO:
                    case RedisCommand.KEYS:
                    case RedisCommand.MONITOR:
                    case RedisCommand.SAVE:
                    case RedisCommand.SHUTDOWN:
                    case RedisCommand.SLAVEOF:
                    case RedisCommand.SLOWLOG:
                    case RedisCommand.SWAPDB:
                    case RedisCommand.SYNC:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public bool IsAsking => (Flags & AskingFlag) != 0;

        internal bool IsScriptUnavailable => (Flags & ScriptUnavailableFlag) != 0;

        internal void SetScriptUnavailable()
        {
            Flags |= ScriptUnavailableFlag;
        }

        public bool IsFireAndForget => (Flags & CommandFlags.FireAndForget) != 0;
        public bool IsInternalCall => (Flags & InternalCallFlag) != 0;

        public IResultBox ResultBox => resultBox;

        public abstract int ArgCount { get; } // note: over-estimate if necessary

        public static Message Create(int db, CommandFlags flags, RedisCommand command)
        {
            if (command == RedisCommand.SELECT)
                return new SelectMessage(db, flags);
            return new CommandMessage(db, flags, command);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key)
        {
            return new CommandKeyMessage(db, flags, command, key);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, in RedisKey key1)
        {
            return new CommandKeyKeyMessage(db, flags, command, key0, key1);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, in RedisKey key1, in RedisValue value)
        {
            return new CommandKeyKeyValueMessage(db, flags, command, key0, key1, value);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, in RedisKey key1, in RedisKey key2)
        {
            return new CommandKeyKeyKeyMessage(db, flags, command, key0, key1, key2);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value)
        {
            return new CommandValueMessage(db, flags, command, value);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value)
        {
            return new CommandKeyValueMessage(db, flags, command, key, value);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisChannel channel)
        {
            return new CommandChannelMessage(db, flags, command, channel);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisChannel channel, in RedisValue value)
        {
            return new CommandChannelValueMessage(db, flags, command, channel, value);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value, in RedisChannel channel)
        {
            return new CommandValueChannelMessage(db, flags, command, value, channel);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1)
        {
            return new CommandKeyValueValueMessage(db, flags, command, key, value0, value1);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1, in RedisValue value2)
        {
            return new CommandKeyValueValueValueMessage(db, flags, command, key, value0, value1, value2);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, GeoEntry[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(values));
            }
            if (values.Length == 1)
            {
                var value = values[0];
                return Message.Create(db, flags, command, key, value.Longitude, value.Latitude, value.Member);
            }
            var arr = new RedisValue[3 * values.Length];
            int index = 0;
            foreach (var value in values)
            {
                arr[index++] = value.Longitude;
                arr[index++] = value.Latitude;
                arr[index++] = value.Member;
            }
            return new CommandKeyValuesMessage(db, flags, command, key, arr);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3)
        {
            return new CommandKeyValueValueValueValueMessage(db, flags, command, key, value0, value1, value2, value3);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value0, in RedisValue value1)
        {
            return new CommandValueValueMessage(db, flags, command, value0, value1);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value, in RedisKey key)
        {
            return new CommandValueKeyMessage(db, flags, command, value, key);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value0, in RedisValue value1, in RedisValue value2)
        {
            return new CommandValueValueValueMessage(db, flags, command, value0, value1, value2);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4)
        {
            return new CommandValueValueValueValueValueMessage(db, flags, command, value0, value1, value2, value3, value4);
        }

        public static Message CreateInSlot(int db, int slot, CommandFlags flags, RedisCommand command, RedisValue[] values)
        {
            return new CommandSlotValuesMessage(db, slot, flags, command, values);
        }

        public static bool IsMasterOnly(RedisCommand command)
        {
            switch (command)
            {
                case RedisCommand.APPEND:
                case RedisCommand.BITOP:
                case RedisCommand.BLPOP:
                case RedisCommand.BRPOP:
                case RedisCommand.BRPOPLPUSH:
                case RedisCommand.DECR:
                case RedisCommand.DECRBY:
                case RedisCommand.DEL:
                case RedisCommand.EXPIRE:
                case RedisCommand.EXPIREAT:
                case RedisCommand.FLUSHALL:
                case RedisCommand.FLUSHDB:
                case RedisCommand.GETSET:
                case RedisCommand.HDEL:
                case RedisCommand.HINCRBY:
                case RedisCommand.HINCRBYFLOAT:
                case RedisCommand.HMSET:
                case RedisCommand.HSET:
                case RedisCommand.HSETNX:
                case RedisCommand.INCR:
                case RedisCommand.INCRBY:
                case RedisCommand.INCRBYFLOAT:
                case RedisCommand.LINSERT:
                case RedisCommand.LPOP:
                case RedisCommand.LPUSH:
                case RedisCommand.LPUSHX:
                case RedisCommand.LREM:
                case RedisCommand.LSET:
                case RedisCommand.LTRIM:
                case RedisCommand.MIGRATE:
                case RedisCommand.MOVE:
                case RedisCommand.MSET:
                case RedisCommand.MSETNX:
                case RedisCommand.PERSIST:
                case RedisCommand.PEXPIRE:
                case RedisCommand.PEXPIREAT:
                case RedisCommand.PFADD:
                case RedisCommand.PFMERGE:
                case RedisCommand.PSETEX:
                case RedisCommand.RENAME:
                case RedisCommand.RENAMENX:
                case RedisCommand.RESTORE:
                case RedisCommand.RPOP:
                case RedisCommand.RPOPLPUSH:
                case RedisCommand.RPUSH:
                case RedisCommand.RPUSHX:
                case RedisCommand.SADD:
                case RedisCommand.SDIFFSTORE:
                case RedisCommand.SET:
                case RedisCommand.SETBIT:
                case RedisCommand.SETEX:
                case RedisCommand.SETNX:
                case RedisCommand.SETRANGE:
                case RedisCommand.SINTERSTORE:
                case RedisCommand.SMOVE:
                case RedisCommand.SPOP:
                case RedisCommand.SREM:
                case RedisCommand.SUNIONSTORE:
                case RedisCommand.SWAPDB:
                case RedisCommand.UNLINK:
                case RedisCommand.ZADD:
                case RedisCommand.ZINTERSTORE:
                case RedisCommand.ZINCRBY:
                case RedisCommand.ZPOPMAX:
                case RedisCommand.ZPOPMIN:
                case RedisCommand.ZREM:
                case RedisCommand.ZREMRANGEBYLEX:
                case RedisCommand.ZREMRANGEBYRANK:
                case RedisCommand.ZREMRANGEBYSCORE:
                case RedisCommand.ZUNIONSTORE:
                    return true;
                default:
                    return false;
            }
        }

        public virtual void AppendStormLog(StringBuilder sb)
        {
            if (Db >= 0) sb.Append(Db).Append(':');
            sb.Append(CommandAndKey);
        }

        public virtual int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) { return ServerSelectionStrategy.NoSlot; }
        public bool IsMasterOnly()
        {
            // note that the constructor runs the switch statement above, so
            // this will alread be true for master-only commands, even if the
            // user specified PreferMaster etc
            return GetMasterSlaveFlags(Flags) == CommandFlags.DemandMaster;
        }

        /// <summary>
        /// This does a few important things:
        /// 1: it suppresses error events for commands that the user isn't interested in
        ///    (i.e. "why does my standalone server keep saying ERR unknown command 'cluster' ?")
        /// 2: it allows the initial PING and GET (during connect) to get queued rather
        ///    than be rejected as no-server-available (note that this doesn't apply to
        ///    handshake messages, as they bypass the queue completely)
        /// 3: it disables non-pref logging, as it is usually server-targeted
        /// </summary>
        public void SetInternalCall()
        {
            Flags |= InternalCallFlag;
        }

        public override string ToString()
        {
            return $"[{Db}]:{CommandAndKey} ({resultProcessor?.GetType().Name ?? "(n/a)"})";
        }

        public void SetResponseReceived() => performance?.SetResponseReceived();

        bool ICompletable.TryComplete(bool isAsync) { Complete(); return true; }

        public void Complete()
        {
            //Ensure we can never call Complete on the same resultBox from two threads by grabbing it now
            var currBox = Interlocked.Exchange(ref resultBox, null);

            // set the completion/performance data
            performance?.SetCompleted();

            currBox?.ActivateContinuations();
        }

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, RedisKey[] keys)
        {
            switch (keys.Length)
            {
                case 0: return new CommandKeyMessage(db, flags, command, key);
                case 1: return new CommandKeyKeyMessage(db, flags, command, key, keys[0]);
                case 2: return new CommandKeyKeyKeyMessage(db, flags, command, key, keys[0], keys[1]);
                default: return new CommandKeyKeysMessage(db, flags, command, key, keys);
            }
        }

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, IList<RedisKey> keys)
        {
            switch (keys.Count)
            {
                case 0: return new CommandMessage(db, flags, command);
                case 1: return new CommandKeyMessage(db, flags, command, keys[0]);
                case 2: return new CommandKeyKeyMessage(db, flags, command, keys[0], keys[1]);
                case 3: return new CommandKeyKeyKeyMessage(db, flags, command, keys[0], keys[1], keys[2]);
                default: return new CommandKeysMessage(db, flags, command, (keys as RedisKey[]) ?? keys.ToArray());
            }
        }

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, IList<RedisValue> values)
        {
            switch (values.Count)
            {
                case 0: return new CommandMessage(db, flags, command);
                case 1: return new CommandValueMessage(db, flags, command, values[0]);
                case 2: return new CommandValueValueMessage(db, flags, command, values[0], values[1]);
                case 3: return new CommandValueValueValueMessage(db, flags, command, values[0], values[1], values[2]);
                // no 4; not worth adding
                case 5: return new CommandValueValueValueValueValueMessage(db, flags, command, values[0], values[1], values[2], values[3], values[4]);
                default: return new CommandValuesMessage(db, flags, command, (values as RedisValue[]) ?? values.ToArray());
            }
        }

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, RedisValue[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            switch (values.Length)
            {
                case 0: return new CommandKeyMessage(db, flags, command, key);
                case 1: return new CommandKeyValueMessage(db, flags, command, key, values[0]);
                case 2: return new CommandKeyValueValueMessage(db, flags, command, key, values[0], values[1]);
                case 3: return new CommandKeyValueValueValueMessage(db, flags, command, key, values[0], values[1], values[2]);
                case 4: return new CommandKeyValueValueValueValueMessage(db, flags, command, key, values[0], values[1], values[2], values[3]);
                default: return new CommandKeyValuesMessage(db, flags, command, key, values);
            }
        }

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, RedisValue[] values, in RedisKey key1)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            return new CommandKeyValuesKeyMessage(db, flags, command, key0, values, key1);
        }

        internal static CommandFlags GetMasterSlaveFlags(CommandFlags flags)
        {
            // for the purposes of the switch, we only care about two bits
            return flags & MaskMasterServerPreference;
        }

        internal static bool RequiresDatabase(RedisCommand command)
        {
            switch (command)
            {
                case RedisCommand.ASKING:
                case RedisCommand.AUTH:
                case RedisCommand.BGREWRITEAOF:
                case RedisCommand.BGSAVE:
                case RedisCommand.CLIENT:
                case RedisCommand.CLUSTER:
                case RedisCommand.CONFIG:
                case RedisCommand.DISCARD:
                case RedisCommand.ECHO:
                case RedisCommand.FLUSHALL:
                case RedisCommand.INFO:
                case RedisCommand.LASTSAVE:
                case RedisCommand.LATENCY:
                case RedisCommand.MEMORY:
                case RedisCommand.MONITOR:
                case RedisCommand.MULTI:
                case RedisCommand.PING:
                case RedisCommand.PUBLISH:
                case RedisCommand.PUBSUB:
                case RedisCommand.PUNSUBSCRIBE:
                case RedisCommand.PSUBSCRIBE:
                case RedisCommand.QUIT:
                case RedisCommand.READONLY:
                case RedisCommand.READWRITE:
                case RedisCommand.SAVE:
                case RedisCommand.SCRIPT:
                case RedisCommand.SHUTDOWN:
                case RedisCommand.SLAVEOF:
                case RedisCommand.SLOWLOG:
                case RedisCommand.SUBSCRIBE:
                case RedisCommand.SWAPDB:
                case RedisCommand.SYNC:
                case RedisCommand.TIME:
                case RedisCommand.UNSUBSCRIBE:
                case RedisCommand.SENTINEL:
                    return false;
                default:
                    return true;
            }
        }

        internal static CommandFlags SetMasterSlaveFlags(CommandFlags everything, CommandFlags masterSlave)
        {
            // take away the two flags we don't want, and add back the ones we care about
            return (everything & ~(CommandFlags.DemandMaster | CommandFlags.DemandSlave | CommandFlags.PreferMaster | CommandFlags.PreferSlave))
                            | masterSlave;
        }

        internal void Cancel() => resultBox?.Cancel();

        // true if ready to be completed (i.e. false if re-issued to another server)
        internal bool ComputeResult(PhysicalConnection connection, in RawResult result)
        {
            var box = resultBox;
            try
            {
                if (box != null && box.IsFaulted) return false; // already failed (timeout, etc)
                if (resultProcessor == null) return true;

                // false here would be things like resends (MOVED) - the message is not yet complete
                return resultProcessor.SetResult(connection, this, result);
            }
            catch (Exception ex)
            {
                ex.Data.Add("got", result.ToString());
                connection?.BridgeCouldBeNull?.Multiplexer?.OnMessageFaulted(this, ex);
                box?.SetException(ex);
                return box != null; // we still want to pulse/complete
            }
        }

        internal void Fail(ConnectionFailureType failure, Exception innerException, string annotation)
        {
            PhysicalConnection.IdentifyFailureType(innerException, ref failure);
            resultProcessor?.ConnectionFail(this, failure, innerException, annotation);
        }

        internal virtual void SetExceptionAndComplete(Exception exception, PhysicalBridge bridge)
        {
            resultBox?.SetException(exception);
            Complete();
        }

        internal bool TrySetResult<T>(T value)
        {
            if (resultBox is IResultBox<T> typed && !typed.IsFaulted)
            {
                typed.SetResult(value);
                return true;
            }
            return false;
        }

        internal void SetEnqueued(PhysicalConnection connection)
        {
#if DEBUG
            QueuePosition = -1;
            ConnectionWriteState = PhysicalConnection.WriteStatus.NA;
#endif
            SetWriteTime();
            performance?.SetEnqueued();
            _enqueuedTo = connection;
            if (connection == null)
            {
                _queuedStampSent = _queuedStampReceived = -1;
            }
            else
            {
                connection.GetBytes(out _queuedStampSent, out _queuedStampReceived);
            }
        }

        internal void TryGetHeadMessages(out Message now, out Message next)
        {
            var connection = _enqueuedTo;
            now = next = null;
            if (connection != null) connection.GetHeadMessages(out now, out next);
        }

        internal bool TryGetPhysicalState(out PhysicalConnection.WriteStatus ws, out PhysicalConnection.ReadStatus rs,
            out long sentDelta, out long receivedDelta)
        {
            var connection = _enqueuedTo;
            sentDelta = receivedDelta = -1;
            if (connection != null)
            {
                ws = connection.GetWriteStatus();
                rs = connection.GetReadStatus();
                connection.GetBytes(out var sent, out var received);
                if (sent >= 0 && _queuedStampSent >= 0) sentDelta = sent - _queuedStampSent;
                if (received >= 0 && _queuedStampReceived >= 0) receivedDelta = received - _queuedStampReceived;
                return true;
            }
            else
            {
                ws = PhysicalConnection.WriteStatus.NA;
                rs = PhysicalConnection.ReadStatus.NA;
                return false;
            }
        }

        private PhysicalConnection _enqueuedTo;
        private long _queuedStampReceived, _queuedStampSent;

        internal void SetRequestSent()
        {
            Status = CommandStatus.Sent;
            performance?.SetRequestSent();
        }

        // the time (ticks) at which this message was considered written
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetWriteTime()
        {
            if ((Flags & NeedsAsyncTimeoutCheckFlag) != 0)
            {
                _writeTickCount = Environment.TickCount; // note this might be reset if we resend a message, cluster-moved etc; I'm OK with that
            }
        }
        private int _writeTickCount;
        public int GetWriteTime() => Volatile.Read(ref _writeTickCount);

        private void SetNeedsTimeoutCheck() => Flags |= NeedsAsyncTimeoutCheckFlag;
        internal bool HasAsyncTimedOut(int now, int timeoutMilliseconds, out int millisecondsTaken)
        {
            if ((Flags & NeedsAsyncTimeoutCheckFlag) != 0)
            {
                millisecondsTaken = unchecked(now - _writeTickCount); // note: we can't just check "if sent < cutoff" because of wrap-aro
                if (millisecondsTaken >= timeoutMilliseconds)
                {
                    Flags &= ~NeedsAsyncTimeoutCheckFlag; // note: we don't remove it from the queue - still might need to marry it up; but: it is toast
                    return true;
                }
            }
            else
            {
                millisecondsTaken = default;
            }
            return false;
        }

        internal void SetAsking(bool value)
        {
            if (value) Flags |= AskingFlag; // the bits giveth
            else Flags &= ~AskingFlag; // and the bits taketh away
        }

        internal void SetNoRedirect()
        {
            Flags |= CommandFlags.NoRedirect;
        }

        internal void SetPreferMaster()
        {
            Flags = (Flags & ~MaskMasterServerPreference) | CommandFlags.PreferMaster;
        }

        internal void SetPreferSlave()
        {
            Flags = (Flags & ~MaskMasterServerPreference) | CommandFlags.PreferSlave;
        }

        internal void SetSource(ResultProcessor resultProcessor, IResultBox resultBox)
        { // note order here reversed to prevent overload resolution errors
            if (resultBox != null && resultBox.IsAsync) SetNeedsTimeoutCheck();
            this.resultBox = resultBox;
            this.resultProcessor = resultProcessor;
        }

        internal void SetSource<T>(IResultBox<T> resultBox, ResultProcessor<T> resultProcessor)
        {
            if (resultBox != null && resultBox.IsAsync) SetNeedsTimeoutCheck();
            this.resultBox = resultBox;
            this.resultProcessor = resultProcessor;
        }

        protected abstract void WriteImpl(PhysicalConnection physical);

        internal void WriteTo(PhysicalConnection physical)
        {
            try
            {
                WriteImpl(physical);
            }
            catch (RedisCommandException)
            { // these have specific meaning; don't wrap
                throw;
            }
            catch (Exception ex)
            {
                physical?.OnInternalError(ex);
                Fail(ConnectionFailureType.InternalFailure, ex, null);
            }
        }

        internal abstract class CommandChannelBase : Message
        {
            protected readonly RedisChannel Channel;

            protected CommandChannelBase(int db, CommandFlags flags, RedisCommand command, in RedisChannel channel) : base(db, flags, command)
            {
                channel.AssertNotNull();
                Channel = channel;
            }

            public override string CommandAndKey => Command + " " + Channel;
        }

        internal abstract class CommandKeyBase : Message
        {
            protected readonly RedisKey Key;

            protected CommandKeyBase(int db, CommandFlags flags, RedisCommand command, in RedisKey key) : base(db, flags, command)
            {
                key.AssertNotNull();
                Key = key;
            }

            public override string CommandAndKey => Command + " " + (string)Key;

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                return serverSelectionStrategy.HashSlot(Key);
            }
        }

        private sealed class CommandChannelMessage : CommandChannelBase
        {
            public CommandChannelMessage(int db, CommandFlags flags, RedisCommand command, in RedisChannel channel) : base(db, flags, command, channel)
            { }
            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 1);
                physical.Write(Channel);
            }
            public override int ArgCount => 1;
        }

        private sealed class CommandChannelValueMessage : CommandChannelBase
        {
            private readonly RedisValue value;
            public CommandChannelValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisChannel channel, in RedisValue value) : base(db, flags, command, channel)
            {
                value.AssertNotNull();
                this.value = value;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.Write(Channel);
                physical.WriteBulkString(value);
            }
            public override int ArgCount => 2;
        }

        private sealed class CommandKeyKeyKeyMessage : CommandKeyBase
        {
            private readonly RedisKey key1, key2;
            public CommandKeyKeyKeyMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, in RedisKey key1, in RedisKey key2) : base(db, flags, command, key0)
            {
                key1.AssertNotNull();
                key2.AssertNotNull();
                this.key1 = key1;
                this.key2 = key2;
            }

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                var slot = serverSelectionStrategy.HashSlot(Key);
                slot = serverSelectionStrategy.CombineSlot(slot, key1);
                return serverSelectionStrategy.CombineSlot(slot, key2);
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 3);
                physical.Write(Key);
                physical.Write(key1);
                physical.Write(key2);
            }
            public override int ArgCount => 3;
        }

        private class CommandKeyKeyMessage : CommandKeyBase
        {
            protected readonly RedisKey key1;
            public CommandKeyKeyMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, in RedisKey key1) : base(db, flags, command, key0)
            {
                key1.AssertNotNull();
                this.key1 = key1;
            }

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                var slot = serverSelectionStrategy.HashSlot(Key);
                return serverSelectionStrategy.CombineSlot(slot, key1);
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.Write(Key);
                physical.Write(key1);
            }
            public override int ArgCount => 2;
        }

        private sealed class CommandKeyKeysMessage : CommandKeyBase
        {
            private readonly RedisKey[] keys;
            public CommandKeyKeysMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key, RedisKey[] keys) : base(db, flags, command, key)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].AssertNotNull();
                }
                this.keys = keys;
            }

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                var slot = serverSelectionStrategy.HashSlot(Key);
                for (int i = 0; i < keys.Length; i++)
                {
                    slot = serverSelectionStrategy.CombineSlot(slot, keys[i]);
                }
                return slot;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(command, keys.Length + 1);
                physical.Write(Key);
                for (int i = 0; i < keys.Length; i++)
                {
                    physical.Write(keys[i]);
                }
            }
            public override int ArgCount => keys.Length + 1;
        }

        private sealed class CommandKeyKeyValueMessage : CommandKeyKeyMessage
        {
            private readonly RedisValue value;
            public CommandKeyKeyValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, in RedisKey key1, in RedisValue value) : base(db, flags, command, key0, key1)
            {
                value.AssertNotNull();
                this.value = value;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 3);
                physical.Write(Key);
                physical.Write(key1);
                physical.WriteBulkString(value);
            }

            public override int ArgCount => 3;
        }

        private sealed class CommandKeyMessage : CommandKeyBase
        {
            public CommandKeyMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key) : base(db, flags, command, key)
            { }
            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 1);
                physical.Write(Key);
            }
            public override int ArgCount => 1;
        }

        private sealed class CommandValuesMessage : Message
        {
            private readonly RedisValue[] values;
            public CommandValuesMessage(int db, CommandFlags flags, RedisCommand command, RedisValue[] values) : base(db, flags, command)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i].AssertNotNull();
                }
                this.values = values;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(command, values.Length);
                for (int i = 0; i < values.Length; i++)
                {
                    physical.WriteBulkString(values[i]);
                }
            }
            public override int ArgCount => values.Length;
        }

        private sealed class CommandKeysMessage : Message
        {
            private readonly RedisKey[] keys;
            public CommandKeysMessage(int db, CommandFlags flags, RedisCommand command, RedisKey[] keys) : base(db, flags, command)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].AssertNotNull();
                }
                this.keys = keys;
            }

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                int slot = ServerSelectionStrategy.NoSlot;
                for (int i = 0; i < keys.Length; i++)
                {
                    slot = serverSelectionStrategy.CombineSlot(slot, keys[i]);
                }
                return slot;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(command, keys.Length);
                for (int i = 0; i < keys.Length; i++)
                {
                    physical.Write(keys[i]);
                }
            }
            public override int ArgCount => keys.Length;
        }

        private sealed class CommandKeyValueMessage : CommandKeyBase
        {
            private readonly RedisValue value;
            public CommandKeyValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value) : base(db, flags, command, key)
            {
                value.AssertNotNull();
                this.value = value;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.Write(Key);
                physical.WriteBulkString(value);
            }
            public override int ArgCount => 2;
        }

        private sealed class CommandKeyValuesKeyMessage : CommandKeyBase
        {
            private readonly RedisKey key1;
            private readonly RedisValue[] values;
            public CommandKeyValuesKeyMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, RedisValue[] values, in RedisKey key1) : base(db, flags, command, key0)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i].AssertNotNull();
                }
                this.values = values;
                key1.AssertNotNull();
                this.key1 = key1;
            }

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                var slot = base.GetHashSlot(serverSelectionStrategy);
                return serverSelectionStrategy.CombineSlot(slot, key1);
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, values.Length + 2);
                physical.Write(Key);
                for (int i = 0; i < values.Length; i++) physical.WriteBulkString(values[i]);
                physical.Write(key1);
            }
            public override int ArgCount => values.Length + 2;
        }

        private sealed class CommandKeyValuesMessage : CommandKeyBase
        {
            private readonly RedisValue[] values;
            public CommandKeyValuesMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key, RedisValue[] values) : base(db, flags, command, key)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i].AssertNotNull();
                }
                this.values = values;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, values.Length + 1);
                physical.Write(Key);
                for (int i = 0; i < values.Length; i++) physical.WriteBulkString(values[i]);
            }
            public override int ArgCount => values.Length + 1;
        }

        private sealed class CommandKeyValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1;
            public CommandKeyValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1) : base(db, flags, command, key)
            {
                value0.AssertNotNull();
                value1.AssertNotNull();
                this.value0 = value0;
                this.value1 = value1;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 3);
                physical.Write(Key);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
            }
            public override int ArgCount => 3;
        }

        private sealed class CommandKeyValueValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1, value2;
            public CommandKeyValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1, in RedisValue value2) : base(db, flags, command, key)
            {
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 4);
                physical.Write(Key);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
                physical.WriteBulkString(value2);
            }
            public override int ArgCount => 4;
        }

        private sealed class CommandKeyValueValueValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1, value2, value3;
            public CommandKeyValueValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3) : base(db, flags, command, key)
            {
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                value3.AssertNotNull();
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
                this.value3 = value3;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 5);
                physical.Write(Key);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
                physical.WriteBulkString(value2);
                physical.WriteBulkString(value3);
            }
            public override int ArgCount => 5;
        }

        private sealed class CommandMessage : Message
        {
            public CommandMessage(int db, CommandFlags flags, RedisCommand command) : base(db, flags, command) { }
            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 0);
            }
            public override int ArgCount => 0;
        }

        private class CommandSlotValuesMessage : Message
        {
            private readonly int slot;
            private readonly RedisValue[] values;

            public CommandSlotValuesMessage(int db, int slot, CommandFlags flags, RedisCommand command, RedisValue[] values)
                : base(db, flags, command)
            {
                this.slot = slot;
                for (int i = 0; i < values.Length; i++)
                {
                    values[i].AssertNotNull();
                }
                this.values = values;
            }

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                return slot;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(command, values.Length);
                for (int i = 0; i < values.Length; i++)
                {
                    physical.WriteBulkString(values[i]);
                }
            }
            public override int ArgCount => values.Length;
        }

        private sealed class CommandValueChannelMessage : CommandChannelBase
        {
            private readonly RedisValue value;
            public CommandValueChannelMessage(int db, CommandFlags flags, RedisCommand command, in RedisValue value, in RedisChannel channel) : base(db, flags, command, channel)
            {
                value.AssertNotNull();
                this.value = value;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.WriteBulkString(value);
                physical.Write(Channel);
            }
            public override int ArgCount => 2;
        }

        private sealed class CommandValueKeyMessage : CommandKeyBase
        {
            private readonly RedisValue value;

            public CommandValueKeyMessage(int db, CommandFlags flags, RedisCommand command, in RedisValue value, in RedisKey key) : base(db, flags, command, key)
            {
                value.AssertNotNull();
                this.value = value;
            }

            public override void AppendStormLog(StringBuilder sb)
            {
                base.AppendStormLog(sb);
                sb.Append(" (").Append((string)value).Append(')');
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.WriteBulkString(value);
                physical.Write(Key);
            }
            public override int ArgCount => 2;
        }

        private sealed class CommandValueMessage : Message
        {
            private readonly RedisValue value;
            public CommandValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisValue value) : base(db, flags, command)
            {
                value.AssertNotNull();
                this.value = value;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 1);
                physical.WriteBulkString(value);
            }
            public override int ArgCount => 1;
        }

        private sealed class CommandValueValueMessage : Message
        {
            private readonly RedisValue value0, value1;
            public CommandValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisValue value0, in RedisValue value1) : base(db, flags, command)
            {
                value0.AssertNotNull();
                value1.AssertNotNull();
                this.value0 = value0;
                this.value1 = value1;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
            }
            public override int ArgCount => 2;
        }

        private sealed class CommandValueValueValueMessage : Message
        {
            private readonly RedisValue value0, value1, value2;
            public CommandValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisValue value0, in RedisValue value1, in RedisValue value2) : base(db, flags, command)
            {
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 3);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
                physical.WriteBulkString(value2);
            }
            public override int ArgCount => 3;
        }

        private sealed class CommandValueValueValueValueValueMessage : Message
        {
            private readonly RedisValue value0, value1, value2, value3, value4;
            public CommandValueValueValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4) : base(db, flags, command)
            {
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                value3.AssertNotNull();
                value4.AssertNotNull();
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
                this.value3 = value3;
                this.value4 = value4;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 5);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
                physical.WriteBulkString(value2);
                physical.WriteBulkString(value3);
                physical.WriteBulkString(value4);
            }
            public override int ArgCount => 5;
        }

        private sealed class SelectMessage : Message
        {
            public SelectMessage(int db, CommandFlags flags) : base(db, flags, RedisCommand.SELECT)
            {
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 1);
                physical.WriteBulkString(Db);
            }
            public override int ArgCount => 1;
        }
    }
}
