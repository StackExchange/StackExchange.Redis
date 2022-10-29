using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using StackExchange.Redis.Profiling;

namespace StackExchange.Redis
{
    internal sealed class LoggingMessage : Message
    {
        public readonly LogProxy log;
        private readonly Message tail;

        public static Message Create(LogProxy? log, Message tail)
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
                log?.WriteLine($"{bridge?.Name}: Writing: {tail.CommandAndKey}");
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

        internal const CommandFlags InternalCallFlag = (CommandFlags)128;

        protected RedisCommand command;

        private const CommandFlags AskingFlag = (CommandFlags)32,
                                   ScriptUnavailableFlag = (CommandFlags)256,
                                   DemandSubscriptionConnection = (CommandFlags)2048;

        private const CommandFlags MaskPrimaryServerPreference = CommandFlags.DemandMaster
                                                               | CommandFlags.DemandReplica
                                                               | CommandFlags.PreferMaster
                                                               | CommandFlags.PreferReplica;

        private const CommandFlags UserSelectableFlags = CommandFlags.None
                                                       | CommandFlags.DemandMaster
                                                       | CommandFlags.DemandReplica
                                                       | CommandFlags.PreferMaster
                                                       | CommandFlags.PreferReplica
#pragma warning disable CS0618 // Type or member is obsolete
                                                       | CommandFlags.HighPriority
#pragma warning restore CS0618
                                                       | CommandFlags.FireAndForget
                                                       | CommandFlags.NoRedirect
                                                       | CommandFlags.NoScriptCache;
        private IResultBox? resultBox;

        private ResultProcessor? resultProcessor;

        // All for profiling purposes
        private ProfiledCommand? performance;
        internal DateTime CreatedDateTime;
        internal long CreatedTimestamp;

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

            bool primaryOnly = command.IsPrimaryOnly();
            Db = db;
            this.command = command;
            Flags = flags & UserSelectableFlags;
            if (primaryOnly) SetPrimaryOnly();

            CreatedDateTime = DateTime.UtcNow;
            CreatedTimestamp = Stopwatch.GetTimestamp();
            Status = CommandStatus.WaitingToBeSent;
        }

        internal void SetPrimaryOnly()
        {
            switch (GetPrimaryReplicaFlags(Flags))
            {
                case CommandFlags.DemandReplica:
                    throw ExceptionFactory.PrimaryOnly(false, command, null, null);
                case CommandFlags.DemandMaster:
                    // already fine as-is
                    break;
                case CommandFlags.PreferMaster:
                case CommandFlags.PreferReplica:
                default: // we will run this on the primary, then
                    Flags = SetPrimaryReplicaFlags(Flags, CommandFlags.DemandMaster);
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

            CreatedDateTime = DateTime.UtcNow;
            CreatedTimestamp = Stopwatch.GetTimestamp();
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
                    case RedisCommand.REPLICAOF:
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

        internal void SetScriptUnavailable() => Flags |= ScriptUnavailableFlag;

        public bool IsFireAndForget => (Flags & CommandFlags.FireAndForget) != 0;
        public bool IsInternalCall => (Flags & InternalCallFlag) != 0;

        public IResultBox? ResultBox => resultBox;

        public abstract int ArgCount { get; } // note: over-estimate if necessary

        public static Message Create(int db, CommandFlags flags, RedisCommand command)
        {
            if (command == RedisCommand.SELECT)
                return new SelectMessage(db, flags);
            return new CommandMessage(db, flags, command);
        }

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key) =>
            new CommandKeyMessage(db, flags, command, key);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, in RedisKey key1) =>
            new CommandKeyKeyMessage(db, flags, command, key0, key1);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, in RedisKey key1, in RedisValue value) =>
            new CommandKeyKeyValueMessage(db, flags, command, key0, key1, value);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, in RedisKey key1, in RedisKey key2) =>
            new CommandKeyKeyKeyMessage(db, flags, command, key0, key1, key2);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value) =>
            new CommandValueMessage(db, flags, command, value);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value) =>
            new CommandKeyValueMessage(db, flags, command, key, value);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisChannel channel) =>
            new CommandChannelMessage(db, flags, command, channel);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisChannel channel, in RedisValue value) =>
            new CommandChannelValueMessage(db, flags, command, channel, value);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value, in RedisChannel channel) =>
            new CommandValueChannelMessage(db, flags, command, value, channel);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1) =>
            new CommandKeyValueValueMessage(db, flags, command, key, value0, value1);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1, in RedisValue value2) =>
            new CommandKeyValueValueValueMessage(db, flags, command, key, value0, value1, value2);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, GeoEntry[] values)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(values);
