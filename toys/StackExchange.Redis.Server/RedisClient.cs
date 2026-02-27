using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;

namespace StackExchange.Redis.Server
{
    public class RedisClient(RedisServer.Node node) : IDisposable
    {
        public RedisServer.Node Node => node;
        internal int SkipReplies { get; set; }
        internal bool ShouldSkipResponse()
        {
            if (SkipReplies > 0)
            {
                SkipReplies--;
                return true;
            }
            return false;
        }
        private HashSet<RedisChannel> _subscripions;
        public int SubscriptionCount => _subscripions?.Count ?? 0;
        internal int Subscribe(RedisChannel channel)
        {
            if (_subscripions == null) _subscripions = new HashSet<RedisChannel>();
            _subscripions.Add(channel);
            return _subscripions.Count;
        }
        internal int Unsubscribe(RedisChannel channel)
        {
            if (_subscripions == null) return 0;
            _subscripions.Remove(channel);
            return _subscripions.Count;
        }
        public int Database { get; set; }
        public string Name { get; set; }
        internal IDuplexPipe LinkedPipe { get; set; }
        public bool Closed { get; internal set; }
        public int Id { get; internal set; }
        public bool IsAuthenticated { get; internal set; }
        public RedisProtocol Protocol { get; internal set; } = RedisProtocol.Resp2;
        public long ProtocolVersion => Protocol is RedisProtocol.Resp2 ? 2 : 3;

        public void Dispose()
        {
            Closed = true;
            var pipe = LinkedPipe;
            LinkedPipe = null;
            if (pipe != null)
            {
                try { pipe.Input.CancelPendingRead(); } catch { }
                try { pipe.Input.Complete(); } catch { }
                try { pipe.Output.CancelPendingFlush(); } catch { }
                try { pipe.Output.Complete(); } catch { }
                if (pipe is IDisposable d) try { d.Dispose(); } catch { }
            }
        }

        private int _activeSlot = ServerSelectionStrategy.NoSlot;
        internal void ResetAfterRequest() => _activeSlot = ServerSelectionStrategy.NoSlot;
        public virtual void OnKey(in RedisKey key, KeyFlags flags)
        {
            if ((flags & KeyFlags.NoSlotCheck) == 0 & node.CheckCrossSlot)
            {
                var slot = RespServer.GetHashSlot(key);
                if (_activeSlot is ServerSelectionStrategy.NoSlot)
                {
                    _activeSlot = slot;
                }
                else if (_activeSlot != slot)
                {
                    CrossSlotException.Throw();
                }
            }
            // ASKING here?
            node.AssertKey(key);

            if ((flags & KeyFlags.ReadOnly) == 0) node.Touch(Database, key);
        }

        public void Touch(int database, in RedisKey key)
        {
            TransactionState failureState = TransactionState.WatchDoomed;
            switch (_transactionState)
            {
                case TransactionState.WatchHopeful:
                    if (_watching.Contains(new(database, key)))
                    {
                        _transactionState = failureState;
                        _watching.Clear();
                    }
                    break;
                case TransactionState.MultiHopeful:
                    failureState = TransactionState.MultiDoomedByTouch;
                    _transaction?.Clear();
                    goto case TransactionState.WatchHopeful;
            }
        }

        public bool Watch(in RedisKey key)
        {
            switch (_transactionState)
            {
                case TransactionState.None:
                    _transactionState = TransactionState.WatchHopeful;
                    goto case TransactionState.WatchHopeful;
                case TransactionState.WatchHopeful:
                    _watching.Add(new(Database, key));
                    return true;
                case TransactionState.WatchDoomed:
                case TransactionState.MultiDoomedByTouch:
                    // no point tracking, just pretend
                    return true;
                default:
                    // can't watch inside multi
                    return false;
            }
        }

        public bool Unwatch()
        {
            switch (_transactionState)
            {
                case TransactionState.MultiHopeful:
                case TransactionState.MultiDoomedByTouch:
                case TransactionState.MultiAbortByError:
                    return false;
                default:
                    _watching.Clear();
                    _transactionState = TransactionState.None;
                    return true;
            }
        }

