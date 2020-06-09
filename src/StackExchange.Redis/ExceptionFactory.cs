using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;

namespace StackExchange.Redis
{
    internal static partial class ExceptionFactory
    {
        private const string
            DataCommandKey = "redis-command",
            DataSentStatusKey = "request-sent-status",
            DataServerKey = "redis-server",
            timeoutHelpLink = "https://stackexchange.github.io/StackExchange.Redis/Timeouts";

        internal static Exception AdminModeNotEnabled(bool includeDetail, RedisCommand command, Message message, ServerEndPoint server)
        {
            string s = GetLabel(includeDetail, command, message);
            var ex = new RedisCommandException("This operation is not available unless admin mode is enabled: " + s);
            if (includeDetail) AddExceptionDetail(ex, message, server, s);
            return ex;
        }

        internal static Exception CommandDisabled(RedisCommand command) => CommandDisabled(command.ToString());

        internal static Exception CommandDisabled(string command)
            => new RedisCommandException("This operation has been disabled in the command-map and cannot be used: " + command);

        internal static Exception TooManyArgs(string command, int argCount)
            => new RedisCommandException($"This operation would involve too many arguments ({argCount + 1} vs the redis limit of {PhysicalConnection.REDIS_MAX_ARGS}): {command}");

        internal static Exception ConnectionFailure(bool includeDetail, ConnectionFailureType failureType, string message, ServerEndPoint server)
        {
            var ex = new RedisConnectionException(failureType, message);
            if (includeDetail) AddExceptionDetail(ex, null, server, null);
            return ex;
        }

        internal static Exception DatabaseNotRequired(bool includeDetail, RedisCommand command)
        {
            string s = command.ToString();
            var ex = new RedisCommandException("A target database is not required for " + s);
            if (includeDetail) AddExceptionDetail(ex, null, null, s);
            return ex;
        }

        internal static Exception DatabaseOutfRange(bool includeDetail, int targetDatabase, Message message, ServerEndPoint server)
        {
            var ex = new RedisCommandException("The database does not exist on the server: " + targetDatabase);
            if (includeDetail) AddExceptionDetail(ex, message, server, null);
            return ex;
        }

        internal static Exception DatabaseRequired(bool includeDetail, RedisCommand command)
        {
            string s = command.ToString();
            var ex = new RedisCommandException("A target database is required for " + s);
            if (includeDetail) AddExceptionDetail(ex, null, null, s);
            return ex;
        }

        internal static Exception MasterOnly(bool includeDetail, RedisCommand command, Message message, ServerEndPoint server)
        {
            string s = GetLabel(includeDetail, command, message);
            var ex = new RedisCommandException("Command cannot be issued to a replica: " + s);
            if (includeDetail) AddExceptionDetail(ex, message, server, s);
            return ex;
        }

        internal static Exception MultiSlot(bool includeDetail, Message message)
        {
            var ex = new RedisCommandException("Multi-key operations must involve a single slot; keys can use 'hash tags' to help this, i.e. '{/users/12345}/account' and '{/users/12345}/contacts' will always be in the same slot");
            if (includeDetail) AddExceptionDetail(ex, message, null, null);
            return ex;
        }

        internal static string GetInnerMostExceptionMessage(Exception e)
        {
            if (e == null)
            {
                return "";
            }
            else
            {
                while (e.InnerException != null)
                {
                    e = e.InnerException;
                }
                return e.Message;
            }
        }