#else
            if (values == null) throw new ArgumentNullException(nameof(values));
#endif
            if (values.Length == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(values));
            }
            if (values.Length == 1)
            {
                var value = values[0];
                return Create(db, flags, command, key, value.Longitude, value.Latitude, value.Member);
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

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3) =>
            new CommandKeyValueValueValueValueMessage(db, flags, command, key, value0, value1, value2, value3);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4) =>
            new CommandKeyValueValueValueValueValueMessage(db, flags, command, key, value0, value1, value2, value3, value4);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4, in RedisValue value5, in RedisValue value6) =>
            new CommandKeyValueValueValueValueValueValueValueMessage(db, flags, command, key, value0, value1, value2, value3, value4, value5, value6);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value0, in RedisValue value1) =>
            new CommandValueValueMessage(db, flags, command, value0, value1);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value, in RedisKey key) =>
            new CommandValueKeyMessage(db, flags, command, value, key);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value0, in RedisValue value1, in RedisValue value2) =>
            new CommandValueValueValueMessage(db, flags, command, value0, value1, value2);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4) =>
            new CommandValueValueValueValueValueMessage(db, flags, command, value0, value1, value2, value3, value4);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
            in RedisKey key1, in RedisValue value0, in RedisValue value1) =>
            new CommandKeyKeyValueValueMessage(db, flags, command, key0, key1, value0, value1);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
            in RedisKey key1, in RedisValue value0, in RedisValue value1, in RedisValue value2) =>
            new CommandKeyKeyValueValueValueMessage(db, flags, command, key0, key1, value0, value1, value2);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
            in RedisKey key1, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3) =>
            new CommandKeyKeyValueValueValueValueMessage(db, flags, command, key0, key1, value0, value1, value2, value3);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
            in RedisKey key1, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4) =>
            new CommandKeyKeyValueValueValueValueValueMessage(db, flags, command, key0, key1, value0, value1, value2, value3, value4);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
            in RedisKey key1, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4, in RedisValue value5) =>
            new CommandKeyKeyValueValueValueValueValueValueMessage(db, flags, command, key0, key1, value0, value1, value2, value3, value4, value5);

        public static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
            in RedisKey key1, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3,
            in RedisValue value4, in RedisValue value5, in RedisValue value6) =>
            new CommandKeyKeyValueValueValueValueValueValueValueMessage(db, flags, command, key0, key1, value0, value1, value2, value3, value4, value5, value6);

        public static Message CreateInSlot(int db, int slot, CommandFlags flags, RedisCommand command, RedisValue[] values) =>
            new CommandSlotValuesMessage(db, slot, flags, command, values);

        /// <summary>Gets whether this is primary-only.</summary>
        /// <remarks>
        /// Note that the constructor runs the switch statement above, so
        /// this will already be true for primary-only commands, even if the
        /// user specified <see cref="CommandFlags.PreferMaster"/> etc.
        /// </remarks>
        public bool IsPrimaryOnly() => GetPrimaryReplicaFlags(Flags) == CommandFlags.DemandMaster;

        public virtual void AppendStormLog(StringBuilder sb)
        {
            if (Db >= 0) sb.Append(Db).Append(':');
            sb.Append(CommandAndKey);
        }

        public virtual int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => ServerSelectionStrategy.NoSlot;

        /// <summary>
        /// This does a few important things:
        /// 1: it suppresses error events for commands that the user isn't interested in
        ///    (i.e. "why does my standalone server keep saying ERR unknown command 'cluster' ?")
        /// 2: it allows the initial PING and GET (during connect) to get queued rather
        ///    than be rejected as no-server-available (note that this doesn't apply to
        ///    handshake messages, as they bypass the queue completely)
        /// 3: it disables non-pref logging, as it is usually server-targeted
        /// </summary>
        public void SetInternalCall() => Flags |= InternalCallFlag;

        /// <summary>
        /// Gets a string representation of this message: "[{DB}]:{CommandAndKey} ({resultProcessor})"
        /// </summary>
        public override string ToString() =>
            $"[{Db}]:{CommandAndKey} ({resultProcessor?.GetType().Name ?? "(n/a)"})";

        /// <summary>
        /// Gets a string representation of this message without the key: "[{DB}]:{Command} ({resultProcessor})"
        /// </summary>
        public string ToStringCommandOnly() =>
            $"[{Db}]:{Command} ({resultProcessor?.GetType().Name ?? "(n/a)"})";

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

        internal bool ResultBoxIsAsync => Volatile.Read(ref resultBox)?.IsAsync == true;

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, RedisKey[] keys) => keys.Length switch
        {
            0 => new CommandKeyMessage(db, flags, command, key),
            1 => new CommandKeyKeyMessage(db, flags, command, key, keys[0]),
            2 => new CommandKeyKeyKeyMessage(db, flags, command, key, keys[0], keys[1]),
            _ => new CommandKeyKeysMessage(db, flags, command, key, keys),
        };

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, IList<RedisKey> keys) => keys.Count switch
        {
            0 => new CommandMessage(db, flags, command),
            1 => new CommandKeyMessage(db, flags, command, keys[0]),
            2 => new CommandKeyKeyMessage(db, flags, command, keys[0], keys[1]),
            3 => new CommandKeyKeyKeyMessage(db, flags, command, keys[0], keys[1], keys[2]),
            _ => new CommandKeysMessage(db, flags, command, (keys as RedisKey[]) ?? keys.ToArray()),
        };

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, IList<RedisValue> values) => values.Count switch
        {
            0 => new CommandMessage(db, flags, command),
            1 => new CommandValueMessage(db, flags, command, values[0]),
            2 => new CommandValueValueMessage(db, flags, command, values[0], values[1]),
            3 => new CommandValueValueValueMessage(db, flags, command, values[0], values[1], values[2]),
            // no 4; not worth adding
            5 => new CommandValueValueValueValueValueMessage(db, flags, command, values[0], values[1], values[2], values[3], values[4]),
            _ => new CommandValuesMessage(db, flags, command, (values as RedisValue[]) ?? values.ToArray()),
        };

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key, RedisValue[] values)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(values);
#else
            if (values == null) throw new ArgumentNullException(nameof(values));
