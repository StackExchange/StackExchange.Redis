using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis.Server
{
    public partial class RedisClient(RedisServer.Node node) : IDisposable
#pragma warning disable SA1001
        #if NET
        , ISpanFormattable
#else
        , IFormattable
        #endif
#pragma warning restore SA1001
    {
        private RespScanState _readState;

        public override string ToString()
        {
            if (Protocol is RedisProtocol.Resp2)
            {
                return IsSubscriber ? $"{node.Host}:{node.Port} #{Id}:sub" : $"{node.Host}:{node.Port} #{Id}";
            }
            return $"{node.Host}:{node.Port} #{Id}:r3";
        }

        string IFormattable.ToString(string format, IFormatProvider formatProvider) => ToString();
#if NET
        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider provider)
        {
            charsWritten = 0;
            if (!(TryWrite(ref destination, node.Host.AsSpan(), ref charsWritten)
                    && TryWrite(ref destination, ":".AsSpan(), ref charsWritten)
                    && TryWriteInt32(ref destination, node.Port, ref charsWritten)
                    && TryWrite(ref destination, " #".AsSpan(), ref charsWritten)
                    && TryWriteInt32(ref destination, Id, ref charsWritten)))
            {
                return false;
            }
            if (Protocol is RedisProtocol.Resp2)
            {
                if (IsSubscriber)
                {
                    if (!TryWrite(ref destination, ":sub".AsSpan(), ref charsWritten)) return false;
                }
            }
            else
            {
                if (!TryWrite(ref destination, ":r3".AsSpan(), ref charsWritten)) return false;
            }
            return true;

            static bool TryWrite(ref Span<char> destination, ReadOnlySpan<char> value, ref int charsWritten)
            {
                if (value.Length > destination.Length)
                {
                    return false;
                }
                value.CopyTo(destination);
                destination = destination.Slice(value.Length);
                charsWritten += value.Length;
                return true;
            }
            static bool TryWriteInt32(ref Span<char> destination, int value, ref int charsWritten)
            {
                if (!value.TryFormat(destination, out var len))
                {
                    return false;
                }
                destination = destination.Slice(len);
                charsWritten += len;
                return true;
            }
        }
#endif

        public bool TryReadRequest(ReadOnlySequence<byte> data, out long consumed)
        {
            // skip past data we've already read
            data = data.Slice(_readState.TotalBytes);
            var status = RespFrameScanner.Default.TryRead(ref _readState, data);
            consumed = _readState.TotalBytes;
            switch (status)
            {
                case OperationStatus.Done:
                    _readState = default; // reset ready for the next frame
                    return true;
                case OperationStatus.NeedMoreData:
                    consumed = 0;
                    return false;
                default:
                    throw new InvalidOperationException($"Unexpected status: {status}");
            }
        }

        public RedisServer.Node Node => node;
        public int SkipReplies { get; set; }
        public void SkipAllReplies() => SkipReplies = -1;
        internal bool ShouldSkipResponse()
        {
            if (SkipReplies > 0) // skips N
            {
                SkipReplies--;
                return true;
            }
            return SkipReplies < 0; // skips forever
        }

        public int Database { get; set; }
        public string Name { get; set; }
        internal IDuplexPipe LinkedPipe { get; set; }
        public bool Closed { get; internal set; }
        public int Id { get; internal set; }
        public bool IsAuthenticated { get; internal set; }
        public RedisProtocol Protocol { get; internal set; } = RedisProtocol.Resp2;

        private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(node.Server.Lifetime);

        public CancellationToken Lifetime => _lifetime.Token;

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
            _lifetime.Dispose();
            _readState = default;
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

        internal bool BufferMulti(in RedisRequest request, in AsciiHash command)
        {
            switch (_transactionState)
            {
                case TransactionState.MultiHopeful when !AllowInTransaction(command):
                    (_transaction ??= []).Add(request.Serialize());
                    return true;
                case TransactionState.MultiAbortByError when !AllowInTransaction(command):
                case TransactionState.MultiDoomedByTouch when !AllowInTransaction(command):
                    // don't buffer anything, just pretend
                    return true;
                default:
                    return false;
            }

            static bool AllowInTransaction(in AsciiHash cmd)
                => cmd.Equals(EXEC) || cmd.Equals(DISCARD) || cmd.Equals(MULTI)
                   || cmd.Equals(WATCH) || cmd.Equals(UNWATCH);
        }

        private static readonly AsciiHash
            EXEC = new("EXEC"u8), DISCARD = new("DISCARD"u8), MULTI = new("MULTI"u8),
            WATCH = new("WATCH"u8), UNWATCH = new("UNWATCH"u8);
    }

    internal sealed class CrossSlotException : Exception
    {
        private CrossSlotException() { }
        public static void Throw() => throw new CrossSlotException();
    }
}