        private TransactionState _transactionState;

        private enum TransactionState
        {
            None,
            WatchHopeful,
            WatchDoomed,
            MultiHopeful,
            MultiDoomedByTouch,
            MultiAbortByError,
        }

        private readonly struct DatabaseKey(int db, in RedisKey key) : IEquatable<DatabaseKey>
        {
            public readonly int Db = db;
            public readonly RedisKey Key = key;
            public override int GetHashCode() => unchecked((Db * 397) ^ Key.GetHashCode());
            public override bool Equals(object obj) => obj is DatabaseKey other && Equals(other);
            public bool Equals(DatabaseKey other) => Db == other.Db && Key.Equals(other.Key);
        }
        private readonly HashSet<DatabaseKey> _watching = [];

        public bool Multi()
        {
            switch (_transactionState)
            {
                case TransactionState.None:
                case TransactionState.WatchHopeful:
                    _transactionState = TransactionState.MultiHopeful;
                    return true;
                case TransactionState.WatchDoomed:
                    _transactionState = TransactionState.MultiDoomedByTouch;
                    return true;
                default:
                    return false;
            }
        }

        public bool Discard()
        {
            switch (_transactionState)
            {
                case TransactionState.MultiHopeful:
                case TransactionState.MultiDoomedByTouch:
                    _transactionState = TransactionState.None;
                    _watching.Clear();
                    _transaction?.Clear();
                    return true;
                case TransactionState.MultiAbortByError:
                    return true;
                default:
                    return false;
            }
        }

        public void ExecAbort()
        {
            switch (_transactionState)
            {
                case TransactionState.MultiHopeful:
                case TransactionState.MultiDoomedByTouch:
                    _transactionState = TransactionState.MultiAbortByError;
                    _watching.Clear();
                    _transaction?.Clear();
                    break;
            }
        }

        public enum ExecResult
        {
            NotInTransaction,
            WatchConflict,
            AbortedByError,
            CommandsReturned,
        }

        public ExecResult FlushMulti(out byte[][] commands)
        {
            commands = [];
            switch (_transactionState)
            {
                case TransactionState.MultiHopeful:
                    _transactionState = TransactionState.None;
                    _watching.Clear();
                    commands = _transaction?.ToArray() ?? [];
                    _transaction?.Clear();
                    return ExecResult.CommandsReturned;
                case TransactionState.MultiDoomedByTouch:
                    _transactionState = TransactionState.None;
                    return ExecResult.WatchConflict;
                case TransactionState.MultiAbortByError:
                    _transactionState = TransactionState.None;
                    return ExecResult.AbortedByError;
                default:
                    return ExecResult.NotInTransaction;
            }
        }

        // completely unoptimized for now; this is fine
        private List<byte[]> _transaction; // null until needed

        internal bool BufferMulti(in RedisRequest request, in CommandBytes command)
        {
            switch (_transactionState)
            {
                case TransactionState.MultiHopeful when !AllowInTransaction(command):
                    // TODO we also can't do this bit! just store the command name for now
                    (_transaction ??= []).Add(Encoding.ASCII.GetBytes(request.GetString(0)));
                    return true;
                case TransactionState.MultiAbortByError when !AllowInTransaction(command):
                case TransactionState.MultiDoomedByTouch when !AllowInTransaction(command):
                    // don't buffer anything, just pretend
                    return true;
                default:
                    return false;
            }

            static bool AllowInTransaction(in CommandBytes cmd)
                => cmd.Equals(EXEC) || cmd.Equals(DISCARD) || cmd.Equals(MULTI)
                   || cmd.Equals(WATCH) || cmd.Equals(UNWATCH);
        }

        private static readonly CommandBytes
            EXEC = new("EXEC"u8), DISCARD = new("DISCARD"u8), MULTI = new("MULTI"u8),
            WATCH = new("WATCH"u8), UNWATCH = new("UNWATCH"u8);
    }

    internal sealed class CrossSlotException : Exception
    {
        public static void Throw() => throw new CrossSlotException();
    }
}