#endif
            return values.Length switch
            {
                0 => new CommandKeyMessage(db, flags, command, key),
                1 => new CommandKeyValueMessage(db, flags, command, key, values[0]),
                2 => new CommandKeyValueValueMessage(db, flags, command, key, values[0], values[1]),
                3 => new CommandKeyValueValueValueMessage(db, flags, command, key, values[0], values[1], values[2]),
                4 => new CommandKeyValueValueValueValueMessage(db, flags, command, key, values[0], values[1], values[2], values[3]),
                _ => new CommandKeyValuesMessage(db, flags, command, key, values),
            };
        }

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, in RedisKey key1, RedisValue[] values)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(values);
#else
            if (values == null) throw new ArgumentNullException(nameof(values));
#endif
            return values.Length switch
            {
                0 => new CommandKeyKeyMessage(db, flags, command, key0, key1),
                1 => new CommandKeyKeyValueMessage(db, flags, command, key0, key1, values[0]),
                2 => new CommandKeyKeyValueValueMessage(db, flags, command, key0, key1, values[0], values[1]),
                3 => new CommandKeyKeyValueValueValueMessage(db, flags, command, key0, key1, values[0], values[1], values[2]),
                4 => new CommandKeyKeyValueValueValueValueMessage(db, flags, command, key0, key1, values[0], values[1], values[2], values[3]),
                5 => new CommandKeyKeyValueValueValueValueValueMessage(db, flags, command, key0, key1, values[0], values[1], values[2], values[3], values[4]),
                6 => new CommandKeyKeyValueValueValueValueValueValueMessage(db, flags, command, key0, key1, values[0], values[1], values[2], values[3],values[4],values[5]),
                7 => new CommandKeyKeyValueValueValueValueValueValueValueMessage(db, flags, command, key0, key1, values[0], values[1], values[2], values[3], values[4], values[5], values[6]),
                _ => new CommandKeyKeyValuesMessage(db, flags, command, key0, key1, values),
            };
        }

        internal static Message Create(int db, CommandFlags flags, RedisCommand command, in RedisKey key0, RedisValue[] values, in RedisKey key1)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(values);
