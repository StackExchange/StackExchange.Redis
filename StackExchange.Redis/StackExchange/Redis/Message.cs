using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if FEATURE_SERIALIZATION
using System.Runtime.Serialization;
#endif
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Indicates that a command was illegal and was not sent to the server
    /// </summary>
#if FEATURE_SERIALIZATION
    [Serializable]
#endif
    public sealed class RedisCommandException : Exception
    {
#if FEATURE_SERIALIZATION
        private RedisCommandException(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }
#endif
        internal RedisCommandException(string message) : base(message) { }
        internal RedisCommandException(string message, Exception innerException) : base(message, innerException) { }
    }



    /// <summary>
    /// Indicates a connection fault when communicating with redis
    /// </summary>
#if FEATURE_SERIALIZATION
    [Serializable]
#endif
    public sealed class RedisConnectionException : RedisException
    {
#if FEATURE_SERIALIZATION
        private RedisConnectionException(SerializationInfo info, StreamingContext ctx) : base(info, ctx)
        {
            FailureType = (ConnectionFailureType)info.GetInt32("failureType");
        }
        /// <summary>
        /// Serialization implementation; not intended for general usage
        /// </summary>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("failureType", (int)FailureType);
        }
#endif

        internal RedisConnectionException(ConnectionFailureType failureType, string message) : base(message)
        {
            FailureType = failureType;
        }
        internal RedisConnectionException(ConnectionFailureType failureType, string message, Exception innerException) : base(message, innerException)
        {
            FailureType = failureType;
        }

        /// <summary>
        /// The type of connection failure
        /// </summary>
        public ConnectionFailureType FailureType { get; }
    }

    /// <summary>
    /// Indicates an issue communicating with redis
    /// </summary>
#if FEATURE_SERIALIZATION
    [Serializable]
#endif
    public class RedisException : Exception
    {
        /// <summary>
        /// Deserialization constructor; not intended for general usage
        /// </summary>
#if FEATURE_SERIALIZATION
        protected RedisException(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }
#endif

        internal RedisException(string message) : base(message) { }
        internal RedisException(string message, Exception innerException) : base(message, innerException) { }
    }
    /// <summary>
    /// Indicates an exception raised by a redis server
    /// </summary>
#if FEATURE_SERIALIZATION
    [Serializable]