        internal static Exception NoConnectionAvailable(
            ConnectionMultiplexer multiplexer,
            Message message,
            ServerEndPoint server,
            ReadOnlySpan<ServerEndPoint> serverSnapshot = default,
            RedisCommand command = default)
        {
            string commandLabel = GetLabel(multiplexer.IncludeDetailInExceptions, message?.Command ?? command, message);

            if (server != null)
            {
                //if we already have the serverEndpoint for connection failure use that
                //otherwise it would output state of all the endpoints
                serverSnapshot = new ServerEndPoint[] { server };
            }

            var innerException = PopulateInnerExceptions(serverSnapshot == default ? multiplexer.GetServerSnapshot() : serverSnapshot);

            // Try to get a useful error message for the user.
            long attempts = multiplexer._connectAttemptCount, completions = multiplexer._connectCompletedCount;
            string initialMessage;
            // We only need to customize the connection if we're aborting on connect fail
            // The "never" case would have thrown, if this was true
            if (!multiplexer.RawConfig.AbortOnConnectFail && attempts <= multiplexer.RawConfig.ConnectRetry && completions == 0)
            {
                // Initial attempt, attempted use before an async connection completes
                initialMessage = $"Connection to Redis never succeeded (attempts: {attempts} - connection likely in-progress), unable to service operation: ";
            }
            else if (!multiplexer.RawConfig.AbortOnConnectFail && attempts > multiplexer.RawConfig.ConnectRetry && completions == 0)
            {
                // Attempted use after a full initial retry connect count # of failures
                // This can happen in Azure often, where user disables abort and has the wrong config
                initialMessage = $"Connection to Redis never succeeded (attempts: {attempts} - check your config), unable to service operation: ";
            }
            else
            {
                // Default if we don't have a more useful error message here based on circumstances
                initialMessage = "No connection is active/available to service this operation: ";
            }

            StringBuilder sb = new StringBuilder(initialMessage);
            sb.Append(commandLabel);
            string innermostExceptionstring = GetInnerMostExceptionMessage(innerException);
            if (!string.IsNullOrEmpty(innermostExceptionstring))
            {
                sb.Append("; ").Append(innermostExceptionstring);
            }

            // Add counters and exception data if we have it
            List<Tuple<string, string>> data = null;
            if (multiplexer.IncludeDetailInExceptions)
            {
                data = new List<Tuple<string, string>>();
                AddCommonDetail(data, sb, message, multiplexer, server);
            }
            var ex = new RedisConnectionException(ConnectionFailureType.UnableToResolvePhysicalConnection, sb.ToString(), innerException, message?.Status ?? CommandStatus.Unknown);
            if (multiplexer.IncludeDetailInExceptions)
            {
                CopyDataToException(data, ex);
                sb.Append("; ").Append(PerfCounterHelper.GetThreadPoolAndCPUSummary(multiplexer.IncludePerformanceCountersInExceptions));
                AddExceptionDetail(ex, message, server, commandLabel);
            }
            return ex;
        }

#pragma warning disable RCS1231 // Make parameter ref read-only. - spans are tiny!
        internal static Exception PopulateInnerExceptions(ReadOnlySpan<ServerEndPoint> serverSnapshot)
#pragma warning restore RCS1231 // Make parameter ref read-only.
        {
            var innerExceptions = new List<Exception>();

            if (serverSnapshot.Length > 0 && serverSnapshot[0].Multiplexer.LastException != null)
            {
                innerExceptions.Add(serverSnapshot[0].Multiplexer.LastException);
            }

            for (int i = 0; i < serverSnapshot.Length; i++)
            {
                if (serverSnapshot[i].LastException != null)
                {
                    var lastException = serverSnapshot[i].LastException;
                    innerExceptions.Add(lastException);
                }
            }

            if (innerExceptions.Count == 1)
            {
                return innerExceptions[0];
            }
            else if (innerExceptions.Count > 1)
            {
                return new AggregateException(innerExceptions);
            }
            return null;
        }

        internal static Exception NotSupported(bool includeDetail, RedisCommand command)
        {
            string s = GetLabel(includeDetail, command, null);
            var ex = new RedisCommandException("Command is not available on your server: " + s);
            if (includeDetail) AddExceptionDetail(ex, null, null, s);
            return ex;
        }

        internal static Exception NoCursor(RedisCommand command)
        {
            string s = GetLabel(false, command, null);
            return new RedisCommandException("Command cannot be used with a cursor: " + s);
        }

        private static string _libVersion;
        internal static string GetLibVersion()
        {
            if (_libVersion == null)
            {
                var assembly = typeof(ConnectionMultiplexer).Assembly;
                _libVersion = ((AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyFileVersionAttribute)))?.Version
                    ?? assembly.GetName().Version.ToString();
            }
            return _libVersion;
        }
        private static void Add(List<Tuple<string, string>> data, StringBuilder sb, string lk, string sk, string v)
        {
            if (v != null)
            {
                if (lk != null) data.Add(Tuple.Create(lk, v));
                if (sk != null) sb.Append(", ").Append(sk).Append(": ").Append(v);
            }
        }

        internal static Exception Timeout(ConnectionMultiplexer multiplexer, string baseErrorMessage, Message message, ServerEndPoint server, WriteResult? result = null)
        {
            List<Tuple<string, string>> data = new List<Tuple<string, string>> { Tuple.Create("Message", message.CommandAndKey) };
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(baseErrorMessage))
            {
                sb.Append(baseErrorMessage);
                if (message != null)
                {
                    sb.Append(", command=").Append(message.Command); // no key here, note
                }
            }
            else
            {
                sb.Append("Timeout performing ").Append(message.Command).Append(" (").Append(Format.ToString(multiplexer.TimeoutMilliseconds)).Append("ms)");
            }