#else
            if (values == null) throw new ArgumentNullException(nameof(values));
#endif
            return new CommandKeyValuesKeyMessage(db, flags, command, key0, values, key1);
        }

        internal static CommandFlags GetPrimaryReplicaFlags(CommandFlags flags)
        {
            // for the purposes of the switch, we only care about two bits
            return flags & MaskPrimaryServerPreference;
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
                case RedisCommand.COMMAND:
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
                case RedisCommand.REPLICAOF:
                case RedisCommand.ROLE:
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

        internal static CommandFlags SetPrimaryReplicaFlags(CommandFlags everything, CommandFlags primaryReplica)
        {
            // take away the two flags we don't want, and add back the ones we care about
            return (everything & ~(CommandFlags.DemandMaster | CommandFlags.DemandReplica | CommandFlags.PreferMaster | CommandFlags.PreferReplica))
                            | primaryReplica;
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

        internal void Fail(ConnectionFailureType failure, Exception? innerException, string? annotation, ConnectionMultiplexer? muxer)
        {
            PhysicalConnection.IdentifyFailureType(innerException, ref failure);
            resultProcessor?.ConnectionFail(this, failure, innerException, annotation, muxer);
        }

        internal virtual void SetExceptionAndComplete(Exception exception, PhysicalBridge? bridge)
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

        internal void SetEnqueued(PhysicalConnection? connection)
        {
            SetWriteTime();
            performance?.SetEnqueued(connection?.BridgeCouldBeNull?.ConnectionType);
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

        internal void TryGetHeadMessages(out Message? now, out Message? next)
        {
            now = next = null;
            _enqueuedTo?.GetHeadMessages(out now, out next);
        }

        internal bool TryGetPhysicalState(
            out PhysicalConnection.WriteStatus ws,
            out PhysicalConnection.ReadStatus rs,
            out long sentDelta,
            out long receivedDelta)
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

        private PhysicalConnection? _enqueuedTo;
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
            _writeTickCount = Environment.TickCount; // note this might be reset if we resend a message, cluster-moved etc; I'm OK with that
        }
        private int _writeTickCount;
        public int GetWriteTime() => Volatile.Read(ref _writeTickCount);

        /// <summary>
        /// Gets if this command should be sent over the subscription bridge.
        /// </summary>
        internal bool IsForSubscriptionBridge => (Flags & DemandSubscriptionConnection) != 0;
        /// <summary>
        /// Sends this command to the subscription connection rather than the interactive.
        /// </summary>
        internal void SetForSubscriptionBridge() => Flags |= DemandSubscriptionConnection;

        /// <summary>
        /// Checks if this message has violated the provided timeout.
        /// Whether it's a sync operation in a .Wait() or in the backlog queue or written/pending asynchronously, we need to timeout everything.
        /// ...or we get indefinite Task hangs for completions.
        /// </summary>
        internal bool HasTimedOut(int now, int timeoutMilliseconds, out int millisecondsTaken)
        {
            millisecondsTaken = unchecked(now - _writeTickCount); // note: we can't just check "if sent < cutoff" because of wrap-around
            return millisecondsTaken >= timeoutMilliseconds;
        }

        internal void SetAsking(bool value)
        {
            if (value) Flags |= AskingFlag; // the bits giveth
            else Flags &= ~AskingFlag; // and the bits taketh away
        }

        internal void SetNoRedirect() => Flags |= CommandFlags.NoRedirect;

        internal void SetPreferPrimary() =>
            Flags = (Flags & ~MaskPrimaryServerPreference) | CommandFlags.PreferMaster;

        internal void SetPreferReplica() =>
            Flags = (Flags & ~MaskPrimaryServerPreference) | CommandFlags.PreferReplica;

        /// <summary>
        /// Sets the processor and box for this message to execute.
        /// </summary>
        /// <remarks>
        /// Note order here is reversed to prevent overload resolution errors.
        /// </remarks>
        internal void SetSource(ResultProcessor? resultProcessor, IResultBox? resultBox)
        {
            this.resultBox = resultBox;
            this.resultProcessor = resultProcessor;
        }

        /// <summary>
        /// Sets the box and processor for this message to execute.
        /// </summary>
        /// <remarks>
        /// Note order here is reversed to prevent overload resolution errors.
        /// </remarks>
        internal void SetSource<T>(IResultBox<T> resultBox, ResultProcessor<T>? resultProcessor)
        {
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
            catch (Exception ex) when (ex is not RedisCommandException) // these have specific meaning; don't wrap
            {
                physical?.OnInternalError(ex);
                Fail(ConnectionFailureType.InternalFailure, ex, null, physical?.BridgeCouldBeNull?.Multiplexer);
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

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => serverSelectionStrategy.HashSlot(Channel);
        }

        internal abstract class CommandKeyBase : Message
        {
            protected readonly RedisKey Key;

            protected CommandKeyBase(int db, CommandFlags flags, RedisCommand command, in RedisKey key) : base(db, flags, command)
            {
                key.AssertNotNull();
                Key = key;
            }

            public override string CommandAndKey => Command + " " + (string?)Key;

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => serverSelectionStrategy.HashSlot(Key);
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

        private sealed class CommandKeyKeyValuesMessage : CommandKeyBase
        {
            private readonly RedisKey key1;
            private readonly RedisValue[] values;
            public CommandKeyKeyValuesMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisKey key1, RedisValue[] values) : base(db, flags, command, key)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i].AssertNotNull();
                }

                key1.AssertNotNull();
                this.key1 = key1;
                this.values = values;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, values.Length + 2);
                physical.Write(Key);
                physical.Write(key1);
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

        private sealed class CommandKeyValueValueValueValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1, value2, value3, value4;
            public CommandKeyValueValueValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4) : base(db, flags, command, key)
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
                physical.WriteHeader(Command, 6);
                physical.Write(Key);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
                physical.WriteBulkString(value2);
                physical.WriteBulkString(value3);
                physical.WriteBulkString(value4);
            }
            public override int ArgCount => 6;
        }

        private sealed class CommandKeyValueValueValueValueValueValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1, value2, value3, value4, value5, value6;

            public CommandKeyValueValueValueValueValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4, in RedisValue value5, in RedisValue value6) : base(db, flags, command, key)
            {
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                value3.AssertNotNull();
                value4.AssertNotNull();
                value5.AssertNotNull();
                value6.AssertNotNull();
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
                this.value3 = value3;
                this.value4 = value4;
                this.value5 = value5;
                this.value6 = value6;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, ArgCount);
                physical.Write(Key);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
                physical.WriteBulkString(value2);
                physical.WriteBulkString(value3);
                physical.WriteBulkString(value4);
                physical.WriteBulkString(value5);
                physical.WriteBulkString(value6);
            }
            public override int ArgCount => 8;
        }

        private sealed class CommandKeyKeyValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1;
            private readonly RedisKey key1;

            public CommandKeyKeyValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
                in RedisKey key1, in RedisValue value0, in RedisValue value1) : base(db, flags, command, key0)
            {
                key1.AssertNotNull();
                value0.AssertNotNull();
                value1.AssertNotNull();
                this.key1 = key1;
                this.value0 = value0;
                this.value1 = value1;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, ArgCount);
                physical.Write(Key);
                physical.Write(key1);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
            }

            public override int ArgCount => 4;
        }

        private sealed class CommandKeyKeyValueValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1, value2;
            private readonly RedisKey key1;

            public CommandKeyKeyValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
                in RedisKey key1, in RedisValue value0, in RedisValue value1, in RedisValue value2) : base(db, flags, command, key0)
            {
                key1.AssertNotNull();
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                this.key1 = key1;
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, ArgCount);
                physical.Write(Key);
                physical.Write(key1);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
                physical.WriteBulkString(value2);
            }

            public override int ArgCount => 5;
        }

        private sealed class CommandKeyKeyValueValueValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1, value2, value3;
            private readonly RedisKey key1;

            public CommandKeyKeyValueValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
                in RedisKey key1, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3) : base(db, flags, command, key0)
            {
                key1.AssertNotNull();
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                value3.AssertNotNull();
                this.key1 = key1;
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
                this.value3 = value3;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, ArgCount);
                physical.Write(Key);
                physical.Write(key1);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
                physical.WriteBulkString(value2);
                physical.WriteBulkString(value3);
            }

            public override int ArgCount => 6;
        }

        private sealed class CommandKeyKeyValueValueValueValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1, value2, value3, value4;
            private readonly RedisKey key1;

            public CommandKeyKeyValueValueValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
                in RedisKey key1, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4) : base(db, flags, command, key0)
            {
                key1.AssertNotNull();
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                value3.AssertNotNull();
                value4.AssertNotNull();
                this.key1 = key1;
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
                this.value3 = value3;
                this.value4 = value4;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, ArgCount);
                physical.Write(Key);
                physical.Write(key1);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
                physical.WriteBulkString(value2);
                physical.WriteBulkString(value3);
                physical.WriteBulkString(value4);
            }

            public override int ArgCount => 7;
        }

        private sealed class CommandKeyKeyValueValueValueValueValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1, value2, value3, value4, value5;
            private readonly RedisKey key1;

            public CommandKeyKeyValueValueValueValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
                in RedisKey key1, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4, in RedisValue value5) : base(db, flags, command, key0)
            {
                key1.AssertNotNull();
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                value3.AssertNotNull();
                value4.AssertNotNull();
                value5.AssertNotNull();
                this.key1 = key1;
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
                this.value3 = value3;
                this.value4 = value4;
                this.value5 = value5;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, ArgCount);
                physical.Write(Key);
                physical.Write(key1);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
                physical.WriteBulkString(value2);
                physical.WriteBulkString(value3);
                physical.WriteBulkString(value4);
                physical.WriteBulkString(value5);
            }

            public override int ArgCount => 8;
        }

        private sealed class CommandKeyKeyValueValueValueValueValueValueValueMessage : CommandKeyBase
        {
            private readonly RedisValue value0, value1, value2, value3, value4, value5, value6;
            private readonly RedisKey key1;

            public CommandKeyKeyValueValueValueValueValueValueValueMessage(int db, CommandFlags flags, RedisCommand command, in RedisKey key0,
                in RedisKey key1, in RedisValue value0, in RedisValue value1, in RedisValue value2, in RedisValue value3, in RedisValue value4, in RedisValue value5, in RedisValue value6) : base(db, flags, command, key0)
            {
                key1.AssertNotNull();
                value0.AssertNotNull();
                value1.AssertNotNull();
                value2.AssertNotNull();
                value3.AssertNotNull();
                value4.AssertNotNull();
                value5.AssertNotNull();
                value6.AssertNotNull();
                this.key1 = key1;
                this.value0 = value0;
                this.value1 = value1;
                this.value2 = value2;
                this.value3 = value3;
                this.value4 = value4;
                this.value5 = value5;
                this.value6 = value6;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                physical.WriteHeader(Command, ArgCount);
                physical.Write(Key);
                physical.Write(key1);
                physical.WriteBulkString(value0);
                physical.WriteBulkString(value1);
                physical.WriteBulkString(value2);
                physical.WriteBulkString(value3);
                physical.WriteBulkString(value4);
                physical.WriteBulkString(value5);
                physical.WriteBulkString(value6);
            }

            public override int ArgCount => 9;
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

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => slot;

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
                sb.Append(" (").Append((string?)value).Append(')');
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
