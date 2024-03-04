using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal class RedisTransaction : RedisDatabase, ITransaction
    {
        private List<ConditionResult>? _conditions;
        private List<QueuedMessage>? _pending;
        private object SyncLock => this;

        public RedisTransaction(RedisDatabase wrapped, object? asyncState) : base(wrapped.multiplexer, wrapped.Database, asyncState ?? wrapped.AsyncState)
        {
            // need to check we can reliably do this...
            var commandMap = multiplexer.CommandMap;
            commandMap.AssertAvailable(RedisCommand.MULTI);
            commandMap.AssertAvailable(RedisCommand.EXEC);
            commandMap.AssertAvailable(RedisCommand.DISCARD);
        }

        public ConditionResult AddCondition(Condition condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            var commandMap = multiplexer.CommandMap;
            lock (SyncLock)
            {
                if (_conditions == null)
                {
                    // we don't demand these unless the user is requesting conditions, but we need both...
                    commandMap.AssertAvailable(RedisCommand.WATCH);
                    commandMap.AssertAvailable(RedisCommand.UNWATCH);
                    _conditions = new List<ConditionResult>();
                }
                condition.CheckCommands(commandMap);
                var result = new ConditionResult(condition);
                _conditions.Add(result);
                return result;
            }
        }

        public void Execute() => Execute(CommandFlags.FireAndForget);

        public bool Execute(CommandFlags flags)
        {
            var msg = CreateMessage(flags, out ResultProcessor<bool>? proc);
            return base.ExecuteSync(msg, proc); // need base to avoid our local "not supported" override
        }

        public Task<bool> ExecuteAsync(CommandFlags flags)
        {
            var msg = CreateMessage(flags, out ResultProcessor<bool>? proc);
            return base.ExecuteAsync(msg, proc); // need base to avoid our local wrapping override
        }

        internal override Task<T> ExecuteAsync<T>(Message? message, ResultProcessor<T>? processor, T defaultValue, ServerEndPoint? server = null)
        {
            if (message == null) return CompletedTask<T>.FromDefault(defaultValue, asyncState);
            multiplexer.CheckMessage(message);

            multiplexer.Trace("Wrapping " + message.Command, "Transaction");
            // prepare the inner command as a task
            Task<T> task;
            if (message.IsFireAndForget)
            {
                task = CompletedTask<T>.FromDefault(defaultValue, null); // F+F explicitly does not get async-state
            }
            else
            {
                var source = TaskResultBox<T>.Create(out var tcs, asyncState);
                message.SetSource(source, processor);
                task = tcs.Task;
            }

            QueueMessage(message);

            return task;
        }

        internal override Task<T?> ExecuteAsync<T>(Message? message, ResultProcessor<T>? processor, ServerEndPoint? server = null) where T : default
        {
            if (message == null) return CompletedTask<T>.Default(asyncState);
            multiplexer.CheckMessage(message);

            multiplexer.Trace("Wrapping " + message.Command, "Transaction");
            // prepare the inner command as a task
            Task<T?> task;
            if (message.IsFireAndForget)
            {
                task = CompletedTask<T>.Default(null); // F+F explicitly does not get async-state
            }
            else
            {
                var source = TaskResultBox<T?>.Create(out var tcs, asyncState);
                message.SetSource(source!, processor);
                task = tcs.Task;
            }

            QueueMessage(message);

            return task;
        }

        private void QueueMessage(Message message)
        {
            // prepare an outer-command that decorates that, but expects QUEUED
            var queued = new QueuedMessage(message);
            var wasQueued = SimpleResultBox<bool>.Create();
            queued.SetSource(wasQueued, QueuedProcessor.Default);

            // store it, and return the task of the *outer* command
            // (there is no task for the inner command)
            lock (SyncLock)
            {
                (_pending ??= new List<QueuedMessage>()).Add(queued);
                switch (message.Command)
                {
                    case RedisCommand.UNKNOWN:
                    case RedisCommand.EVAL:
                    case RedisCommand.EVALSHA:
                        var server = multiplexer.SelectServer(message);
                        if (server != null && server.SupportsDatabases)
                        {
                            // people can do very naughty things in an EVAL
                            // including change the DB; change it back to what we
                            // think it should be!
                            var sel = PhysicalConnection.GetSelectDatabaseCommand(message.Db);
                            queued = new QueuedMessage(sel);
                            wasQueued = SimpleResultBox<bool>.Create();
                            queued.SetSource(wasQueued, QueuedProcessor.Default);
                            _pending.Add(queued);
                        }

                        break;
                }
            }
        }

        internal override T? ExecuteSync<T>(Message? message, ResultProcessor<T>? processor, ServerEndPoint? server = null, T? defaultValue = default) where T : default
        {
            throw new NotSupportedException("ExecuteSync cannot be used inside a transaction");
        }

        private Message? CreateMessage(CommandFlags flags, out ResultProcessor<bool>? processor)
        {
            List<ConditionResult>? cond;
            List<QueuedMessage>? work;
            lock (SyncLock)
            {
                work = _pending;
                _pending = null; // any new operations go into a different queue
                cond = _conditions;
                _conditions = null; // any new conditions go into a different queue
            }
            if ((work == null || work.Count == 0) && (cond == null || cond.Count == 0))
            {
                if ((flags & CommandFlags.FireAndForget) != 0)
                {
                    processor = null;
                    return null; // they won't notice if we don't do anything...
                }
                processor = ResultProcessor.DemandPONG;
                return Message.Create(-1, flags, RedisCommand.PING);
            }
            processor = TransactionProcessor.Default;
            return new TransactionMessage(Database, flags, cond, work);
        }

        private class QueuedMessage : Message
        {
            public Message Wrapped { get; }
            private volatile bool wasQueued;

            public QueuedMessage(Message message) : base(message.Db, message.Flags | CommandFlags.NoRedirect, message.Command)
            {
                message.SetNoRedirect();
                Wrapped = message;
            }

            public bool WasQueued
            {
                get => wasQueued;
                set => wasQueued = value;
            }

            protected override void WriteImpl(PhysicalConnection physical)
            {
                Wrapped.WriteTo(physical);
                Wrapped.SetRequestSent();
            }
            public override int ArgCount => Wrapped.ArgCount;
            public override string CommandAndKey => Wrapped.CommandAndKey;
            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
                => Wrapped.GetHashSlot(serverSelectionStrategy);
        }

        private class QueuedProcessor : ResultProcessor<bool>
        {
            public static readonly ResultProcessor<bool> Default = new QueuedProcessor();

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeBulkString == ResultType.SimpleString && result.IsEqual(CommonReplies.QUEUED))
                {
                    if (message is QueuedMessage q)
                    {
                        connection?.BridgeCouldBeNull?.Multiplexer?.OnTransactionLog("Observed QUEUED for " + q.Wrapped?.CommandAndKey);
                        q.WasQueued = true;
                    }
                    return true;
                }
                return false;
            }
        }

        private class TransactionMessage : Message, IMultiMessage
        {
            private readonly ConditionResult[] conditions;
            public QueuedMessage[] InnerOperations { get; }

            public TransactionMessage(int db, CommandFlags flags, List<ConditionResult>? conditions, List<QueuedMessage>? operations)
                : base(db, flags, RedisCommand.EXEC)
            {
                InnerOperations = (operations?.Count > 0) ? operations.ToArray() : Array.Empty<QueuedMessage>();
                this.conditions = (conditions?.Count > 0) ? conditions.ToArray() : Array.Empty<ConditionResult>();
            }

            internal override void SetExceptionAndComplete(Exception exception, PhysicalBridge? bridge)
            {
                var inner = InnerOperations;
                if (inner != null)
                {
                    for(int i = 0; i < inner.Length;i++)
                    {
                        inner[i]?.Wrapped?.SetExceptionAndComplete(exception, bridge);
                    }
                }
                base.SetExceptionAndComplete(exception, bridge);
            }

            public bool IsAborted => command != RedisCommand.EXEC;

            public override void AppendStormLog(StringBuilder sb)
            {
                base.AppendStormLog(sb);
                if (conditions.Length != 0)
                {
                    sb.Append(", ").Append(conditions.Length).Append(" conditions");
                }
                sb.Append(", ").Append(InnerOperations.Length).Append(" operations");
            }

            public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
            {
                int slot = ServerSelectionStrategy.NoSlot;
                for (int i = 0; i < conditions.Length; i++)
                {
                    int newSlot = conditions[i].Condition.GetHashSlot(serverSelectionStrategy);
                    slot = ServerSelectionStrategy.CombineSlot(slot, newSlot);
                    if (slot == ServerSelectionStrategy.MultipleSlots) return slot;
                }
                for (int i = 0; i < InnerOperations.Length; i++)
                {
                    int newSlot = InnerOperations[i].Wrapped.GetHashSlot(serverSelectionStrategy);
                    slot = ServerSelectionStrategy.CombineSlot(slot, newSlot);
                    if (slot == ServerSelectionStrategy.MultipleSlots) return slot;
                }
                return slot;
            }

            public IEnumerable<Message> GetMessages(PhysicalConnection connection)
            {
                IResultBox? lastBox = null;
                var bridge = connection.BridgeCouldBeNull ?? throw new ObjectDisposedException(connection.ToString());

                bool explicitCheckForQueued = !bridge.ServerEndPoint.GetFeatures().ExecAbort;
                var multiplexer = bridge.Multiplexer;
                var sb = new StringBuilder();
                try
                {
                    try
                    {
                        // Important: if the server supports EXECABORT, then we can check the preconditions (pause there),
                        // which will usually be pretty small and cheap to do - if that passes, we can just issue all the commands
                        // and rely on EXECABORT to kick us if we are being idiotic inside the MULTI. However, if the server does
                        // *not* support EXECABORT, then we need to explicitly check for QUEUED anyway; we might as well defer
                        // checking the preconditions to the same time to avoid having to pause twice. This will mean that on
                        // up-version servers, precondition failures exit with UNWATCH; and on down-version servers precondition
                        // failures exit with DISCARD - but that's okay : both work fine

                        // PART 1: issue the preconditions
                        if (!IsAborted && conditions.Length != 0)
                        {
                            sb.AppendLine("issuing conditions...");
                            int cmdCount = 0;
                            for (int i = 0; i < conditions.Length; i++)
                            {
                                // need to have locked them before sending them
                                // to guarantee that we see the pulse
                                IResultBox latestBox = conditions[i].GetBox()!;
                                Monitor.Enter(latestBox);
                                if (lastBox != null) Monitor.Exit(lastBox);
                                lastBox = latestBox;
                                foreach (var msg in conditions[i].CreateMessages(Db))
                                {
                                    msg.SetNoRedirect(); // need to keep them in the current context only
                                    yield return msg;
                                    sb.Append("issuing ").AppendLine(msg.CommandAndKey);
                                    cmdCount++;
                                }
                            }
                            sb.Append("issued ").Append(conditions.Length).Append(" conditions (").Append(cmdCount).AppendLine(" commands)");

                            if (!explicitCheckForQueued && lastBox != null)
                            {
                                sb.AppendLine("checking conditions in the *early* path");
                                // need to get those sent ASAP; if they are stuck in the buffers, we die
                                multiplexer.Trace("Flushing and waiting for precondition responses");
#pragma warning disable CS0618 // Type or member is obsolete
                                connection.FlushSync(true, multiplexer.TimeoutMilliseconds); // make sure they get sent, so we can check for QUEUED (and the preconditions if necessary)
#pragma warning restore CS0618

                                if (Monitor.Wait(lastBox, multiplexer.TimeoutMilliseconds))
                                {
                                    if (!AreAllConditionsSatisfied(multiplexer))
                                        command = RedisCommand.UNWATCH; // somebody isn't happy

                                    sb.Append("after condition check, we are ").Append(command).AppendLine();
                                }
                                else
                                { // timeout running preconditions
                                    multiplexer.Trace("Timeout checking preconditions");
                                    command = RedisCommand.UNWATCH;

                                    sb.Append("timeout waiting for conditions, we are ").Append(command).AppendLine();
                                }
                                Monitor.Exit(lastBox);
                                lastBox = null;
                            }
                        }

                        // PART 2: begin the transaction
                        if (!IsAborted)
                        {
                            multiplexer.Trace("Beginning transaction");
                            yield return Message.Create(-1, CommandFlags.None, RedisCommand.MULTI);
                            sb.AppendLine("issued MULTI");
                        }

                        // PART 3: issue the commands
                        if (!IsAborted && InnerOperations.Length != 0)
                        {
                            multiplexer.Trace("Issuing operations...");

                            foreach (var op in InnerOperations)
                            {
                                if (explicitCheckForQueued)
                                {   // need to have locked them before sending them
                                    // to guarantee that we see the pulse
                                    IResultBox? thisBox = op.ResultBox;
                                    if (thisBox != null)
                                    {
                                        Monitor.Enter(thisBox);
                                        if (lastBox != null) Monitor.Exit(lastBox);
                                        lastBox = thisBox;
                                    }
                                }
                                yield return op;
                                sb.Append("issued ").AppendLine(op.CommandAndKey);
                            }
                            sb.Append("issued ").Append(InnerOperations.Length).AppendLine(" operations");

                            if (explicitCheckForQueued && lastBox != null)
                            {
                                sb.AppendLine("checking conditions in the *late* path");

                                multiplexer.Trace("Flushing and waiting for precondition+queued responses");
#pragma warning disable CS0618 // Type or member is obsolete
                                connection.FlushSync(true, multiplexer.TimeoutMilliseconds); // make sure they get sent, so we can check for QUEUED (and the preconditions if necessary)
#pragma warning restore CS0618
                                if (Monitor.Wait(lastBox, multiplexer.TimeoutMilliseconds))
                                {
                                    if (!AreAllConditionsSatisfied(multiplexer))
                                    {
                                        command = RedisCommand.DISCARD;
                                    }
                                    else
                                    {
                                        foreach (var op in InnerOperations)
                                        {
                                            if (!op.WasQueued)
                                            {
                                                multiplexer.Trace("Aborting: operation was not queued: " + op.Command);
                                                sb.Append("command was not issued: ").AppendLine(op.CommandAndKey);
                                                command = RedisCommand.DISCARD;
                                                break;
                                            }
                                        }
                                    }
                                    multiplexer.Trace("Confirmed: QUEUED x " + InnerOperations.Length);
                                    sb.Append("after condition check, we are ").Append(command).AppendLine();
                                }
                                else
                                {
                                    multiplexer.Trace("Aborting: timeout checking queued messages");
                                    command = RedisCommand.DISCARD;
                                    sb.Append("timeout waiting for conditions, we are ").Append(command).AppendLine();
                                }
                                Monitor.Exit(lastBox);
                                lastBox = null;
                            }
                        }
                    }
                    finally
                    {
                        if (lastBox != null) Monitor.Exit(lastBox);
                    }
                    if (IsAborted)
                    {
                        sb.Append("aborting ").Append(InnerOperations.Length).AppendLine(" wrapped commands...");
                        connection.Trace("Aborting: canceling wrapped messages");
                        foreach (var op in InnerOperations)
                        {
                            var inner = op.Wrapped;
                            inner.Cancel();
                            inner.Complete();
                        }
                    }
                    connection.Trace("End of transaction: " + Command);
                    sb.Append("issuing ").Append(Command).AppendLine();
                    yield return this; // acts as either an EXEC or an UNWATCH, depending on "aborted"
                }
                finally
                {
                    multiplexer.OnTransactionLog(sb.ToString());
                }
            }

            protected override void WriteImpl(PhysicalConnection physical) => physical.WriteHeader(Command, 0);

            public override int ArgCount => 0;

            private bool AreAllConditionsSatisfied(ConnectionMultiplexer multiplexer)
            {
                bool result = true;
                for (int i = 0; i < conditions.Length; i++)
                {
                    var condition = conditions[i];
                    if (condition.UnwrapBox())
                    {
                        multiplexer.Trace("Precondition passed: " + condition.Condition);
                    }
                    else
                    {
                        multiplexer.Trace("Precondition failed: " + condition.Condition);
                        result = false;
                    }
                }
                return result;
            }
        }

        private class TransactionProcessor : ResultProcessor<bool>
        {
            public static readonly TransactionProcessor Default = new();

            public override bool SetResult(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.IsError && message is TransactionMessage tran)
                {
                    string error = result.GetString()!;
                    foreach (var op in tran.InnerOperations)
                    {
                        var inner = op.Wrapped;
                        ServerFail(inner, error);
                        inner.Complete();
                    }
                }
                return base.SetResult(connection, message, result);
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                var muxer = connection.BridgeCouldBeNull?.Multiplexer;
                muxer?.OnTransactionLog($"got {result} for {message.CommandAndKey}");
                if (message is TransactionMessage tran)
                {
                    var wrapped = tran.InnerOperations;
                    switch (result.Resp2TypeArray)
                    {
                        case ResultType.SimpleString:
                            if (tran.IsAborted && result.IsEqual(CommonReplies.OK))
                            {
                                connection.Trace("Acknowledging UNWATCH (aborted electively)");
                                SetResult(message, false);
                                return true;
                            }
                            //EXEC returned with a NULL
                            if (!tran.IsAborted && result.IsNull)
                            {
                                connection.Trace("Server aborted due to failed EXEC");
                                //cancel the commands in the transaction and mark them as complete with the completion manager
                                foreach (var op in wrapped)
                                {
                                    var inner = op.Wrapped;
                                    inner.Cancel();
                                    inner.Complete();
                                }
                                SetResult(message, false);
                                return true;
                            }
                            break;
                        case ResultType.Array:
                            if (!tran.IsAborted)
                            {
                                var arr = result.GetItems();
                                if (result.IsNull)
                                {
                                    muxer?.OnTransactionLog("Aborting wrapped messages (failed watch)");
                                    connection.Trace("Server aborted due to failed WATCH");
                                    foreach (var op in wrapped)
                                    {
                                        var inner = op.Wrapped;
                                        inner.Cancel();
                                        inner.Complete();
                                    }
                                    SetResult(message, false);
                                    return true;
                                }
                                else if (wrapped.Length == arr.Length)
                                {
                                    connection.Trace("Server committed; processing nested replies");
                                    muxer?.OnTransactionLog($"Processing {arr.Length} wrapped messages");

                                    int i = 0;
                                    foreach(ref RawResult item in arr)
                                    {
                                        var inner = wrapped[i++].Wrapped;
                                        muxer?.OnTransactionLog($"> got {item} for {inner.CommandAndKey}");
                                        if (inner.ComputeResult(connection, in item))
                                        {
                                            inner.Complete();
                                        }
                                    }
                                    SetResult(message, true);
                                    return true;
                                }
                            }
                            break;
                    }
                    // even if we didn't fully understand the result, we still need to do something with
                    // the pending tasks
                    foreach (var op in wrapped)
                    {
                        if (op?.Wrapped is Message inner)
                        {
                            inner.Fail(ConnectionFailureType.ProtocolFailure, null, "Transaction failure", muxer);
                            inner.Complete();
                        }
                    }
                }
                return false;
            }
        }
    }
}