            // Add timeout data, if we have it
            if (result == WriteResult.TimeoutBeforeWrite)
            {
                Add(data, sb, "Timeout", "timeout", Format.ToString(multiplexer.TimeoutMilliseconds));
                try
                {
#if DEBUG
                    if (message.QueuePosition >= 0) Add(data, sb, "QueuePosition", null, message.QueuePosition.ToString()); // the position the item was when added to the queue
                    if ((int)message.ConnectionWriteState >= 0) Add(data, sb, "WriteState", null, message.ConnectionWriteState.ToString()); // what the physical was doing when it was added to the queue
#endif
                    if (message != null && message.TryGetPhysicalState(out var ws, out var rs, out var sentDelta, out var receivedDelta))
                    {
                        Add(data, sb, "Write-State", null, ws.ToString());
                        Add(data, sb, "Read-State", null, rs.ToString());
                        // these might not always be available
                        if (sentDelta >= 0)
                        {
                            Add(data, sb, "OutboundDeltaKB", "outbound", $"{sentDelta >> 10}KiB");
                        }
                        if (receivedDelta >= 0)
                        {
                            Add(data, sb, "InboundDeltaKB", "inbound", $"{receivedDelta >> 10}KiB");
                        }
                    }
                }
                catch { }
            }

            AddCommonDetail(data, sb, message, multiplexer, server);

            sb.Append(" (Please take a look at this article for some common client-side issues that can cause timeouts: ");
            sb.Append(timeoutHelpLink);
            sb.Append(")");

            var ex = new RedisTimeoutException(sb.ToString(), message?.Status ?? CommandStatus.Unknown)
            {
                HelpLink = timeoutHelpLink
            };
            CopyDataToException(data, ex);