#endif
    public sealed class RedisServerException : RedisException
    {
#if FEATURE_SERIALIZATION
        private RedisServerException(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }
#endif
        
        internal RedisServerException(string message) : base(message) { }
    }

    sealed class LoggingMessage : Message
    {
        public readonly TextWriter log;
        private readonly Message tail;

        public static Message Create(TextWriter log, Message tail)
        {
            return log == null ? tail : new LoggingMessage(log, tail);
        }
        private LoggingMessage(TextWriter log, Message tail) : base(tail.Db, tail.Flags, tail.Command)
        {
            this.log = log;
            this.tail = tail;
            FlagsRaw = tail.FlagsRaw;
        }
        public override string CommandAndKey => tail.CommandAndKey;

        public override void AppendStormLog(StringBuilder sb)
        {
            tail.AppendStormLog(sb);
        }
        public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
        {
            return tail.GetHashSlot(serverSelectionStrategy);
        }
        internal override void WriteImpl(PhysicalConnection physical)
        {
            try
            {
                physical.Multiplexer.LogLocked(log, "Writing to {0}: {1}", physical.Bridge, tail.CommandAndKey);
            }
            catch { }
            tail.WriteImpl(physical);
        }

        public TextWriter Log => log;
    }

    abstract class Message : ICompletable
    {

        public static readonly Message[] EmptyArray = new Message[0];
        public readonly int Db;

        internal const CommandFlags InternalCallFlag = (CommandFlags)128;
        protected RedisCommand command;

        private const CommandFlags AskingFlag = (CommandFlags)32,
                                   ScriptUnavailableFlag = (CommandFlags)256;

        const CommandFlags MaskMasterServerPreference = CommandFlags.DemandMaster | CommandFlags.DemandSlave | CommandFlags.PreferMaster | CommandFlags.PreferSlave;

        private const CommandFlags UserSelectableFlags
            = CommandFlags.None | CommandFlags.DemandMaster | CommandFlags.DemandSlave
            | CommandFlags.PreferMaster | CommandFlags.PreferSlave
            | CommandFlags.HighPriority | CommandFlags.FireAndForget | CommandFlags.NoRedirect;

        private CommandFlags flags;
        internal CommandFlags FlagsRaw { get { return flags; } set { flags = value; } }
        private ResultBox resultBox;

        private ResultProcessor resultProcessor;

        // All for profiling purposes
        private ProfileStorage performance;
        internal DateTime createdDateTime;
        internal long createdTimestamp;

        protected Message(int db, CommandFlags flags, RedisCommand command)
        {
            bool dbNeeded = RequiresDatabase(command);
            if (db < 0)
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
            this.flags = flags & UserSelectableFlags;
            if (masterOnly) SetMasterOnly();

            createdDateTime = DateTime.UtcNow;
            createdTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        internal void SetMasterOnly()
        {
            switch (GetMasterSlaveFlags(flags))
            {
                case CommandFlags.DemandSlave:
                    throw ExceptionFactory.MasterOnly(false, command, null, null);
                case CommandFlags.DemandMaster:
                    // already fine as-is
                    break;
                case CommandFlags.PreferMaster:
                case CommandFlags.PreferSlave:
                default: // we will run this on the master, then
                    flags = SetMasterSlaveFlags(flags, CommandFlags.DemandMaster);
                    break;
            }
        }

        internal void SetProfileStorage(ProfileStorage storage)
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
            performance = ProfileStorage.NewAttachedToSameContext(oldPerformance, resendTo, isMoved);
            performance.SetMessage(this);
        }

        public RedisCommand Command => command;

        public virtual string CommandAndKey => Command.ToString();

        public CommandFlags Flags => flags;

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
                    case RedisCommand.SYNC:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public bool IsAsking => (flags & AskingFlag) != 0;

        internal bool IsScriptUnavailable => (flags & ScriptUnavailableFlag) != 0;

        internal void SetScriptUnavailable()
        {
            flags |= ScriptUnavailableFlag;
        }

        public bool IsFireAndForget => (flags & CommandFlags.FireAndForget) != 0;

        public bool IsHighPriority => (flags & CommandFlags.HighPriority) != 0;

        public bool IsInternalCall => (flags & InternalCallFlag) != 0;

        public ResultBox ResultBox => resultBox;

        public static Message Create(int db, CommandFlags flags, RedisCommand command)
        {
            if (command == RedisCommand.SELECT)
                return new SelectMessage(db, flags);
            return new CommandMessage(db, flags, command);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisKey key)
        {
            return new CommandKeyMessage(db, flags, command, key);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisKey key0, RedisKey key1)
        {
            return new CommandKeyKeyMessage(db, flags, command, key0, key1);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisKey key0, RedisKey key1, RedisValue value)
        {
            return new CommandKeyKeyValueMessage(db, flags, command, key0, key1, value);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisKey key0, RedisKey key1, RedisKey key2)
        {
            return new CommandKeyKeyKeyMessage(db, flags, command, key0, key1, key2);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisValue value)
        {
            return new CommandValueMessage(db, flags, command, value);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue value)
        {
            return new CommandKeyValueMessage(db, flags, command, key, value);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisChannel channel)
        {
            return new CommandChannelMessage(db, flags, command, channel);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisChannel channel, RedisValue value)
        {
            return new CommandChannelValueMessage(db, flags, command, channel, value);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisValue value, RedisChannel channel)
        {
            return new CommandValueChannelMessage(db, flags, command, value, channel);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue value0, RedisValue value1)
        {
            return new CommandKeyValueValueMessage(db, flags, command, key, value0, value1);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue value0, RedisValue value1, RedisValue value2)
        {
            return new CommandKeyValueValueValueMessage(db, flags, command, key, value0, value1, value2);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue value0, RedisValue value1, RedisValue value2, RedisValue value3)
        {
            return new CommandKeyValueValueValueValueMessage(db, flags, command, key, value0, value1, value2, value3);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisValue value0, RedisValue value1)
        {
            return new CommandValueValueMessage(db, flags, command, value0, value1);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisValue value, RedisKey key)
        {
            return new CommandValueKeyMessage(db, flags, command, value, key);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisValue value0, RedisValue value1, RedisValue value2)
        {
            return new CommandValueValueValueMessage(db, flags, command, value0, value1, value2);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, RedisValue value0, RedisValue value1, RedisValue value2, RedisValue value3, RedisValue value4)
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
                case RedisCommand.ZADD:
                case RedisCommand.ZINTERSTORE:
                case RedisCommand.ZINCRBY:
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
            return GetMasterSlaveFlags(flags) == CommandFlags.DemandMaster;
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
            flags |= InternalCallFlag;
        }

        public override string ToString()
        {
            return $"[{Db}]:{CommandAndKey} ({resultProcessor?.GetType().Name ?? "(n/a)"})";
        }

        public void SetResponseReceived()
        {
            performance?.SetResponseReceived();
        }

        public bool TryComplete(bool isAsync)
        {
            //Ensure we can never call TryComplete on the same resultBox from two threads by grabbing it now
            var currBox = Interlocked.Exchange(ref resultBox, null);
            if (currBox != null)
            {
                var ret = currBox.TryComplete(isAsync);

                //in async mode TryComplete will have unwrapped and recycled resultBox
                if (!(ret && isAsync))
                {
                    //put result box back if it was not already recycled
                    Interlocked.Exchange(ref resultBox, currBox);
                }

                performance?.SetCompleted();
                return ret;
            }
            else
            {
                ConnectionMultiplexer.TraceWithoutContext("No result-box to complete for " + Command, "Message");
                performance?.SetCompleted();
                return true;
            }
        }

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisKey[] keys)
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

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue[] values)
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

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, RedisKey key0, RedisValue[] values, RedisKey key1)
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

        internal void Cancel()
        {
            resultProcessor?.SetException(this, new TaskCanceledException());
        }

        // true if ready to be completed (i.e. false if re-issued to another server)
        internal bool ComputeResult(PhysicalConnection connection, RawResult result)
        {
            return resultProcessor == null || resultProcessor.SetResult(connection, this, result);
        }

        internal void Fail(ConnectionFailureType failure, Exception innerException)
        {
            PhysicalConnection.IdentifyFailureType(innerException, ref failure);
            resultProcessor?.ConnectionFail(this, failure, innerException);
        }

        internal void SetEnqueued()
        {
            performance?.SetEnqueued();
        }

        internal void SetRequestSent()
        {
            performance?.SetRequestSent();
        }

        internal void SetAsking(bool value)
        {
            if (value) flags |= AskingFlag; // the bits giveth
            else flags &= ~AskingFlag; // and the bits taketh away
        }

        internal void SetNoRedirect()
        {
            flags |= CommandFlags.NoRedirect;
        }

        internal void SetPreferMaster()
        {
            flags = (flags & ~MaskMasterServerPreference) | CommandFlags.PreferMaster;
        }

        internal void SetPreferSlave()
        {
            flags = (flags & ~MaskMasterServerPreference) | CommandFlags.PreferSlave;
        }
        internal void SetSource(ResultProcessor resultProcessor, ResultBox resultBox)
        { // note order here reversed to prevent overload resolution errors
            this.resultBox = resultBox;
            this.resultProcessor = resultProcessor;
        }

        internal void SetSource<T>(ResultBox<T> resultBox, ResultProcessor<T> resultProcessor)
        {
            this.resultBox = resultBox;
            this.resultProcessor = resultProcessor;
        }

        internal abstract void WriteImpl(PhysicalConnection physical);

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
                Fail(ConnectionFailureType.InternalFailure, ex);
            }
        }
        internal abstract class CommandChannelBase : Message
        {
            protected readonly RedisChannel Channel;

            public CommandChannelBase(int db, CommandFlags flags, RedisCommand command, RedisChannel channel) : base(db, flags, command)
            {
                channel.AssertNotNull();
                Channel = channel;
            }

            public override string CommandAndKey => Command + " " + Channel;
        }

        internal abstract class CommandKeyBase : Message
        {
            protected readonly RedisKey Key;

            public CommandKeyBase(int db, CommandFlags flags, RedisCommand command, RedisKey key) : base(db, flags, command)
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
        sealed class CommandChannelMessage : CommandChannelBase
        {
            public CommandChannelMessage(int db, CommandFlags flags, RedisCommand command, RedisChannel channel) : base(db, flags, command, channel)
            { }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 1);
                physical.Write(Channel);
            }
        }

        sealed class CommandChannelValueMessage : CommandChannelBase
        {
            private readonly RedisValue value;
            public CommandChannelValueMessage(int db, CommandFlags flags, RedisCommand command, RedisChannel channel, RedisValue value) : base(db, flags, command, channel)
            {
                value.AssertNotNull();
                this.value = value;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.Write(Channel);
                physical.Write(value);
            }
        }

        sealed class CommandKeyKeyKeyMessage : CommandKeyBase
        {
            private readonly RedisKey key1, key2;
            public CommandKeyKeyKeyMessage(int db, CommandFlags flags, RedisCommand command, RedisKey key0, RedisKey key1, RedisKey key2) : base(db, flags, command, key0)
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
                slot = serverSelectionStrategy.CombineSlot(slot, key2);
                return slot;
            }

            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 3);
                physical.Write(Key);
                physical.Write(key1);
                physical.Write(key2);
            }
        }

        class CommandKeyKeyMessage : CommandKeyBase
        {
            protected readonly RedisKey key1;
            public CommandKeyKeyMessage(int db, CommandFlags flags, RedisCommand command, RedisKey key0, RedisKey key1) : base(db, flags, command, key0)
            {
                key1.AssertNotNull();
                this.key1 = key1;
            }
            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                var slot = serverSelectionStrategy.HashSlot(Key);
                slot = serverSelectionStrategy.CombineSlot(slot, key1);
                return slot;
            }

            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.Write(Key);
                physical.Write(key1);
            }
        }
        sealed class CommandKeyKeysMessage : CommandKeyBase
        {
            private readonly RedisKey[] keys;
            public CommandKeyKeysMessage(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisKey[] keys) : base(db, flags, command, key)
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
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(command, keys.Length + 1);
                physical.Write(Key);
                for (int i = 0; i < keys.Length; i++)
                {
                    physical.Write(keys[i]);
                }
            }
        }

        sealed class CommandKeyKeyValueMessage : CommandKeyKeyMessage
        {
            private readonly RedisValue value;
            public CommandKeyKeyValueMessage(int db, CommandFlags flags, RedisCommand command, RedisKey key0, RedisKey key1, RedisValue value) : base(db, flags, command, key0, key1)
            {
                value.AssertNotNull();
                this.value = value;
            }

            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 3);
                physical.Write(Key);
                physical.Write(key1);
                physical.Write(value);
            }
        }
        sealed class CommandKeyMessage : CommandKeyBase
        {
            public CommandKeyMessage(int db, CommandFlags flags, RedisCommand command, RedisKey key) : base(db, flags, command, key)
            { }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 1);
                physical.Write(Key);
            }
        }
        sealed class CommandValuesMessage : Message
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
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(command, values.Length);
                for (int i = 0; i < values.Length; i++)
                {
                    physical.Write(values[i]);
                }
            }
        }
        sealed class CommandKeysMessage : Message
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
                for(int i = 0; i < keys.Length; i++)
                {
                    slot = serverSelectionStrategy.CombineSlot(slot, keys[i]);
                }
                return slot;
            }

            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(command, keys.Length);
                for (int i = 0; i < keys.Length; i++)
                {
                    physical.Write(keys[i]);
                }
            }
        }

        sealed class CommandKeyValueMessage : CommandKeyBase
        {
            private readonly RedisValue value;
            public CommandKeyValueMessage(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue value) : base(db, flags, command, key)
            {
                value.AssertNotNull();
                this.value = value;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.Write(Key);
                physical.Write(value);
            }
        }
        sealed class CommandKeyValuesKeyMessage : CommandKeyBase
        {
            private readonly RedisKey key1;
            private readonly RedisValue[] values;
            public CommandKeyValuesKeyMessage(int db, CommandFlags flags, RedisCommand command, RedisKey key0, RedisValue[] values, RedisKey key1) : base(db, flags, command, key0)
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

            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, values.Length + 2);
                physical.Write(Key);
                for (int i = 0; i < values.Length; i++) physical.Write(values[i]);
                physical.Write(key1);
            }
        }

        sealed class CommandKeyValuesMessage : CommandKeyBase
        {
            private readonly RedisValue[] values;
            public CommandKeyValuesMessage(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue[] values) : base(db, flags, command, key)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i].AssertNotNull();
                }
                this.values = values;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, values.Length + 1);
                physical.Write(Key);
                for (int i = 0; i < values.Length; i++) physical.Write(values[i]);
            }
        }

        sealed class CommandKeyValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1;
            public CommandKeyValueValueMessage(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue value0, RedisValue value1) : base(db, flags, command, key)
            {
                value0.AssertNotNull();
                value1.AssertNotNull();
                this.value0 = value0;
                this.value1 = value1;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 3);
                physical.Write(Key);
                physical.Write(value0);
                physical.Write(value1);
            }
        }

        sealed class CommandKeyValueValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1, value2;
            public CommandKeyValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue value0, RedisValue value1, RedisValue value2) : base(db, flags, command, key)
            {
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 4);
                physical.Write(Key);
                physical.Write(value0);
                physical.Write(value1);
                physical.Write(value2);
            }
        }

        sealed class CommandKeyValueValueValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1, value2, value3;
            public CommandKeyValueValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, RedisKey key, RedisValue value0, RedisValue value1, RedisValue value2, RedisValue value3) : base(db, flags, command, key)
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
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 5);
                physical.Write(Key);
                physical.Write(value0);
                physical.Write(value1);
                physical.Write(value2);
                physical.Write(value3);
            }
        }

        sealed class CommandMessage : Message
        {
            public CommandMessage(int db, CommandFlags flags, RedisCommand command) : base(db, flags, command) { }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 0);
            }
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
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(command, values.Length);
                for (int i = 0; i < values.Length; i++)
                {
                    physical.Write(values[i]);
                }
            }
        }

        sealed class CommandValueChannelMessage : CommandChannelBase
        {
            private readonly RedisValue value;
            public CommandValueChannelMessage(int db, CommandFlags flags, RedisCommand command, RedisValue value, RedisChannel channel) : base(db, flags, command, channel)
            {
                value.AssertNotNull();
                this.value = value;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.Write(value);
                physical.Write(Channel);
            }
        }
        sealed class CommandValueKeyMessage : CommandKeyBase
        {
            private readonly RedisValue value;

            public CommandValueKeyMessage(int db, CommandFlags flags, RedisCommand command, RedisValue value, RedisKey key) : base(db, flags, command, key)
            {
                value.AssertNotNull();
                this.value = value;
            }

            public override void AppendStormLog(StringBuilder sb)
            {
                base.AppendStormLog(sb);
                sb.Append(" (").Append((string)value).Append(')');
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.Write(value);
                physical.Write(Key);
            }
        }

        sealed class CommandValueMessage : Message
        {
            private readonly RedisValue value;
            public CommandValueMessage(int db, CommandFlags flags, RedisCommand command, RedisValue value) : base(db, flags, command)
            {
                value.AssertNotNull();
                this.value = value;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 1);
                physical.Write(value);
            }
        }

        sealed class CommandValueValueMessage : Message
        {
            private readonly RedisValue value0, value1;
            public CommandValueValueMessage(int db, CommandFlags flags, RedisCommand command, RedisValue value0, RedisValue value1) : base(db, flags, command)
            {
                value0.AssertNotNull();
                value1.AssertNotNull();
                this.value0 = value0;
                this.value1 = value1;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 2);
                physical.Write(value0);
                physical.Write(value1);
            }
        }

        sealed class CommandValueValueValueMessage : Message
        {
            private readonly RedisValue value0, value1, value2;
            public CommandValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, RedisValue value0, RedisValue value1, RedisValue value2) : base(db, flags, command)
            {
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 3);
                physical.Write(value0);
                physical.Write(value1);
                physical.Write(value2);
            }
        }

        sealed class CommandValueValueValueValueValueMessage : Message
        {
            private readonly RedisValue value0, value1, value2, value3, value4;
            public CommandValueValueValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, RedisValue value0, RedisValue value1, RedisValue value2, RedisValue value3, RedisValue value4) : base(db, flags, command)
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
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 5);
                physical.Write(value0);
                physical.Write(value1);
                physical.Write(value2);
                physical.Write(value3);
                physical.Write(value4);
            }
        }

        sealed class SelectMessage : Message
        {
            public SelectMessage(int db, CommandFlags flags) : base(db, flags, RedisCommand.SELECT)
            {
            }
            internal override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, 1);
                physical.Write(Db);
            }
        }
    }
}