            if (multiplexer.IncludeDetailInExceptions) AddExceptionDetail(ex, message, server, null);
            return ex;
        }

        private static void CopyDataToException(List<Tuple<string, string>> data, Exception ex)
        {
            if (data != null)
            {
                var exData = ex.Data;
                foreach (var kv in data)
                {
                    exData["Redis-" + kv.Item1] = kv.Item2;
                }
            }
        }

        private static void AddCommonDetail(
            List<Tuple<string, string>> data,
            StringBuilder sb,
            Message message,
            ConnectionMultiplexer multiplexer,
            ServerEndPoint server
            )
        {
            if (message != null)
            {
                message.TryGetHeadMessages(out var now, out var next);
                if (now != null) Add(data, sb, "Message-Current", "active", multiplexer.IncludeDetailInExceptions ? now.CommandAndKey : now.Command.ToString());
                if (next != null) Add(data, sb, "Message-Next", "next", multiplexer.IncludeDetailInExceptions ? next.CommandAndKey : next.Command.ToString());
            }

            // Add server data, if we have it
            if (server != null && message != null)
            {
                server.GetOutstandingCount(message.Command, out int inst, out int qs, out long @in, out int qu, out bool aw, out long toRead, out long toWrite, out var bs, out var rs, out var ws);
                switch (rs)
                {
                    case PhysicalConnection.ReadStatus.CompletePendingMessageAsync:
                    case PhysicalConnection.ReadStatus.CompletePendingMessageSync:
                        sb.Append(" ** possible thread-theft indicated; see https://stackexchange.github.io/StackExchange.Redis/ThreadTheft ** ");
                        break;
                }
                Add(data, sb, "OpsSinceLastHeartbeat", "inst", inst.ToString());
                Add(data, sb, "Queue-Awaiting-Write", "qu", qu.ToString());
                Add(data, sb, "Queue-Awaiting-Response", "qs", qs.ToString());
                Add(data, sb, "Active-Writer", "aw", aw.ToString());
                if (qu != 0) Add(data, sb, "Backlog-Writer", "bw", bs.ToString());
                if (rs != PhysicalConnection.ReadStatus.NA) Add(data, sb, "Read-State", "rs", rs.ToString());
                if (ws != PhysicalConnection.WriteStatus.NA) Add(data, sb, "Write-State", "ws", ws.ToString());

                if (@in >= 0) Add(data, sb, "Inbound-Bytes", "in", @in.ToString());
                if (toRead >= 0) Add(data, sb, "Inbound-Pipe-Bytes", "in-pipe", toRead.ToString());
                if (toWrite >= 0) Add(data, sb, "Outbound-Pipe-Bytes", "out-pipe", toWrite.ToString());

                if (multiplexer.StormLogThreshold >= 0 && qs >= multiplexer.StormLogThreshold && Interlocked.CompareExchange(ref multiplexer.haveStormLog, 1, 0) == 0)
                {
                    var log = server.GetStormLog(message.Command);
                    if (string.IsNullOrWhiteSpace(log)) Interlocked.Exchange(ref multiplexer.haveStormLog, 0);
                    else Interlocked.Exchange(ref multiplexer.stormLogSnapshot, log);
                }
                Add(data, sb, "Server-Endpoint", "serverEndpoint", server.EndPoint.ToString().Replace("Unspecified/", ""));
            }
            Add(data, sb, "Multiplexer-Connects", "mc", $"{multiplexer._connectAttemptCount}/{multiplexer._connectCompletedCount}/{multiplexer._connectionCloseCount}");
            Add(data, sb, "Manager", "mgr", multiplexer.SocketManager?.GetState());

            Add(data, sb, "Client-Name", "clientName", multiplexer.ClientName);
            if (message != null)
            {
                var hashSlot = message.GetHashSlot(multiplexer.ServerSelectionStrategy);
                // only add keyslot if its a valid cluster key slot
                if (hashSlot != ServerSelectionStrategy.NoSlot)
                {
                    Add(data, sb, "Key-HashSlot", "PerfCounterHelperkeyHashSlot", message.GetHashSlot(multiplexer.ServerSelectionStrategy).ToString());
                }
            }
            int busyWorkerCount = PerfCounterHelper.GetThreadPoolStats(out string iocp, out string worker);
            Add(data, sb, "ThreadPool-IO-Completion", "IOCP", iocp);
            Add(data, sb, "ThreadPool-Workers", "WORKER", worker);
            data.Add(Tuple.Create("Busy-Workers", busyWorkerCount.ToString()));

            if (multiplexer.IncludePerformanceCountersInExceptions)
            {
                Add(data, sb, "Local-CPU", "Local-CPU", PerfCounterHelper.GetSystemCpuPercent());
            }

            Add(data, sb, "Version", "v", GetLibVersion());
        }

        private static void AddExceptionDetail(Exception exception, Message message, ServerEndPoint server, string label)
        {
            if (exception != null)
            {
                if (message != null)
                {
                    exception.Data.Add(DataCommandKey, message.CommandAndKey);
                    exception.Data.Add(DataSentStatusKey, message.Status);
                }
                else if (label != null)
                {
                    exception.Data.Add(DataCommandKey, label);
                }

                if (server != null) exception.Data.Add(DataServerKey, Format.ToString(server.EndPoint));
            }
        }

        private static string GetLabel(bool includeDetail, RedisCommand command, Message message)
        {
            return message == null ? command.ToString() : (includeDetail ? message.CommandAndKey : message.Command.ToString());
        }

        internal static Exception UnableToConnect(ConnectionMultiplexer muxer, string failureMessage=null)
        {
            var sb = new StringBuilder("It was not possible to connect to the redis server(s).");
            if (muxer != null)
            {
                if (muxer.AuthSuspect) sb.Append(" There was an authentication failure; check that passwords (or client certificates) are configured correctly.");
                else if (!muxer.RawConfig.AbortOnConnectFail) sb.Append(" Error connecting right now. To allow this multiplexer to continue retrying until it's able to connect, use abortConnect=false in your connection string or AbortOnConnectFail=false; in your code.");
            }
            if (!string.IsNullOrWhiteSpace(failureMessage)) sb.Append(" ").Append(failureMessage.Trim());

            return new RedisConnectionException(ConnectionFailureType.UnableToConnect, sb.ToString());
        }

        internal static Exception BeganProfilingWithDuplicateContext(object forContext)
        {
            var exc = new InvalidOperationException("Attempted to begin profiling for the same context twice");
            exc.Data["forContext"] = forContext;
            return exc;
        }

        internal static Exception FinishedProfilingWithInvalidContext(object forContext)
        {
            var exc = new InvalidOperationException("Attempted to finish profiling for a context which is no longer valid, or was never begun");
            exc.Data["forContext"] = forContext;
            return exc;
        }
    }
}
