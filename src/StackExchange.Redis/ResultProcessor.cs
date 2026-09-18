using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis
{
    internal abstract partial class ResultProcessor
    {
        /// <summary>
        /// If a reply is a NOSCRIPT error, note it on the message so the caller can re-issue as EVAL, and
        /// drop our cached hashes for the server.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every processor that can be the target of an EVALSHA needs this, not just the one returning
        /// <see cref="RedisResult"/>: without it the retry filters on <c>IsScriptUnavailable</c> can never
        /// match, and the NOSCRIPT surfaces to the caller.
        /// </para>
        /// <para>
        /// Returns whether <em>this</em> reply was a NOSCRIPT, which is not the same question as
        /// <c>message.IsScriptUnavailable</c>: that flag is set once and never cleared, so on a retry that
        /// comes back with some other error it still reads true. Callers deciding something about the reply
        /// in hand - rather than about the message's history - want this.
        /// </para>
        /// </remarks>
        /// <returns><c>true</c> if this reply was a NOSCRIPT error.</returns>
        private protected static bool NoteIfScriptUnavailable(PhysicalConnection connection, Message message, in RespReader errorReader)
        {
            if (errorReader.IsError && RedisErrorKindMetadata.Classify(errorReader) == RedisErrorKind.NoScript)
            {
                // scripts are not flushed individually, so assume the entire script cache is toast ("SCRIPT FLUSH").
                // Still true for the scripts we track, though the reasoning is subtler than when it was written:
                // since 7.4 the server DOES evict individually, but only scripts that arrived via EVAL/EVAL_RO -
                // and those are exactly the ones we never record as loaded and never address by hash, because
                // that path is NoScriptCache. So anything we track got there by SCRIPT LOAD, and its absence
                // still implies a wholesale event rather than eviction. See CommandFlags.NoScriptCache.
                connection.BridgeCouldBeNull?.ServerEndPoint?.FlushScriptCache();
                message.SetScriptUnavailable();
                return true;
            }

            return false;
        }

        /// <summary>The <c>NOSCRIPT</c> decision, in one place: note it, and say whether to try again.</summary>
        /// <remarks>
        /// <para>
        /// Three processors can be the target of an <c>EVALSHA</c> - the <c>RedisResult</c> one, the
        /// <c>RespResult</c> one, and the frame path's - and this rule is subtle enough that a third copy
        /// was where it would have gone wrong.
        /// </para>
        /// <para>
        /// The stickiness is the mechanism: <see cref="Message.IsScriptUnavailable"/> is read <b>before</b>
        /// noting, so a second <c>NOSCRIPT</c> for the same message finds the flag already set and reports
        /// rather than retrying. That is only load-bearing when the caller supplied a <i>hash</i>: given a
        /// body, the retry sends <c>EVAL</c> with it - because noting flushed the belief - and there is
        /// never a second <c>NOSCRIPT</c> to guard against.
        /// </para>
        /// </remarks>
        private protected static ReplyVerdict NoScriptVerdict(PhysicalConnection connection, Message message, in RespReader errorReader)
        {
            var alreadyTried = message.IsScriptUnavailable;
            return NoteIfScriptUnavailable(connection, message, in errorReader) && !alreadyTried
                ? ReplyVerdict.Reissue
                : ReplyVerdict.Complete;
        }

        public static readonly ResultProcessor<bool>
            Boolean = new BooleanProcessor(),
            DemandOK = new ExpectBasicStringProcessor(Literals.OK.Hash),
            HashImportOK = HashImportProcessor.Instance,
            DemandPONG = new ExpectBasicStringProcessor(Literals.PONG.Hash),
            DemandZeroOrOne = new DemandZeroOrOneProcessor(),
            AutoConfigure = new AutoConfigureProcessor(),
            TrackSubscriptions = new TrackSubscriptionsProcessor(null),
            Tracer = new TracerProcessor(false),
            EstablishConnection = new TracerProcessor(true),
            MaintenanceNotifications = new MaintenanceNotificationsProcessor(),
            BackgroundSaveStarted = new ExpectBasicStringProcessor(Literals.background_saving_started.Hash, startsWith: true),
            BackgroundSaveAOFStarted = new ExpectBasicStringProcessor(Literals.background_aof_rewriting_started.Hash, startsWith: true);

        public static readonly ResultProcessor<byte[]?>
            ByteArray = new ByteArrayProcessor();

        public static readonly ResultProcessor<byte[]>
            ScriptLoad = new ScriptLoadProcessor();

        public static readonly ResultProcessor<ClusterConfiguration>
            ClusterNodes = new ClusterNodesProcessor();

        internal static readonly ResultProcessor<ClusterSlotsResult?>
            ClusterSlots = ClusterSlotsResult.AutoConfigureProcessor;

        public static readonly ResultProcessor<EndPoint>
            ConnectionIdentity = new ConnectionIdentityProcessor();

        public static readonly ResultProcessor<DateTime>
            DateTime = new DateTimeProcessor();

        public static readonly ResultProcessor<DateTime?>
            NullableDateTimeFromMilliseconds = new NullableDateTimeProcessor(fromMilliseconds: true),
            NullableDateTimeFromSeconds = new NullableDateTimeProcessor(fromMilliseconds: false);

        public static readonly ResultProcessor<double>
                                            Double = new DoubleProcessor();
        public static readonly ResultProcessor<IGrouping<string, KeyValuePair<string, string>>[]>
            Info = new InfoProcessor();

        public static readonly MultiStreamProcessor
            MultiStream = new MultiStreamProcessor();

        public static readonly ResultProcessor<long>
            Int64 = new Int64Processor(),
            PubSubNumSub = new PubSubNumSubProcessor(),
            Int64DefaultNegativeOne = new Int64DefaultValueProcessor(-1);

        public static readonly ResultProcessor<int> Int32 = new Int32Processor();

        public static readonly ResultProcessor<double?>
                            NullableDouble = new NullableDoubleProcessor();

        public static readonly ResultProcessor<double?[]>
                            NullableDoubleArray = new NullableDoubleArrayProcessor();

        public static readonly ResultProcessor<long?>
            NullableInt64 = new NullableInt64Processor();

        public static readonly ResultProcessor<ExpireResult[]> ExpireResultArray = new ExpireResultArrayProcessor();

        public static readonly ResultProcessor<PersistResult[]> PersistResultArray = new PersistResultArrayProcessor();

        public static readonly ResultProcessor<RedisChannel[]>
            RedisChannelArrayLiteral = new RedisChannelArrayProcessor(RedisChannel.RedisChannelOptions.None);

        public static readonly ResultProcessor<RedisKey>
                    RedisKey = new RedisKeyProcessor();

        public static readonly ResultProcessor<RedisKey[]>
            RedisKeyArray = new RedisKeyArrayProcessor();

        public static readonly ResultProcessor<RedisType>
            RedisType = new RedisTypeProcessor();

        public static readonly ResultProcessor<RedisValue>
            RedisValue = new RedisValueProcessor();

        public static readonly ResultProcessor<RedisValue>
            RedisValueFromArray = new RedisValueFromArrayProcessor();

        public static readonly ResultProcessor<RedisValue[]>
            RedisValueArray = new RedisValueArrayProcessor();

        public static readonly ResultProcessor<long[]>
            Int64Array = new Int64ArrayProcessor();

        public static readonly ResultProcessor<string?[]>
            NullableStringArray = new NullableStringArrayProcessor();

        public static readonly ResultProcessor<string[]>
            StringArray = new StringArrayProcessor();

        public static readonly ResultProcessor<bool[]>
            BooleanArray = new BooleanArrayProcessor();

        public static readonly ResultProcessor<GeoPosition?[]>
            RedisGeoPositionArray = new RedisValueGeoPositionArrayProcessor();
        public static readonly ResultProcessor<GeoPosition?>
            RedisGeoPosition = new RedisValueGeoPositionProcessor();

        public static readonly ResultProcessor<TimeSpan>
            ResponseTimer = new TimingProcessor();

        public static readonly ResultProcessor<Role>
            Role = new RoleProcessor();

        public static readonly ResultProcessor<RedisResult>
            ScriptResult = new ScriptResultProcessor();

        public static readonly SortedSetEntryProcessor
            SortedSetEntry = new SortedSetEntryProcessor();
        public static readonly SortedSetEntryArrayProcessor
            SortedSetWithScores = new SortedSetEntryArrayProcessor();

        public static readonly SortedSetPopResultProcessor
            SortedSetPopResult = new SortedSetPopResultProcessor();

        public static readonly ListPopResultProcessor
            ListPopResult = new ListPopResultProcessor();

        public static readonly SingleStreamProcessor
            SingleStream = new SingleStreamProcessor();

        public static readonly SingleStreamProcessor
            SingleStreamWithNameSkip = new SingleStreamProcessor(skipStreamName: true);

        public static readonly StreamAutoClaimProcessor
            StreamAutoClaim = new StreamAutoClaimProcessor();

        public static readonly StreamAutoClaimIdsOnlyProcessor
            StreamAutoClaimIdsOnly = new StreamAutoClaimIdsOnlyProcessor();

        public static readonly StreamConsumerInfoProcessor
            StreamConsumerInfo = new StreamConsumerInfoProcessor();

        public static readonly StreamGroupInfoProcessor
            StreamGroupInfo = new StreamGroupInfoProcessor();

        public static readonly StreamInfoProcessor
            StreamInfo = new StreamInfoProcessor();

        public static readonly StreamPendingInfoProcessor
            StreamPendingInfo = new StreamPendingInfoProcessor();

        public static readonly StreamPendingMessagesProcessor
            StreamPendingMessages = new StreamPendingMessagesProcessor();

        public static ResultProcessor<GeoRadiusResult[]> GeoRadiusArray(GeoRadiusOptions options) => GeoRadiusResultArrayProcessor.Get(options);

        public static readonly ResultProcessor<LCSMatchResult>
            LCSMatchResult = new LongestCommonSubsequenceProcessor();

        public static readonly ResultProcessor<string?>
            String = new StringProcessor(),
            TieBreaker = new TieBreakerProcessor(),
            ClusterNodesRaw = new ClusterNodesRawProcessor();

        public static readonly ResultProcessor<EndPoint?>
            SentinelPrimaryEndpoint = new SentinelGetPrimaryAddressByNameProcessor();

        public static readonly ResultProcessor<EndPoint[]>
            SentinelAddressesEndPoints = new SentinelGetSentinelAddressesProcessor();

        public static readonly ResultProcessor<EndPoint[]>
            SentinelReplicaEndPoints = new SentinelGetReplicaAddressesProcessor();

        public static readonly ResultProcessor<KeyValuePair<string, string>[][]>
            SentinelArrayOfArrays = new SentinelArrayOfArraysProcessor();

        public static readonly ResultProcessor<KeyValuePair<string, string>[]>
            StringPairInterleaved = new StringPairInterleavedProcessor();
        public static readonly TimeSpanProcessor
            TimeSpanFromMilliseconds = new TimeSpanProcessor(true),
            TimeSpanFromSeconds = new TimeSpanProcessor(false);
        public static readonly HashEntryArrayProcessor
            HashEntryArray = new HashEntryArrayProcessor();

        // If the server reports max (i.e. FATAL), use int.MinValue as a similarly obviously bad value.
        internal static int ParseStreamDeliveryCount(long deliveryCount)
            => deliveryCount == long.MaxValue ? int.MinValue : checked((int)deliveryCount);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Conditionally run on instance")]
        public void ConnectionFail(Message message, ConnectionFailureType fail, Exception? innerException, string? annotation, ConnectionMultiplexer? muxer)
        {
            PhysicalConnection.IdentifyFailureType(innerException, ref fail);

            var sb = new StringBuilder(fail.ToString());
            if (message is not null)
            {
                sb.Append(" on ");
                sb.Append(muxer?.RawConfig.IncludeDetailInExceptions == true ? message.ToString() : message.ToStringCommandOnly());
            }
            if (!string.IsNullOrWhiteSpace(annotation))
            {
                sb.Append(", ");
                sb.Append(annotation);
            }
            var ex = new RedisConnectionException(fail, message?.Flags ?? CommandFlags.None, sb.ToString(), innerException);
            SetException(message, ex);
        }

        public static void ConnectionFail(Message message, ConnectionFailureType fail, string errorMessage) =>
            SetException(message, new RedisConnectionException(fail, message.Flags, errorMessage));

        public static void ServerFail(Message message, RedisErrorKind kind, string errorMessage) =>
            SetException(message, new RedisServerException(kind, message.Flags, errorMessage));

        public static void SetException(Message? message, Exception ex)
        {
            var box = message?.ResultBox;
            box?.SetException(ex);
        }

        /// <summary>
        /// See the reply before anything consumes it, to decide something about the <i>connection</i> or
        /// the <i>message</i> rather than to produce a result.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>SetResult</c> has always done two jobs - inspect, then parse - and the four processors that
        /// needed the first had to override the whole of it to get it: take a copy of the reader, advance
        /// the copy, look, then hand the <i>original</i> back to <c>base.SetResult</c>. Every one of them
        /// hand-rolled that rewind, and getting it wrong means parsing from the wrong position, which
        /// presents as somebody else's reply arriving for your command.
        /// </para>
        /// <para>
        /// The reader is passed by <c>in</c> and at the <b>start</b> of the reply, so an implementation
        /// copies it and advances the copy - the caller's position cannot be disturbed, which is the
        /// property the rewind dance was manually preserving.
        /// </para>
        /// <para>
        /// Inspection cannot yet <i>direct</i> what happens next; it can only record. The clearest cost of
        /// that is <c>NOSCRIPT</c>: the inspection sets a flag on the message, the task faults, and six
        /// <c>catch (RedisServerException) when (msg.IsScriptUnavailable)</c> sites re-issue - a verdict
        /// delivered by unwinding, because there is no way to say "reissue". Giving this a return value is
        /// the next step, and is why the seam is named rather than inlined.
        /// </para>
        /// </remarks>
        protected virtual ReplyVerdict Inspect(PhysicalConnection connection, Message message, in RespReader reader)
            => ReplyVerdict.Complete;

        /// <summary>What inspecting a reply concluded should happen to the message.</summary>
        internal enum ReplyVerdict
        {
            /// <summary>Carry on: parse the reply and complete the message.</summary>
            Complete = 0,

            /// <summary>
            /// Send this same message again, to the same endpoint, and do not complete it.
            /// </summary>
            /// <remarks>
            /// Only <c>NOSCRIPT</c> so far. The resend is the one <c>MOVED</c> has always used from this
            /// same read path - <c>PrepareToResend</c> then <c>TryWriteSync</c>, returning <c>false</c> from
            /// <c>SetResult</c> to mean "re-issued, do not complete" - rather than a second mechanism.
            /// </remarks>
            Reissue = 1,
        }

        /// <summary>Write the message again, to the endpoint that just answered.</summary>
        /// <remarks>
        /// Deliberately not <c>ServerSelectionStrategy.TryResend</c>, which is about <i>redirects</i>: it
        /// refuses a message with no hash slot - which a keyless script has - and sets asking/no-redirect
        /// on the way through. This is the same endpoint and the same message, with nothing to re-route.
        /// </remarks>
        private static bool TryReissue(PhysicalConnection connection, Message message)
        {
            var server = connection.BridgeCouldBeNull?.ServerEndPoint;
            if (server is null) return false;

            try
            {
                message.PrepareToResend(server, isMoved: false);
#pragma warning disable CS0618 // sync write is what the MOVED path uses from here too
                return server.TryWriteSync(message) == WriteResult.Success;
#pragma warning restore CS0618
            }
            catch
            {
                return false; // fall through to ordinary error handling, which still has the reply in hand
            }
        }

        // true if ready to be completed (i.e. false if re-issued to another server)
        public virtual bool SetResult(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            var verdict = Inspect(connection, message, in reader);
            if (verdict == ReplyVerdict.Reissue && TryReissue(connection, message))
            {
                // re-issued: this reply is spent, and the message now belongs to its next attempt
                return false;
            }

            reader.MovePastBof();
            connection.OnDetailLog($"(core result for {message.Command}, '{reader.GetOverview()}')");
            var bridge = connection.BridgeCouldBeNull;
            if (message is LoggingMessage logging)
            {
                try
                {
                    logging.Log?.LogInformationResponse(bridge?.Name, message.CommandAndKey, reader.GetOverview());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
            if (reader.IsError)
            {
                return HandleCommonError(message, reader, connection);
            }

            var copy = reader;
            if (SetResultCore(connection, message, ref reader))
            {
                bridge?.Multiplexer.Trace("Completed with success: " + copy.GetOverview() + " (" + GetType().Name + ")", ToString());
            }
            else
            {
                UnexpectedResponse(message, in copy);
            }
            return true;
        }

        private bool HandleCommonError(Message message, RespReader reader, PhysicalConnection connection)
        {
            connection.OnDetailLog($"applying common error-handling: {reader.GetOverview()}");
            var bridge = connection.BridgeCouldBeNull;
            var errorKind = RedisErrorKindMetadata.Classify(reader);
            switch (errorKind)
            {
                case RedisErrorKind.NoAuth:
                    bridge?.Multiplexer.SetAuthSuspect(new RedisServerException(errorKind, message.Flags, "NOAUTH Returned - connection has not yet authenticated"));
                    break;
                case RedisErrorKind.WrongPass:
                    bridge?.Multiplexer.SetAuthSuspect(new RedisServerException(errorKind, message.Flags, reader.GetOverview()));
                    break;
            }

            var server = bridge?.ServerEndPoint;
            bool log = !message.IsInternalCall;
            bool isMoved = errorKind == RedisErrorKind.Moved;
            bool wasNoRedirect = (message.Flags & CommandFlags.NoRedirect) != 0;
            string? err = string.Empty;
            bool unableToConnectError = false;
            if (isMoved || errorKind == RedisErrorKind.Ask)
            {
                connection.OnDetailLog($"redirect via {(isMoved ? "MOVED" : "ASK")} to '{reader.ReadString()}'");
                message.SetResponseReceived();

                log = false;
                string[] parts = reader.ReadString()!.Split(StringSplits.Space, 3);
                if (Format.TryParseInt32(parts[1], out int hashSlot)
                    && Format.TryParseEndPoint(parts[2], out var endpoint)
                    && IsUnroutableRedirect(endpoint))
                {
                    // the slot moved, but the reply does not say where to: a hostname-preferring node
                    // redirecting to a peer with no announced hostname reports "?", which denotes an
                    // *unknown* node and so cannot be resolved to the answering node either. Do not
                    // manufacture a ServerEndPoint for it - that would leave us dialling a host called "?"
                    // - but do ask for a topology refresh, which can name the target properly, since
                    // CLUSTER NODES reports addresses positionally whatever the preference
                    connection.OnDetailLog($"unroutable {(isMoved ? "MOVED" : "ASK")} target '{parts[2]}'");
                    bridge?.Multiplexer.ReconfigureIfNeeded(server?.EndPoint, false, "unroutable redirect");
                    errorKind = RedisErrorKind.UnknownRedirectTarget;
                    err = bridge?.Multiplexer.RawConfig.IncludeDetailInExceptions == true
                        ? $"The server redirected hashslot {hashSlot} to '{parts[2]}', which does not identify a node that can be connected to; a topology refresh has been requested. Command: {message.CommandAndKey}. "
                        : "The server redirected to an endpoint that does not identify a node that can be connected to; a topology refresh has been requested. ";
                }
                else if (Format.TryParseInt32(parts[1], out hashSlot)
                    && Format.TryParseEndPoint(parts[2], out endpoint))
                {
                    // Check if MOVED points to same endpoint
                    bool isSameEndpoint = Equals(server?.EndPoint, endpoint);
                    if (isSameEndpoint && isMoved)
                    {
                        // MOVED to same endpoint detected.
                        // This occurs when Redis/Valkey servers are behind DNS records, load balancers, or proxies.
                        // The MOVED error signals that the client should reconnect to allow the DNS/proxy/load balancer
                        // to route the connection to a different underlying server host, then retry the command.
                        // Mark the bridge to reconnect - reader loop will handle disconnection and reconnection.
                        bridge?.MarkNeedsReconnect();
                    }
                    if (bridge is null)
                    {
                        // already toast
                        connection.OnDetailLog($"no bridge for {message.Command}");
                    }
                    else if (bridge.Multiplexer.TryResend(hashSlot, message, endpoint, isMoved, isSameEndpoint))
                    {
                        connection.OnDetailLog($"re-issued to {Format.ToString(endpoint)}");
                        bridge.Multiplexer.Trace(message.Command + " re-issued to " + endpoint, isMoved ? "MOVED" : "ASK");
                        return false;
                    }
                    else
                    {
                        connection.OnDetailLog($"unable to re-issue {message.Command} to {Format.ToString(endpoint)}; treating as error");
                        if (isMoved && wasNoRedirect)
                        {
                            if (bridge.Multiplexer.RawConfig.IncludeDetailInExceptions)
                            {
                                err = $"Key has MOVED to Endpoint {endpoint} and hashslot {hashSlot} but CommandFlags.NoRedirect was specified - redirect not followed for {message.CommandAndKey}. ";
                            }
                            else
                            {
                                err = "Key has MOVED but CommandFlags.NoRedirect was specified - redirect not followed. ";
                            }
                        }
                        else
                        {
                            unableToConnectError = true;
                            if (bridge.Multiplexer.RawConfig.IncludeDetailInExceptions)
                            {
                                err = $"Endpoint {endpoint} serving hashslot {hashSlot} is not reachable at this point of time. Please check connectTimeout value. If it is low, try increasing it to give the ConnectionMultiplexer a chance to recover from the network disconnect. "
                                      + PerfCounterHelper.GetThreadPoolAndCPUSummary();
                            }
                            else
                            {
                                err = "Endpoint is not reachable at this point of time. Please check connectTimeout value. If it is low, try increasing it to give the ConnectionMultiplexer a chance to recover from the network disconnect. ";
                            }
                        }
                    }
                }
                else
                {
                    connection.OnDetailLog($"unable to parse redirect response");
                }
            }

            if (string.IsNullOrWhiteSpace(err))
            {
                err = reader.ReadString()!;
            }

            if (log && server != null)
            {
                bridge?.Multiplexer.OnErrorMessage(server.EndPoint, err);
            }
            bridge?.Multiplexer.Trace("Completed with error: " + err + " (" + GetType().Name + ")", ToString());
            if (unableToConnectError)
            {
                ConnectionFail(message, ConnectionFailureType.UnableToConnect, err);
            }
            else
            {
                ServerFail(message, errorKind, err);
            }

            return true;
        }

        protected abstract bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader);

        // "?" is the documented placeholder for an unknown node, and a zero/absent port is what the
        // "unknown-endpoint" form parses to; neither can be dialled, and neither may be assumed to mean
        // "the node that answered" - see the CLUSTER SLOTS endpoint contract
        private static bool IsUnroutableRedirect(EndPoint endpoint) => endpoint switch
        {
            DnsEndPoint dns => dns.Port == 0 || dns.Host is "" or "?",
            IPEndPoint ip => ip.Port == 0,
            _ => false,
        };

        private void UnexpectedResponse(Message message, in RespReader reader)
        {
            ConnectionMultiplexer.TraceWithoutContext("From " + GetType().Name, "Unexpected Response");
            ConnectionFail(message, ConnectionFailureType.ProtocolFailure, "Unexpected response to " + (message?.CommandString ?? "n/a") + ": " + reader.GetOverview());
        }

        public sealed class TimeSpanProcessor : ResultProcessor<TimeSpan?>
        {
            private readonly bool isMilliseconds;
            public TimeSpanProcessor(bool isMilliseconds)
            {
                this.isMilliseconds = isMilliseconds;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    if (reader.IsNull)
                    {
                        SetResult(message, null);
                        return true;
                    }

                    if (reader.TryReadInt64(out long time))
                    {
                        TimeSpan? expiry;
                        if (time < 0)
                        {
                            expiry = null;
                        }
                        else if (isMilliseconds)
                        {
                            expiry = TimeSpan.FromMilliseconds(time);
                        }
                        else
                        {
                            expiry = TimeSpan.FromSeconds(time);
                        }
                        SetResult(message, expiry);
                        return true;
                    }
                }
                return false;
            }
        }

        public sealed class TimingProcessor : ResultProcessor<TimeSpan>
        {
            private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

            public static TimerMessage CreateMessage(int db, CommandFlags flags, RedisCommand command, RedisValue value = default) =>
                new TimerMessage(db, flags, command, value);

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // don't check the actual reply; there are multiple ways of constructing
                // a timing message, and we don't actually care about what approach was used
                TimeSpan duration;
                if (message is TimerMessage timingMessage)
                {
                    var timestampDelta = Stopwatch.GetTimestamp() - timingMessage.StartedWritingTimestamp;
                    var ticks = (long)(TimestampToTicks * timestampDelta);
                    duration = new TimeSpan(ticks);
                }
                else
                {
                    duration = TimeSpan.MaxValue;
                }
                SetResult(message, duration);
                return true;
            }

            internal sealed class TimerMessage : Message
            {
                public long StartedWritingTimestamp;
                private readonly RedisValue value;
                public TimerMessage(int db, CommandFlags flags, RedisCommand command, RedisValue value)
                    : base(db, flags, command)
                {
                    this.value = value;
                }

                protected override void WriteImpl(in MessageWriter writer)
                {
                    StartedWritingTimestamp = Stopwatch.GetTimestamp();
                    if (value.IsNull)
                    {
                        writer.WriteHeader(command, 0);
                    }
                    else
                    {
                        writer.WriteHeader(command, 1);
                        writer.WriteBulkString(value);
                    }
                }
                public override int ArgCount => value.IsNull ? 0 : 1;
            }
        }

        public sealed class TrackSubscriptionsProcessor : ResultProcessor<bool>
        {
            private ConnectionMultiplexer.Subscription? Subscription { get; }
            public TrackSubscriptionsProcessor(ConnectionMultiplexer.Subscription? sub) => Subscription = sub;

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    int length = reader.AggregateLength();
                    if (length >= 3)
                    {
                        var iter = reader.AggregateChildren();
                        // Skip first two elements
                        iter.DemandNext(); // [0]
                        iter.DemandNext(); // [1]
                        iter.DemandNext(); // [2] - the count

                        if (iter.Value.TryReadInt64(out long count))
                        {
                            connection.SubscriptionCount = count;
                            SetResult(message, true);

                            var ep = connection.BridgeCouldBeNull?.ServerEndPoint;
                            if (ep is not null)
                            {
                                switch (message.Command)
                                {
                                    case RedisCommand.SUBSCRIBE:
                                    case RedisCommand.SSUBSCRIBE:
                                    case RedisCommand.PSUBSCRIBE:
                                        Subscription?.AddEndpoint(ep);
                                        break;
                                    default:
                                        Subscription?.TryRemoveEndpoint(ep);
                                        break;
                                }
                            }
                            return true;
                        }
                    }
                }
                SetResult(message, false);
                return false;
            }
        }

        internal sealed class DemandZeroOrOneProcessor : ResultProcessor<bool>
        {
            public static bool TryGet(ref RespReader reader, out bool value)
            {
                if (reader.IsScalar && reader.ScalarLengthIs(1))
                {
                    var span = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(stackalloc byte[8]);
                    var byteValue = span[0];
                    if (byteValue == (byte)'1')
                    {
                        value = true;
                        return true;
                    }
                    if (byteValue == (byte)'0')
                    {
                        value = false;
                        return true;
                    }
                }
                value = false;
                return false;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (TryGet(ref reader, out bool value))
                {
                    SetResult(message, value);
                    return true;
                }
                return false;
            }
        }

        internal sealed class ScriptLoadProcessor : ResultProcessor<byte[]>
        {
            /// <summary>
            /// Anything hashed with SHA1 has exactly 40 characters. We can use that as a shortcut in the code bellow.
            /// </summary>
            private const int SHA1Length = 40;

            private static readonly Regex sha1 = new Regex("^[0-9a-f]{40}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            internal static bool IsSHA1(string? script) => script is not null && script.Length == SHA1Length && sha1.IsMatch(script);

            internal const int Sha1HashLength = 20;
            internal static byte[] ParseSHA1(ReadOnlySpan<byte> value)
            {
                static int FromHex(char c)
                {
                    if (c >= '0' && c <= '9') return c - '0';
                    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                    return -1;
                }

                if (value.Length == Sha1HashLength * 2)
                {
                    var tmp = new byte[Sha1HashLength];
                    int charIndex = 0;
                    for (int i = 0; i < tmp.Length; i++)
                    {
                        int x = FromHex((char)value[charIndex++]), y = FromHex((char)value[charIndex++]);
                        if (x < 0 || y < 0)
                        {
                            throw new ArgumentException("Unable to parse response as SHA1", nameof(value));
                        }
                        tmp[i] = (byte)((x << 4) | y);
                    }
                    return tmp;
                }
                throw new ArgumentException("Unable to parse response as SHA1", nameof(value));
            }

            // note that top-level error messages still get handled by SetResult, but nested errors
            // (is that a thing?) will be wrapped in the RedisResult
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // We expect a scalar with exactly 40 ASCII hex characters (20 bytes when parsed)
                if (reader.IsScalar && reader.ScalarLengthIs(Sha1HashLength * 2))
                {
                    var asciiHash = reader.ReadByteArray()!;

                    // External caller wants the hex bytes, not the ASCII bytes
                    // For nullability/consistency reasons, we always do the parse here.
                    byte[] hash;
                    try
                    {
                        hash = ParseSHA1(asciiHash);
                    }
                    catch (ArgumentException)
                    {
                        return false; // Invalid hex characters
                    }

                    if (message is RedisDatabase.ScriptLoadMessage sl)
                    {
                        connection.BridgeCouldBeNull?.ServerEndPoint?.AddScript(sl.Script, asciiHash);
                    }
                    SetResult(message, hash);
                    return true;
                }
                return false;
            }
        }

        internal sealed class SortedSetEntryProcessor : ResultProcessor<SortedSetEntry?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // the shape lives on the type it produces, so the interpolated surface's handler reads the
                // identical reply the identical way; see SortedSetEntry.Resp.cs
                if (!Redis.SortedSetEntry.TryRead(ref reader, out var result)) return false;

                SetResult(message, result);
                return true;
            }
        }

        internal sealed class SortedSetEntryArrayProcessor : ValuePairInterleavedProcessorBase<SortedSetEntry>
        {
            protected override SortedSetEntry Parse(ref RespReader first, ref RespReader second, object? state) =>
                new SortedSetEntry(first.ReadRedisValue(), second.TryReadDouble(out double val) ? val : double.NaN);
        }

        internal sealed class SortedSetPopResultProcessor : ResultProcessor<SortedSetPopResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // the shape lives on the type it produces, so the interpolated surface's handler reads the
                // identical reply the identical way; see SortedSetPopResult.Resp.cs
                if (!Redis.SortedSetPopResult.TryRead(ref reader, out var result)) return false;

                SetResult(message, result);
                return true;
            }
        }

        internal sealed class ListPopResultProcessor : ResultProcessor<ListPopResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // the shape lives on the type it produces, so the interpolated surface's handler reads the
                // identical reply the identical way; see ListPopResult.Resp.cs
                if (!Redis.ListPopResult.TryRead(ref reader, out var result)) return false;

                SetResult(message, result);
                return true;
            }
        }

        internal sealed class HashEntryArrayProcessor : ValuePairInterleavedProcessorBase<HashEntry>
        {
            protected override HashEntry Parse(ref RespReader first, ref RespReader second, object? state) =>
                new HashEntry(first.ReadRedisValue(), second.ReadRedisValue());
        }

        internal abstract class ValuePairInterleavedProcessorBase<T> : ResultProcessor<T[]>
        {
            // when RESP3 was added, some interleaved value/pair responses: became jagged instead;
            // this isn't strictly a RESP3 thing (RESP2 supports jagged), but: it is a thing that
            // happened, and we need to handle that; thus, by default, we'll detect jagged data
            // and handle it automatically; this virtual is included so we can turn it off
            // on a per-processor basis if needed
            protected virtual bool AllowJaggedPairs(RedisProtocol protocol) => protocol >= RedisProtocol.Resp3;

            /// <summary>Read the pairs, deciding the wire shape from the protocol's policy.</summary>
            public T[]? ParseArray(ref RespReader reader, RedisProtocol protocol, bool allowOversized, out int count, object? state)
                => ParseArray(ref reader, AllowJaggedPairs(protocol), allowOversized, out count, state);

            /// <summary>
            /// Read the pairs, being told outright whether jagged is permitted.
            /// </summary>
            /// <remarks>
            /// The protocol is never anything but a way of asking <see cref="AllowJaggedPairs"/> this
            /// question, so a caller that already knows the answer - or that has no connection to ask
            /// about, as the deferred reply shapes do not - says so directly rather than naming a
            /// protocol version it is not really claiming.
            /// </remarks>
            public T[]? ParseArray(ref RespReader reader, bool allowJagged, bool allowOversized, out int count, object? state)
            {
                if (reader.IsNull)
                {
                    count = 0;
                    return null;
                }

                // Get the aggregate length first
                count = reader.AggregateLength();
                if (count == 0)
                {
                    return [];
                }

                // Whether the bytes ARE jagged is RespReader.IsAllJaggedPairs - shared with the deferred
                // pair window (RespPairAggregate<T>), so the two paths cannot drift about what arrived.
                // Whether jagged is PERMITTED is the caller's policy, and arrives as allowJagged.
                bool isJagged = allowJagged && reader.IsAllJaggedPairs();

                if (isJagged)
                {
                    // Jagged format: [[k1, v1], [k2, v2], ...]
                    // Count is the number of pairs (outer array length)
                    var pairs = allowOversized ? ArrayPool<T>.Shared.Rent(count) : new T[count];
                    var iter = reader.AggregateChildren();
                    for (int i = 0; i < count; i++)
                    {
                        iter.DemandNext();

                        var pairIter = iter.Value.AggregateChildren();
                        pairIter.DemandNext();
                        var first = pairIter.Value;

                        pairIter.DemandNext();
                        var second = pairIter.Value;

                        pairs[i] = Parse(ref first, ref second, state);
                    }
                    return pairs;
                }
                else
                {
                    // Interleaved format: [k1, v1, k2, v2, ...]
                    // Count is half the array length (>> 1 discards odd element if present)
                    count >>= 1; // divide by 2
                    var pairs = allowOversized ? ArrayPool<T>.Shared.Rent(count) : new T[count];
                    var iter = reader.AggregateChildren();

                    for (int i = 0; i < count; i++)
                    {
                        iter.DemandNext();
                        var first = iter.Value;

                        iter.DemandNext();
                        var second = iter.Value;

                        pairs[i] = Parse(ref first, ref second, state);
                    }
                    return pairs;
                }
            }

            protected abstract T Parse(ref RespReader first, ref RespReader second, object? state);

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (!reader.IsAggregate)
                {
                    return false;
                }

                var pairs = ParseArray(ref reader, connection.Protocol.GetValueOrDefault(), false, out _, null);
                SetResult(message, pairs!);
                return true;
            }
        }

        internal sealed class AutoConfigureProcessor : ResultProcessor<bool>
        {
            private ILogger? Log { get; }
            public AutoConfigureProcessor(ILogger? log = null) => Log = log;

            protected override ReplyVerdict Inspect(PhysicalConnection connection, Message message, in RespReader reader)
            {
                var probe = reader;
                probe.MovePastBof();
                if (probe.IsError && RedisErrorKindMetadata.Classify(probe) == RedisErrorKind.ReadOnly)
                {
                    var bridge = connection.BridgeCouldBeNull;
                    if (bridge != null)
                    {
                        var server = bridge.ServerEndPoint;
                        Log?.LogInformationAutoConfiguredRoleReplica(new(server));
                        server.IsReplica = true;
                    }
                }

                return ReplyVerdict.Complete;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                var server = connection.BridgeCouldBeNull?.ServerEndPoint;
                if (server == null) return false;

                // Handle CLIENT command (returns integer client ID)
                if (message?.Command == RedisCommand.CLIENT && reader.Prefix == RespPrefix.Integer)
                {
                    if (reader.TryReadInt64(out long clientId))
                    {
                        connection.ConnectionId = clientId;
                        Log?.LogInformationAutoConfiguredClientConnectionId(new(server), clientId);
                        SetResult(message, true);
                        return true;
                    }
                    return false;
                }

                // Handle INFO command (returns bulk string)
                if (message?.Command == RedisCommand.INFO && reader.IsScalar)
                {
                    string? info = reader.ReadString();
                    if (string.IsNullOrWhiteSpace(info))
                    {
                        SetResult(message, true);
                        return true;
                    }
                    string? primaryHost = null, primaryPort = null;
                    bool roleSeen = false;
                    ProductVariant productVariant = ProductVariant.Redis;
                    string productVersion = "";

                    using (var infoReader = new StringReader(info))
                    {
                        while (infoReader.ReadLine() is { } line)
                        {
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("# "))
                            {
                                continue;
                            }

                            var idx = line.IndexOf(':');
                            if (idx < 0) continue;

                            if (!AutoConfigureInfoFieldMetadata.TryParse(line.AsSpan(0, idx), out AutoConfigureInfoField field))
                            {
                                continue;
                            }
                            var valSpan = line.AsSpan(idx + 1).Trim();

                            switch (field)
                            {
                                case AutoConfigureInfoField.Role:
                                    roleSeen = true;
                                    if (KnownRoleMetadata.TryParse(valSpan, out bool isReplica))
                                    {
                                        server.IsReplica = isReplica;
                                        Log?.LogInformationAutoConfiguredInfoRole(new(server), isReplica ? "replica" : "primary");
                                    }
                                    break;
                                case AutoConfigureInfoField.MasterHost:
                                    primaryHost = valSpan.ToString();
                                    break;
                                case AutoConfigureInfoField.MasterPort:
                                    primaryPort = valSpan.ToString();
                                    break;
                                case AutoConfigureInfoField.RedisVersion:
                                    if (Format.TryParseVersion(valSpan, out Version? version))
                                    {
                                        server.Version = version;
                                        Log?.LogInformationAutoConfiguredInfoVersion(new(server), version);
                                    }
                                    if (productVariant is ProductVariant.Redis)
                                    {
                                        // if we haven't already decided this is Garnet/Valkey, etc: capture the version string.
                                        productVersion = valSpan.ToString();
                                    }
                                    break;
                                case AutoConfigureInfoField.RedisMode:
                                case AutoConfigureInfoField.ServerMode:
                                    if (ServerTypeMetadata.TryParse(valSpan, out var serverType))
                                    {
                                        server.ServerType = serverType;
                                        Log?.LogInformationAutoConfiguredInfoServerType(new(server), serverType);
                                    }
                                    break;
                                case AutoConfigureInfoField.RunId:
                                    server.RunId = valSpan.ToString();
                                    break;
                                case AutoConfigureInfoField.GarnetVersion:
                                    productVariant = ProductVariant.Garnet;
                                    productVersion = valSpan.ToString();
                                    break;
                                case AutoConfigureInfoField.ValkeyVersion:
                                    productVariant = ProductVariant.Valkey;
                                    productVersion = valSpan.ToString();
                                    break;
                                case AutoConfigureInfoField.DragonflyVersion:
                                    productVariant = ProductVariant.Dragonfly;
                                    productVersion = valSpan.ToString();
                                    break;
                                case AutoConfigureInfoField.MemuraiVersion:
                                    productVariant = ProductVariant.Memurai;
                                    productVersion = valSpan.ToString();
                                    break;
                                case AutoConfigureInfoField.RedictVersion:
                                    productVariant = ProductVariant.Redict;
                                    productVersion = valSpan.ToString();
                                    break;
                                case AutoConfigureInfoField.Executable when valSpan.EndsWith("/keydb-server"):
                                    productVariant = ProductVariant.KeyDB;
                                    break;
                            }
                        }
                        if (roleSeen && Format.TryParseEndPoint(primaryHost!, primaryPort, out var sep))
                        {
                            // These are in the same section, if present
                            server.PrimaryEndPoint = sep;
                        }
                    }

                    // Set the product variant and version (this is deferred because there can be
                    // both redis_version:6.1.2 and whatever_version:12.3.4, in any order).
                    if (!string.IsNullOrWhiteSpace(productVersion))
                    {
                        server.SetProductVariant(productVariant, productVersion);
                    }
                }

                // Handle SENTINEL command (returns bulk string)
                if (message?.Command == RedisCommand.SENTINEL && reader.IsScalar)
                {
                    server.ServerType = ServerType.Sentinel;
                    Log?.LogInformationAutoConfiguredSentinelServerType(new(server));
                    SetResult(message, true);
                    return true;
                }

                // Handle CONFIG command (returns array of key-value pairs)
                if (message?.Command == RedisCommand.CONFIG && reader.IsAggregate)
                {
                    // ReSharper disable once HeuristicUnreachableCode - this is a compile-time Max(...)
                    var iter = reader.AggregateChildren();
                    while (iter.MoveNext())
                    {
                        var key = iter.Value;
                        ConfigField field;
                        unsafe
                        {
                            if (!key.TryParseScalar(&ConfigFieldMetadata.TryParse, out field))
                            {
                                field = ConfigField.Unknown;
                            }
                        }

                        if (!iter.MoveNext()) break;
                        var val = iter.Value;

                        switch (field)
                        {
                            case ConfigField.Timeout when val.TryReadInt64(out long i64):
                                // note the configuration is in seconds
                                int timeoutSeconds = checked((int)i64), targetSeconds;
                                if (timeoutSeconds > 0)
                                {
                                    if (timeoutSeconds >= 60)
                                    {
                                        targetSeconds = timeoutSeconds - 20; // time to spare...
                                    }
                                    else
                                    {
                                        targetSeconds = (timeoutSeconds * 3) / 4;
                                    }
                                    Log?.LogInformationAutoConfiguredConfigTimeout(new(server), targetSeconds);
                                    server.WriteEverySeconds = targetSeconds;
                                }
                                break;
                            case ConfigField.Databases when val.TryReadInt64(out long dbI64):
                                int dbCount = checked((int)dbI64);
                                Log?.LogInformationAutoConfiguredConfigDatabases(new(server), dbCount);
                                server.Databases = dbCount;
                                if (dbCount > 1)
                                {
                                    connection.MultiDatabasesOverride = true;
                                }
                                break;
                            case ConfigField.SlaveReadOnly:
                            case ConfigField.ReplicaReadOnly:
                                YesNo yesNo;
                                unsafe
                                {
                                    if (val.TryParseScalar(&YesNoMetadata.TryParse, out yesNo))
                                    {
                                        switch (yesNo)
                                        {
                                            case YesNo.Yes:
                                                server.ReplicaReadOnly = true;
                                                Log?.LogInformationAutoConfiguredConfigReadOnlyReplica(
                                                    new(server),
                                                    true);
                                                break;
                                            case YesNo.No:
                                                server.ReplicaReadOnly = false;
                                                Log?.LogInformationAutoConfiguredConfigReadOnlyReplica(
                                                    new(server),
                                                    false);
                                                break;
                                        }
                                    }
                                }

                                break;
                        }
                    }
                    SetResult(message, true);
                    return true;
                }

                // Handle HELLO command (returns array/map of key-value pairs)
                if (message?.Command == RedisCommand.HELLO && reader.IsAggregate)
                {
                    var iter = reader.AggregateChildren();
                    while (iter.MoveNext())
                    {
                        HelloField field;
                        unsafe
                        {
                            if (!iter.Value.TryParseScalar(&HelloFieldMetadata.TryParse, out field))
                            {
                                field = HelloField.Unknown;
                            }

                            if (!iter.MoveNext()) break;

                            switch (field)
                            {
                                case HelloField.Version when iter.Value.TryParseScalar(Format.TryParseVersion!, out Version version):
                                    server.Version = version;
                                    Log?.LogInformationAutoConfiguredHelloServerVersion(new(server), version);
                                    break;
                                case HelloField.Proto when iter.Value.TryReadInt64(out var i64):
                                    connection.SetProtocol(i64 >= 3 ? RedisProtocol.Resp3 : RedisProtocol.Resp2);
                                    Log?.LogInformationAutoConfiguredHelloProtocol(
                                        new(server),
                                        connection.Protocol ?? RedisProtocol.Resp2);
                                    break;
                                case HelloField.Id when iter.Value.TryReadInt64(out var i64):
                                    connection.ConnectionId = i64;
                                    Log?.LogInformationAutoConfiguredHelloConnectionId(new(server), i64);
                                    break;
                                // note: "mode" and "role" describe a *server*, so we only trust them when we're
                                // talking to one directly; a proxy may answer HELLO from an arbitrary backend node
                                // (envoy 1.39 does), and taking that at face value would flip us into cluster mode
                                // or mark the proxy as a replica
                                case HelloField.Mode
                                    when server.ServerType.SupportsAutoConfigure()
                                        && iter.Value.TryParseScalar(&ServerTypeMetadata.TryParse, out ServerType serverType):
                                    server.ServerType = serverType;
                                    Log?.LogInformationAutoConfiguredHelloServerType(new(server), serverType);
                                    break;
                                case HelloField.Role
                                    when server.ServerType.SupportsAutoConfigure()
                                        && iter.Value.TryParseScalar(&KnownRoleMetadata.TryParse, out bool isReplica):
                                    server.IsReplica = isReplica;
                                    server.RoleKnownFromHello = true; // so we don't need the key-based fallback probe
                                    Log?.LogInformationAutoConfiguredHelloRole(
                                        new(server),
                                        isReplica ? "replica" : "primary");
                                    break;
                            }
                        }
                    }

                    SetResult(message, true);
                    return true;
                }

                return false;
            }

            internal static ResultProcessor<bool> Create(ILogger? log) => log is null ? AutoConfigure : new AutoConfigureProcessor(log);
        }

        private sealed class BooleanProcessor : ResultProcessor<bool>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsNull)
                {
                    SetResult(message, false); // lots of ops return (nil) when they mean "no"
                    return true;
                }

                if (reader.IsScalar)
                {
                    SetResult(message, reader.ReadBoolean());
                    return true;
                }

                if (reader.IsAggregate && reader.TryMoveNext() && reader.IsScalar)
                {
                    // treat an array of 1 like a single reply (for example, SCRIPT EXISTS)
                    var value = reader.ReadBoolean();
                    if (!reader.TryMoveNext())
                    {
                        SetResult(message, value);
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class ByteArrayProcessor : ResultProcessor<byte[]?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    SetResult(message, reader.ReadByteArray());
                    return true;
                }
                return false;
            }
        }

        private sealed class ClusterNodesProcessor : ResultProcessor<ClusterConfiguration>
        {
            internal static ClusterConfiguration Parse(PhysicalConnection connection, string nodes)
            {
                var bridge = connection.BridgeCouldBeNull ?? throw new ObjectDisposedException(connection.ToString());
                var server = bridge.ServerEndPoint;
                var config = new ClusterConfiguration(bridge.Multiplexer.ServerSelectionStrategy, nodes, server.EndPoint);
                server.SetClusterConfiguration(config);
                return config;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    string? nodes = reader.ReadString();
                    if (nodes is not null)
                    {
                        var bridge = connection.BridgeCouldBeNull;
                        var config = Parse(connection, nodes);

                        // re multi-db: https://github.com/StackExchange/StackExchange.Redis/issues/2642
                        if (bridge != null && !connection.MultiDatabasesOverride)
                        {
                            bridge.ServerEndPoint.ServerType = ServerType.Cluster;
                        }
                        SetResult(message, config);
                        return true;
                    }
                }
                return false;
            }
        }

        private sealed class ClusterNodesRawProcessor : ResultProcessor<string?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    var nodes = reader.ReadString();
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(nodes))
                        {
                            ClusterNodesProcessor.Parse(connection, nodes!);
                        }
                    }
                    catch
                    {
                        /* tralalalala */
                    }
                    SetResult(message, nodes);
                    return true;
                }
                return false;
            }
        }

        private sealed class ConnectionIdentityProcessor : ResultProcessor<EndPoint>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                SetResult(message, connection.BridgeCouldBeNull?.ServerEndPoint.EndPoint!);
                return true;
            }
        }

        private sealed class DateTimeProcessor : ResultProcessor<DateTime>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // Handle scalar integer (seconds since Unix epoch)
                if (reader.IsScalar && reader.TryReadInt64(out long unixTime))
                {
                    var time = RedisBase.UnixEpoch.AddSeconds(unixTime);
                    SetResult(message, time);
                    return true;
                }

                // Handle array (TIME command returns [seconds, microseconds])
                if (reader.IsAggregate && reader.TryMoveNext() && reader.IsScalar)
                {
                    if (reader.TryReadInt64(out unixTime))
                    {
                        // Check if there's a second element (microseconds)
                        if (!reader.TryMoveNext())
                        {
                            // Array of 1: just seconds
                            var time = RedisBase.UnixEpoch.AddSeconds(unixTime);
                            SetResult(message, time);
                            return true;
                        }

                        // Array of 2: seconds + microseconds - verify no third element
                        if (reader.IsScalar && reader.TryReadInt64(out long micros) && !reader.TryMoveNext())
                        {
                            var time = RedisBase.UnixEpoch.AddSeconds(unixTime).AddTicks(micros * 10); // DateTime ticks are 100ns
                            SetResult(message, time);
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        public sealed class NullableDateTimeProcessor : ResultProcessor<DateTime?>
        {
            private readonly bool isMilliseconds;
            public NullableDateTimeProcessor(bool fromMilliseconds) => isMilliseconds = fromMilliseconds;

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    // Handle null (e.g., OBJECT IDLETIME on a key that doesn't exist)
                    if (reader.IsNull)
                    {
                        SetResult(message, null);
                        return true;
                    }

                    // Handle integer (TTL/PTTL/EXPIRETIME commands)
                    if (reader.TryReadInt64(out var duration))
                    {
                        DateTime? expiry = duration switch
                        {
                            // -1 means no expiry and -2 means key does not exist
                            < 0 => null,
                            _ when isMilliseconds => RedisBase.UnixEpoch.AddMilliseconds(duration),
                            _ => RedisBase.UnixEpoch.AddSeconds(duration),
                        };
                        SetResult(message, expiry);
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class DoubleProcessor : ResultProcessor<double>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.Prefix is RespPrefix.Integer && reader.TryReadInt64(out long i64))
                {
                    SetResult(message, i64);
                    return true;
                }
                if (reader.IsScalar && reader.TryReadDouble(out double val))
                {
                    SetResult(message, val);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// As <see cref="DemandOK"/>, but also releases the message's rendered request buffer.
        /// </summary>
        /// <remarks>
        /// A dedicated processor rather than a change to the shared one: HIMPORT is the only command whose
        /// message renders its arguments up front *and* has no notion of being re-issued, so any reply at
        /// all - success or error - is the end of the road for its buffer. Releasing from the reply rather
        /// than from completion for the same reason as everything else here: a reply proves the write
        /// finished, which completion on its own does not.
        /// </remarks>
        private sealed class HashImportProcessor : ResultProcessor<bool>
        {
            /// <remarks>Not about the reply at all: the arrival of one is when the rendered arguments stop
            /// being needed, whatever it says.</remarks>
            protected override ReplyVerdict Inspect(PhysicalConnection connection, Message message, in RespReader reader)
            {
                if (message is IRenderedArgsOwner owner) owner.ReleaseRenderedArgs();
                return ReplyVerdict.Complete;
            }

            public override bool SetResult(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                Inspect(connection, message, in reader);
                return DemandOK.SetResult(connection, message, ref reader);
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader) =>
                throw new NotSupportedException(); // SetResult is fully overridden above

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1801", Justification = "n/a")]
            internal static readonly HashImportProcessor Instance = new();
        }

        private sealed class ExpectBasicStringProcessor : ResultProcessor<bool>
        {
            private readonly AsciiHash _expected;
            private readonly bool _startsWith;

            public ExpectBasicStringProcessor(in AsciiHash expected, bool startsWith = false)
            {
                _expected = expected;
                _startsWith = startsWith;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (!reader.IsScalar) return false;

                var expectedLength = _expected.Length;
                // For exact match, length must be exact
                if (_startsWith)
                {
                    if (reader.ScalarLength() < expectedLength) return false;
                }
                else
                {
                    if (!reader.ScalarLengthIs(expectedLength)) return false;
                }

                var bytes = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(stackalloc byte[_expected.BufferLength]);
                if (_startsWith) bytes = bytes.Slice(0, expectedLength);
                if (_expected.IsCS(bytes))
                {
                    SetResult(message, true);
                    return true;
                }

                if (message.Command == RedisCommand.AUTH) connection?.BridgeCouldBeNull?.Multiplexer?.SetAuthSuspect(new RedisException("Unknown AUTH exception"));
                return false;
            }
        }

        private sealed class InfoProcessor : ResultProcessor<IGrouping<string, KeyValuePair<string, string>>[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    string category = Normalize(null);
                    var list = new List<Tuple<string, KeyValuePair<string, string>>>();
                    if (!reader.IsNull)
                    {
                        using var stringReader = new StringReader(reader.ReadString()!);
                        while (stringReader.ReadLine() is string line)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            if (line.StartsWith("# "))
                            {
                                category = Normalize(line.Substring(2));
                                continue;
                            }
                            int idx = line.IndexOf(':');
                            if (idx < 0) continue;
                            var pair = new KeyValuePair<string, string>(
                                line.Substring(0, idx).Trim(),
                                line.Substring(idx + 1).Trim());
                            list.Add(Tuple.Create(category, pair));
                        }
                    }
                    var final = list.GroupBy(x => x.Item1, x => x.Item2).ToArray();
                    SetResult(message, final);
                    return true;
                }
                return false;
            }

            private static string Normalize(string? category) =>
                category.IsNullOrWhiteSpace() ? "miscellaneous" : category.Trim();
        }

        internal sealed class Int64DefaultValueProcessor : ResultProcessor<long>
        {
            private readonly long _defaultValue;

            public Int64DefaultValueProcessor(long defaultValue) => _defaultValue = defaultValue;

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsNull)
                {
                    SetResult(message, _defaultValue);
                    return true;
                }
                if (reader.IsScalar && reader.TryReadInt64(out var i64))
                {
                    SetResult(message, i64);
                    return true;
                }
                return false;
            }
        }

        private class Int64Processor : ResultProcessor<long>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar && reader.TryReadInt64(out long i64))
                {
                    SetResult(message, i64);
                    return true;
                }
                return false;
            }
        }

        private class Int32Processor : ResultProcessor<int>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar && reader.TryReadInt64(out long i64))
                {
                    SetResult(message, checked((int)i64));
                    return true;
                }
                return false;
            }
        }

        internal static ResultProcessor<StreamTrimResult> StreamTrimResult =>
            Int32EnumProcessor<StreamTrimResult>.Instance;

        internal static ResultProcessor<StreamTrimResult[]> StreamTrimResultArray =>
            Int32EnumArrayProcessor<StreamTrimResult>.Instance;

        private sealed class Int32EnumProcessor<T> : ResultProcessor<T> where T : unmanaged, Enum
        {
            private Int32EnumProcessor() { }
            public static readonly Int32EnumProcessor<T> Instance = new();

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // Accept integer, simple string, bulk string, or unit array
                long i64;
                if (reader.IsScalar && reader.TryReadInt64(out i64))
                {
                    // Direct scalar read
                }
                else if (reader.IsAggregate && reader.AggregateLengthIs(1)
                    && reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out i64))
                {
                    // Unit array - read the single element
                }
                else
                {
                    return false;
                }

                Debug.Assert(Unsafe.SizeOf<T>() == sizeof(int));
                int i32 = (int)i64;
                SetResult(message, Unsafe.As<int, T>(ref i32));
                return true;
            }
        }

        private sealed class Int32EnumArrayProcessor<T> : ResultProcessor<T[]> where T : unmanaged, Enum
        {
            private Int32EnumArrayProcessor() { }
            public static readonly Int32EnumArrayProcessor<T> Instance = new();

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (!reader.IsAggregate) return false;

                Debug.Assert(Unsafe.SizeOf<T>() == sizeof(int));
                var arr = reader.ReadPastArray(
                    static (ref r) =>
                    {
                        int i32 = (int)r.ReadInt64();
                        return Unsafe.As<int, T>(ref i32);
                    },
                    scalar: true);

                SetResult(message, arr!);
                return true;
            }
        }

        private sealed class PubSubNumSubProcessor : ResultProcessor<long>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate // name/count pairs
                    && reader.TryMoveNext() && reader.IsScalar // name, ignored
                    && reader.TryMoveNext() && reader.IsScalar // count
                    && reader.TryReadInt64(out long val) // parse the count
                    && !reader.TryMoveNext()) // no more elements
                {
                    SetResult(message, val);
                    return true;
                }

                return false;
            }
        }

        private sealed class NullableDoubleArrayProcessor : ResultProcessor<double?[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var arr = reader.ReadPastArray(
                        static (ref r) =>
                        {
                            if (r.IsNull) return (double?)null;
                            return r.TryReadDouble(out var val) ? val : null;
                        },
                        scalar: true);
                    SetResult(message, arr!);
                    return true;
                }
                return false;
            }
        }

        private sealed class NullableDoubleProcessor : ResultProcessor<double?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    if (reader.IsNull)
                    {
                        SetResult(message, null);
                        return true;
                    }
                    if (reader.TryReadDouble(out double val))
                    {
                        SetResult(message, val);
                        return true;
                    }
                }
                return false;
            }
        }

        private sealed class NullableInt64Processor : ResultProcessor<long?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    if (reader.IsNull)
                    {
                        SetResult(message, null);
                        return true;
                    }

                    if (reader.TryReadInt64(out var i64))
                    {
                        SetResult(message, i64);
                        return true;
                    }
                }

                // handle unit arrays with a scalar
                if (reader.IsAggregate && reader.TryMoveNext() && reader.IsScalar)
                {
                    if (reader.IsNull)
                    {
                        if (!reader.TryMoveNext()) // only if unit, else ignore
                        {
                            SetResult(message, null);
                            return true;
                        }
                    }
                    else if (reader.TryReadInt64(out var i64) && !reader.TryMoveNext())
                    {
                        // treat an array of 1 like a single reply
                        SetResult(message, i64);
                        return true;
                    }
                }
                return false;
            }
        }

        private sealed class ExpireResultArrayProcessor : ResultProcessor<ExpireResult[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var arr = reader.ReadPastArray(
                        static (ref r) =>
                        {
                            r.TryReadInt64(out var val);
                            return (ExpireResult)val;
                        },
                        scalar: true);
                    SetResult(message, arr!);
                    return true;
                }
                return false;
            }
        }

        private sealed class PersistResultArrayProcessor : ResultProcessor<PersistResult[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var arr = reader.ReadPastArray(
                        static (ref r) =>
                        {
                            r.TryReadInt64(out var val);
                            return (PersistResult)val;
                        },
                        scalar: true);
                    SetResult(message, arr!);
                    return true;
                }
                return false;
            }
        }

        private sealed class RedisChannelArrayProcessor : ResultProcessor<RedisChannel[]>
        {
            private readonly RedisChannel.RedisChannelOptions options;
            public RedisChannelArrayProcessor(RedisChannel.RedisChannelOptions options)
            {
                this.options = options;
            }

            // think "value-tuple", just: without the dependency hell on netfx
            private readonly struct ChannelState(PhysicalConnection connection, RedisChannel.RedisChannelOptions options)
            {
                public readonly PhysicalConnection Connection = connection;
                public readonly RedisChannel.RedisChannelOptions Options = options;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var state = new ChannelState(connection, options);
                    var arr = reader.ReadPastArray(
                        ref state,
                        static (ref s, ref r) =>
                            s.Connection.AsRedisChannel(in r, s.Options),
                        scalar: true);
                    SetResult(message, arr!);
                    return true;
                }
                return false;
            }
        }

        private sealed class RedisKeyArrayProcessor : ResultProcessor<RedisKey[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var arr = reader.ReadPastArray(static (ref r) => r.ReadRedisKey(), scalar: true);
                    SetResult(message, arr!);
                    return true;
                }
                return false;
            }
        }

        private sealed class RedisKeyProcessor : ResultProcessor<RedisKey>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    SetResult(message, reader.ReadByteArray());
                    return true;
                }
                return false;
            }
        }

        private sealed class RedisTypeProcessor : ResultProcessor<RedisType>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    RedisType redisType;
                    unsafe
                    {
                        if (!reader.TryParseScalar(&RedisTypeMetadata.TryParse, out redisType))
                        {
                            // RESP null values and empty strings should map to None rather than Unknown
                            redisType = (reader.IsNull || reader.ScalarLengthIs(0))
                                ? Redis.RedisType.None
                                : Redis.RedisType.Unknown;
                        }
                    }

                    SetResult(message, redisType);
                    return true;
                }
                return false;
            }
        }

        private sealed class RedisValueArrayProcessor : ResultProcessor<RedisValue[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var arr = reader.ReadPastRedisValues() ?? [];
                    SetResult(message, arr);
                    return true;
                }
                if (reader.IsScalar)
                {
                    // allow a single item to pass explicitly pretending to be an array; example: SPOP {key} 1
                    // If the result is nil, the result should be an empty array
                    var arr = reader.IsNull
                        ? Array.Empty<RedisValue>()
                        : new[] { reader.ReadRedisValue() };
                    SetResult(message, arr);
                    return true;
                }
                return false;
            }
        }

        private sealed class Int64ArrayProcessor : ResultProcessor<long[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var arr = reader.ReadPastArray(static (ref r) => r.ReadInt64(), scalar: true);
                    SetResult(message, arr!);
                    return true;
                }

                return false;
            }
        }

        private sealed class NullableStringArrayProcessor : ResultProcessor<string?[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var arr = reader.ReadPastArray(static (ref r) => r.ReadString(), scalar: true);
                    SetResult(message, arr!);
                    return true;
                }
                return false;
            }
        }

        private sealed class StringArrayProcessor : ResultProcessor<string[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var arr = reader.ReadPastArray(static (ref r) => r.ReadString()!, scalar: true);
                    SetResult(message, arr!);
                    return true;
                }
                return false;
            }
        }

        private sealed class BooleanArrayProcessor : ResultProcessor<bool[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var arr = reader.ReadPastArray(static (ref r) => r.ReadBoolean(), scalar: true);
                    SetResult(message, arr!);
                    return true;
                }
                return false;
            }
        }

        private sealed class RedisValueGeoPositionProcessor : ResultProcessor<GeoPosition?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    if (reader.AggregateLengthIs(1))
                    {
                        reader.MoveNext();
                        SetResult(message, ParseGeoPosition(ref reader));
                    }
                    else
                    {
                        SetResult(message, null);
                    }
                    return true;
                }
                return false;
            }
        }

        private sealed class RedisValueGeoPositionArrayProcessor : ResultProcessor<GeoPosition?[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var arr = reader.ReadPastArray(static (ref r) => ParseGeoPosition(ref r));
                    SetResult(message, arr!);
                    return true;
                }
                return false;
            }
        }

        // as GeoRadiusResult: the shape lives on the type, and both readers go through it
        private static GeoPosition? ParseGeoPosition(ref RespReader reader) => GeoPosition.TryRead(ref reader);

        private sealed class GeoRadiusResultArrayProcessor : ResultProcessor<GeoRadiusResult[]>
        {
            private static readonly GeoRadiusResultArrayProcessor?[] instances = new GeoRadiusResultArrayProcessor?[8];
            private readonly GeoRadiusOptions options;

            public static GeoRadiusResultArrayProcessor Get(GeoRadiusOptions options)
                => instances[(int)options] ??= new(options);

            private GeoRadiusResultArrayProcessor(GeoRadiusOptions options)
            {
                this.options = options;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var opts = options;
                    var typed = reader.ReadPastArray(
                        ref opts,
                        static (ref options, ref reader) => Parse(ref reader, options),
                        scalar: options == GeoRadiusOptions.None);
                    SetResult(message, typed!);
                    return true;
                }
                return false;
            }

            // the shape lives on the type it produces, so the interpolated surface's handler reads the
            // identical reply the identical way; see GeoRadiusResult.Resp.cs
            private static GeoRadiusResult Parse(ref RespReader reader, GeoRadiusOptions options)
                => GeoRadiusResult.Read(ref reader, options);
        }

        /// <summary>
        /// Parser for the https://redis.io/commands/lcs/ format with the <see cref="RedisLiterals.IDX"/> and <see cref="RedisLiterals.WITHMATCHLEN"/> arguments.
        /// </summary>
        /// <example>
        /// Example response:
        /// 1) "matches"
        /// 2) 1) 1) 1) (integer) 4
        ///          2) (integer) 7
        ///       2) 1) (integer) 5
        ///          2) (integer) 8
        ///       3) (integer) 4
        /// 3) "len"
        /// 4) (integer) 6
        /// ...
        /// </example>
        private sealed class LongestCommonSubsequenceProcessor : ResultProcessor<LCSMatchResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // the shape lives on the type it produces, so the interpolated surface's handler reads
                // the identical reply the identical way; see LCSMatchResult.Read.cs
                if (!StackExchange.Redis.LCSMatchResult.TryRead(ref reader, out var result)) return false;

                SetResult(message, result);
                return true;
            }
        }

        private sealed class RedisValueProcessor : ResultProcessor<RedisValue>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    SetResult(message, reader.ReadRedisValue());
                    return true;
                }
                return false;
            }
        }

        private sealed class RedisValueFromArrayProcessor : ResultProcessor<RedisValue>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate && reader.AggregateLengthIs(1))
                {
                    reader.MoveNext();
                    SetResult(message, reader.ReadRedisValue());
                    return true;
                }
                return false;
            }
        }

        private sealed class RoleProcessor : ResultProcessor<Role>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // Null, non-aggregate, empty, or non-scalar first element returns null Role
                if (!(reader.IsAggregate && !reader.IsNull && reader.TryMoveNext() && reader.IsScalar))
                {
                    SetResult(message, null!);
                    return true;
                }

                RoleType roleType;
                unsafe
                {
                    if (!reader.TryParseScalar(&RoleTypeMetadata.TryParse, out roleType))
                    {
                        roleType = RoleType.Unknown;
                    }
                }

                var role = roleType switch
                {
                    RoleType.Master => ParsePrimary(ref reader),
                    RoleType.Slave => ParseReplica(ref reader, "slave"),
                    RoleType.Replica => ParseReplica(ref reader, "replica"),
                    RoleType.Sentinel => ParseSentinel(ref reader),
                    _ => new Role.Unknown(reader.ReadString()!),
                };

                SetResult(message, role!);
                return true;
            }

            private static Role? ParsePrimary(ref RespReader reader)
            {
                // Expect: offset (int64), replicas (array)
                if (!(reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var offset)))
                {
                    return null;
                }

                if (!(reader.TryMoveNext() && reader.IsAggregate))
                {
                    return null;
                }

                var failed = false;
                var replicas = reader.ReadPastArray(
                    ref failed,
                    static (ref isFailed, ref r) =>
                    {
                        if (isFailed) return default; // bail early if already failed
                        if (!TryParsePrimaryReplica(ref r, out var replica))
                        {
                            isFailed = true;
                            return default;
                        }
                        return replica;
                    },
                    scalar: false) ?? [];

                if (failed) return null;

                return new Role.Master(offset, replicas);
            }

            private static bool TryParsePrimaryReplica(ref RespReader reader, out Role.Master.Replica replica)
            {
                // Expect: [ip, port, offset]
                if (!reader.IsAggregate || reader.IsNull)
                {
                    replica = default;
                    return false;
                }

                // IP
                if (!(reader.TryMoveNext() && reader.IsScalar))
                {
                    replica = default;
                    return false;
                }
                var primaryIp = reader.ReadString()!;

                // Port
                if (!(reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var primaryPort) && primaryPort <= int.MaxValue))
                {
                    replica = default;
                    return false;
                }

                // Offset
                if (!(reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var replicationOffset)))
                {
                    replica = default;
                    return false;
                }

                replica = new Role.Master.Replica(primaryIp, (int)primaryPort, replicationOffset);
                return true;
            }

            private static Role? ParseReplica(ref RespReader reader, string role)
            {
                // Expect: masterIp, masterPort, state, offset

                // Master IP
                if (!(reader.TryMoveNext() && reader.IsScalar))
                {
                    return null;
                }
                var primaryIp = reader.ReadString()!;

                // Master Port
                if (!(reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var primaryPort) && primaryPort <= int.MaxValue))
                {
                    return null;
                }

                // Replication State
                if (!(reader.TryMoveNext() && reader.IsScalar))
                {
                    return null;
                }

                // this is just a long-winded way of avoiding some string allocs!
                ReplicationState state;
                unsafe
                {
                    if (!reader.TryParseScalar(&ReplicationStateMetadata.TryParse, out state))
                    {
                        state = ReplicationState.Unknown;
                    }
                }

                var replicationState = state switch
                {
                    ReplicationState.Connect => "connect",
                    ReplicationState.Connecting => "connecting",
                    ReplicationState.Sync => "sync",
                    ReplicationState.Connected => "connected",
                    ReplicationState.None => "none",
                    ReplicationState.Handshake => "handshake",
                    _ => reader.ReadString()!,
                };

                // Replication Offset
                if (!(reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var replicationOffset)))
                {
                    return null;
                }

                return new Role.Replica(role, primaryIp, (int)primaryPort, replicationState, replicationOffset);
            }

            private static Role? ParseSentinel(ref RespReader reader)
            {
                // Expect: array of master names
                if (!(reader.TryMoveNext() && reader.IsAggregate))
                {
                    return null;
                }

                var primaries = reader.ReadPastArray(static (ref r) => r.ReadString(), scalar: true);
                return new Role.Sentinel(primaries ?? []);
            }
        }

        private sealed class ScriptResultProcessor : ResultProcessor<RedisResult>
        {
            protected override ReplyVerdict Inspect(PhysicalConnection connection, Message message, in RespReader reader)
            {
                var probe = reader;
                probe.MovePastBof();
                return probe.IsError ? NoScriptVerdict(connection, message, in probe) : ReplyVerdict.Complete;
            }

            // note that top-level error messages still get handled by SetResult, but nested errors
            // (is that a thing?) will be wrapped in the RedisResult
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (RedisResult.TryCreate(connection, ref reader, out var value))
                {
                    SetResult(message, value);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Read the entries of the single stream in an <c>XREAD</c>/<c>XREADGROUP</c> reply, past the
        /// stream-name wrapper the server puts around them.
        /// </summary>
        /// <param name="reader">The reply, positioned on its root.</param>
        /// <param name="isMap">Whether the root is a map, which is how RESP3 spells this.</param>
        /// <param name="allowJaggedFields">Whether an entry's fields may arrive as nested pairs.</param>
        /// <remarks>
        /// <para><inheritdoc cref="TryParseStreamPendingInfo" path="/remarks"/></para>
        /// <para>
        /// <b><paramref name="isMap"/> is a parameter rather than a protocol.</b> The classic path knows
        /// the connection and passes <c>protocol == Resp3</c>; a reply object has no connection, so it
        /// passes <c>Prefix == RespPrefix.Map</c> - which is the fact that actually decides the shape, and
        /// is what <see cref="MultiStreamProcessor"/> has always tested. Keeping it a parameter means the
        /// shipped path's behaviour is untouched.
        /// </para>
        /// </remarks>
        internal static StreamEntry[] ParseStreamWithNameSkip(ref RespReader reader, bool isMap, bool allowJaggedFields)
        {
            if (isMap)
            {
                // map: skip the key, read the value
                reader.MoveNext();
                reader.MoveNext();
                return ParseRedisStreamEntries(ref reader, allowJaggedFields);
            }

            // array: the first element is [name, entries]
            var iter = reader.AggregateChildren();
            if (!iter.MoveNext()) return [];
            var streamIter = iter.Value.AggregateChildren();
            streamIter.DemandNext(); // skip the stream name
            streamIter.DemandNext(); // the entries array
            return ParseRedisStreamEntries(ref streamIter.Value, allowJaggedFields);
        }

        internal sealed class SingleStreamProcessor : ResultProcessor<StreamEntry[]>
        {
            private readonly bool skipStreamName;

            public SingleStreamProcessor(bool skipStreamName = false)
            {
                this.skipStreamName = skipStreamName;
            }

            /// <summary>
            /// Handles <see href="https://redis.io/commands/xread"/>.
            /// </summary>
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsNull)
                {
                    // Server returns 'nil' if no entries are returned for the given stream.
                    SetResult(message, []);
                    return true;
                }

                if (!reader.IsAggregate)
                {
                    return false;
                }

                var protocol = connection.Protocol.GetValueOrDefault();
                StreamEntry[] entries;

                if (skipStreamName)
                {
                    /*
                    RESP 2: array element per stream; each element is an array of a name plus payload; payload is array of name/value pairs

                    127.0.0.1:6379> XREAD COUNT 2 STREAMS temperatures:us-ny:10007 0-0
                    1) 1) "temperatures:us-ny:10007"
                       2) 1) 1) "1691504774593-0"
                             2) 1) "temp_f"
                                2) "87.2"
                                3) "pressure"
                                4) "29.69"
                                5) "humidity"
                                6) "46"
                          2) 1) "1691504856705-0"
                             2) 1) "temp_f"
                                2) "87.2"
                                3) "pressure"
                                4) "29.69"
                                5) "humidity"
                                6) "46"

                    RESP 3: map of element names with array of name plus payload; payload is array of name/value pairs

                    127.0.0.1:6379> XREAD COUNT 2 STREAMS temperatures:us-ny:10007 0-0
                    1# "temperatures:us-ny:10007" => 1) 1) "1691504774593-0"
                          2) 1) "temp_f"
                             2) "87.2"
                             3) "pressure"
                             4) "29.69"
                             5) "humidity"
                             6) "46"
                       2) 1) "1691504856705-0"
                          2) 1) "temp_f"
                             2) "87.2"
                             3) "pressure"
                             4) "29.69"
                             5) "humidity"
                             6) "46"
                        */

                    entries = ParseStreamWithNameSkip(ref reader, protocol == RedisProtocol.Resp3, AllowJaggedStreamFields(protocol));
                }
                else
                {
                    entries = ParseRedisStreamEntries(ref reader, protocol);
                }

                SetResult(message, entries);
                return true;
            }
        }

        /// <summary>
        /// Handles <see href="https://redis.io/commands/xread"/>.
        /// </summary>
        internal sealed class MultiStreamProcessor : ResultProcessor<RedisStream[]>
        {
            /*
                The result is similar to the XRANGE result (see SingleStreamProcessor)
                with the addition of the stream name as the first element of top level
                Multibulk array.

                > XREAD COUNT 2 STREAMS mystream writers 0-0 0-0
                1) 1) "mystream"
                   2) 1) 1) 1526984818136-0
                         2) 1) "duration"
                            2) "1532"
                            3) "event-id"
                            4) "5"
                      2) 1) 1526999352406-0
                         2) 1) "duration"
                            2) "812"
                            3) "event-id"
                            4) "9"
                2) 1) "writers"
                   2) 1) 1) 1526985676425-0
                         2) 1) "name"
                            2) "Virginia"
                            3) "surname"
                            4) "Woolf"
                      2) 1) 1526985685298-0
                         2) 1) "name"
                            2) "Jane"
                            3) "surname"
                            4) "Austen"

                (note that XREADGROUP may include additional interior elements; see ParseRedisStreamEntries)
            */

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsNull)
                {
                    // Nothing returned for any of the requested streams. The server returns 'nil'.
                    SetResult(message, []);
                    return true;
                }

                if (!reader.IsAggregate)
                {
                    return false;
                }

                var protocol = connection.Protocol.GetValueOrDefault();
                RedisStream[] streams;

                if (reader.Prefix == RespPrefix.Map) // see SetResultCore for the shape delta between RESP2 and RESP3
                {
                    // root is a map of named inner-arrays
                    // RedisStreamInterleavedProcessor handles maps via the interleaved processor base
                    var processor = protocol == RedisProtocol.Resp2 ? RedisStreamInterleavedProcessor.Resp2 : RedisStreamInterleavedProcessor.Resp3;
                    streams = processor.ParseArray(ref reader, protocol, false, out _, null)!; // null-checked below
                }
                else
                {
                    streams = reader.ReadPastArray(
                        ref protocol,
                        static (ref protocol, ref itemReader) =>
                        {
                            if (!itemReader.IsAggregate)
                            {
                                throw new InvalidOperationException("Expected aggregate for stream");
                            }

                            // [0] = Name of the Stream
                            if (!itemReader.TryMoveNext())
                            {
                                throw new InvalidOperationException("Expected stream name");
                            }
                            var key = itemReader.ReadRedisKey();

                            // [1] = Multibulk Array of Stream Entries
                            if (!itemReader.TryMoveNext())
                            {
                                throw new InvalidOperationException("Expected stream entries");
                            }
                            var entries = ParseRedisStreamEntries(ref itemReader, protocol);

                            return new RedisStream(key: key, entries: entries);
                        },
                        scalar: false)!; // null-checked below

                    if (streams == null)
                    {
                        return false;
                    }
                }

                SetResult(message, streams);
                return true;
            }
        }

        private sealed class RedisStreamInterleavedProcessor : ValuePairInterleavedProcessorBase<RedisStream>
        {
            protected override bool AllowJaggedPairs(RedisProtocol protocol) => false; // we only use this on a flattened map

            public static readonly RedisStreamInterleavedProcessor Resp2 = new(RedisProtocol.Resp2);
            public static readonly RedisStreamInterleavedProcessor Resp3 = new(RedisProtocol.Resp3);

            private readonly RedisProtocol _protocol;
            private RedisStreamInterleavedProcessor(RedisProtocol protocol)
            {
                _protocol = protocol;
            }

            protected override RedisStream Parse(ref RespReader first, ref RespReader second, object? state)
            {
                return new(key: first.ReadRedisKey(), entries: ParseRedisStreamEntries(ref second, _protocol));
            }
        }

        /// <summary>
        /// This processor is for <see cref="RedisCommand.XAUTOCLAIM"/> *without* the <see cref="StreamConstants.JustId"/> option.
        /// </summary>
        /// <summary>Read a flat run of ids from an <c>XAUTOCLAIM</c> reply element.</summary>
        /// <remarks>
        /// Tolerates a non-aggregate or null, because the trailing "deleted ids" element is absent on 6.2
        /// - which is why the callers check the aggregate length before asking for it at all.
        /// </remarks>
        private static RedisValue[] ReadIdList(ref RespReader reader)
            => reader.IsAggregate && !reader.IsNull
                ? reader.ReadPastArray(static (ref RespReader r) => r.ReadRedisValue(), scalar: true)!
                : [];

        /// <summary>Parse an <c>XAUTOCLAIM</c> reply; <see langword="false"/> if it is not that shape.</summary>
        /// <remarks><inheritdoc cref="TryParseStreamPendingInfo" path="/remarks"/></remarks>
        internal static bool TryParseStreamAutoClaim(ref RespReader reader, bool allowJaggedFields, out StreamAutoClaimResult value)
        {
            // See https://redis.io/commands/xautoclaim for command documentation.
            // Note that the result should never be null, so intentionally treating it as a failure to parse here
            value = default;
            if (!reader.IsAggregate || reader.IsNull) return false;

            var length = reader.AggregateLength();
            if (length is not (2 or 3)) return false;

            var iter = reader.AggregateChildren();

            // [0] The next start ID.
            iter.DemandNext();
            var nextStartId = iter.Value.ReadRedisValue();

            // [1] The array of StreamEntry's.
            iter.DemandNext();
            var entries = ParseRedisStreamEntries(ref iter.Value, allowJaggedFields);

            // [2] The array of message IDs deleted from the stream that were in the PEL; absent on 6.2.
            RedisValue[] deletedIds = [];
            if (length == 3)
            {
                iter.DemandNext();
                deletedIds = ReadIdList(ref iter.Value);
            }

            value = new StreamAutoClaimResult(nextStartId, entries, deletedIds);
            return true;
        }

        /// <summary>Parse an <c>XAUTOCLAIM JUSTID</c> reply.</summary>
        /// <remarks><inheritdoc cref="TryParseStreamPendingInfo" path="/remarks"/></remarks>
        internal static bool TryParseStreamAutoClaimIdsOnly(ref RespReader reader, out StreamAutoClaimIdsOnlyResult value)
        {
            value = default;
            if (!reader.IsAggregate || reader.IsNull) return false;

            var length = reader.AggregateLength();
            if (length is not (2 or 3)) return false;

            var iter = reader.AggregateChildren();

            iter.DemandNext();
            var nextStartId = iter.Value.ReadRedisValue();

            iter.DemandNext();
            var claimedIds = ReadIdList(ref iter.Value);

            RedisValue[] deletedIds = [];
            if (length == 3)
            {
                iter.DemandNext();
                deletedIds = ReadIdList(ref iter.Value);
            }

            value = new StreamAutoClaimIdsOnlyResult(nextStartId, claimedIds, deletedIds);
            return true;
        }

        internal sealed class StreamAutoClaimProcessor : ResultProcessor<StreamAutoClaimResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (!TryParseStreamAutoClaim(ref reader, AllowJaggedStreamFields(connection.Protocol.GetValueOrDefault()), out var value)) return false;
                SetResult(message, value);
                return true;
            }
        }

        internal sealed class StreamAutoClaimIdsOnlyProcessor : ResultProcessor<StreamAutoClaimIdsOnlyResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (!TryParseStreamAutoClaimIdsOnly(ref reader, out var value)) return false;
                SetResult(message, value);
                return true;
            }
        }

        internal sealed class StreamConsumerInfoProcessor : InterleavedStreamInfoProcessorBase<StreamConsumerInfo>
        {
            protected override StreamConsumerInfo ParseItem(ref RespReader reader)
            {
                // Note: the base class passes a single consumer from the response into this method.

                // Response format:
                // > XINFO CONSUMERS mystream mygroup
                // 1) 1) name
                //    2) "Alice"
                //    3) pending
                //    4) (integer)1
                //    5) idle
                //    6) (integer)9104628
                // 2) 1) name
                //    2) "Bob"
                //    3) pending
                //    4) (integer)1
                //    5) idle
                //    6) (integer)83841983
                if (!reader.IsAggregate)
                {
                    return default;
                }

                string? name = default;
                int pendingMessageCount = default;
                long idleTimeInMilliseconds = default;

                while (reader.TryMoveNext() && reader.IsScalar)
                {
                    StreamConsumerInfoField field;
                    unsafe
                    {
                        if (!reader.TryParseScalar(&StreamConsumerInfoFieldMetadata.TryParse, out field))
                        {
                            field = StreamConsumerInfoField.Unknown;
                        }
                    }

                    if (!reader.TryMoveNext()) break;

                    switch (field)
                    {
                        case StreamConsumerInfoField.Name:
                            name = reader.ReadString();
                            break;
                        case StreamConsumerInfoField.Pending when reader.TryReadInt64(out var pending):
                            pendingMessageCount = checked((int)pending);
                            break;
                        case StreamConsumerInfoField.Idle:
                            reader.TryReadInt64(out idleTimeInMilliseconds);
                            break;
                    }
                }

                return new StreamConsumerInfo(name!, pendingMessageCount, idleTimeInMilliseconds);
            }
        }

        internal sealed class StreamGroupInfoProcessor : InterleavedStreamInfoProcessorBase<StreamGroupInfo>
        {
            protected override StreamGroupInfo ParseItem(ref RespReader reader)
            {
                // Note: the base class passes a single item from the response into this method.

                // Response format:
                // > XINFO GROUPS mystream
                // 1) 1) name
                //    2) "mygroup"
                //    3) consumers
                //    4) (integer)2
                //    5) pending
                //    6) (integer)2
                //    7) last-delivered-id
                //    8) "1588152489012-0"
                //    9) "entries-read"
                //   10) (integer)2
                //   11) "lag"
                //   12) (integer)0
                // 2) 1) name
                //    2) "some-other-group"
                //    3) consumers
                //    4) (integer)1
                //    5) pending
                //    6) (integer)0
                //    7) last-delivered-id
                //    8) "1588152498034-0"
                //    9) "entries-read"
                //   10) (integer)1
                //   11) "lag"
                //   12) (integer)1
                if (!reader.IsAggregate)
                {
                    return default;
                }

                string? name = default, lastDeliveredId = default;
                int consumerCount = default, pendingMessageCount = default;
                long entriesRead = default;
                long? lag = default;

                while (reader.TryMoveNext() && reader.IsScalar)
                {
                    StreamGroupInfoField field;
                    unsafe
                    {
                        if (!reader.TryParseScalar(&StreamGroupInfoFieldMetadata.TryParse, out field))
                        {
                            field = StreamGroupInfoField.Unknown;
                        }
                    }

                    if (!reader.TryMoveNext()) break;

                    switch (field)
                    {
                        case StreamGroupInfoField.Name:
                            name = reader.ReadString();
                            break;
                        case StreamGroupInfoField.Consumers when reader.TryReadInt64(out var consumers):
                            consumerCount = checked((int)consumers);
                            break;
                        case StreamGroupInfoField.Pending when reader.TryReadInt64(out var pending):
                            pendingMessageCount = checked((int)pending);
                            break;
                        case StreamGroupInfoField.LastDeliveredId:
                            lastDeliveredId = reader.ReadString();
                            break;
                        case StreamGroupInfoField.EntriesRead:
                            reader.TryReadInt64(out entriesRead);
                            break;
                        case StreamGroupInfoField.Lag when reader.TryReadInt64(out var lagValue):
                            lag = lagValue;
                            break;
                    }
                }

                return new StreamGroupInfo(name!, consumerCount, pendingMessageCount, lastDeliveredId, entriesRead, lag);
            }
        }

        internal abstract class InterleavedStreamInfoProcessorBase<T> : ResultProcessor<T[]>
        {
            protected abstract T ParseItem(ref RespReader reader);

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (!reader.IsAggregate)
                {
                    return false;
                }

                var self = this;
                var parsedItems = reader.ReadPastArray(
                    ref self,
                    static (ref self, ref r) => self.ParseItem(ref r),
                    scalar: false);

                SetResult(message, parsedItems!);
                return true;
            }
        }

        internal sealed class StreamInfoProcessor : ResultProcessor<StreamInfo>
        {
            // Parse the following format:
            // > XINFO mystream
            // 1) length
            // 2) (integer) 13
            // 3) radix-tree-keys
            // 4) (integer) 1
            // 5) radix-tree-nodes
            // 6) (integer) 2
            // 7) groups
            // 8) (integer) 2
            // 9) first-entry
            // 10) 1) 1524494395530-0
            //     2) 1) "a"
            //        2) "1"
            //        3) "b"
            //        4) "2"
            // 11) last-entry
            // 12) 1) 1526569544280-0
            //     2) 1) "message"
            //        2) "banana"
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (!reader.IsAggregate)
                {
                    return false;
                }

                int count = reader.AggregateLength();
                if ((count & 1) != 0) return false; // must be even (key-value pairs)

                long length = -1, radixTreeKeys = -1, radixTreeNodes = -1, groups = -1,
                    entriesAdded = -1, idmpDuration = -1, idmpMaxsize = -1,
                    pidsTracked = -1, iidsTracked = -1, iidsAdded = -1, iidsDuplicates = -1;
                RedisValue lastGeneratedId = Redis.RedisValue.Null,
                    maxDeletedEntryId = Redis.RedisValue.Null,
                    recordedFirstEntryId = Redis.RedisValue.Null;
                StreamEntry firstEntry = StreamEntry.Null, lastEntry = StreamEntry.Null;

                var protocol = connection.Protocol.GetValueOrDefault();

                while (reader.TryMoveNext() && reader.IsScalar)
                {
                    StreamInfoField field;
                    unsafe
                    {
                        if (!reader.TryParseScalar(&StreamInfoFieldMetadata.TryParse, out field))
                        {
                            field = StreamInfoField.Unknown;
                        }
                    }

                    // Move to value
                    if (!reader.TryMoveNext()) break;

                    switch (field)
                    {
                        case StreamInfoField.Length:
                            if (!reader.TryReadInt64(out length)) return false;
                            break;
                        case StreamInfoField.RadixTreeKeys:
                            if (!reader.TryReadInt64(out radixTreeKeys)) return false;
                            break;
                        case StreamInfoField.RadixTreeNodes:
                            if (!reader.TryReadInt64(out radixTreeNodes)) return false;
                            break;
                        case StreamInfoField.Groups:
                            if (!reader.TryReadInt64(out groups)) return false;
                            break;
                        case StreamInfoField.LastGeneratedId:
                            lastGeneratedId = reader.ReadRedisValue();
                            break;
                        case StreamInfoField.FirstEntry:
                            firstEntry = ParseRedisStreamEntry(ref reader, protocol);
                            break;
                        case StreamInfoField.LastEntry:
                            lastEntry = ParseRedisStreamEntry(ref reader, protocol);
                            break;
                        // 7.0
                        case StreamInfoField.MaxDeletedEntryId:
                            maxDeletedEntryId = reader.ReadRedisValue();
                            break;
                        case StreamInfoField.RecordedFirstEntryId:
                            recordedFirstEntryId = reader.ReadRedisValue();
                            break;
                        case StreamInfoField.EntriesAdded:
                            if (!reader.TryReadInt64(out entriesAdded)) return false;
                            break;
                        // 8.6
                        case StreamInfoField.IdmpDuration:
                            if (!reader.TryReadInt64(out idmpDuration)) return false;
                            break;
                        case StreamInfoField.IdmpMaxsize:
                            if (!reader.TryReadInt64(out idmpMaxsize)) return false;
                            break;
                        case StreamInfoField.PidsTracked:
                            if (!reader.TryReadInt64(out pidsTracked)) return false;
                            break;
                        case StreamInfoField.IidsTracked:
                            if (!reader.TryReadInt64(out iidsTracked)) return false;
                            break;
                        case StreamInfoField.IidsAdded:
                            if (!reader.TryReadInt64(out iidsAdded)) return false;
                            break;
                        case StreamInfoField.IidsDuplicates:
                            if (!reader.TryReadInt64(out iidsDuplicates)) return false;
                            break;
                    }
                }

                var streamInfo = new StreamInfo(
                    length: checked((int)length),
                    radixTreeKeys: checked((int)radixTreeKeys),
                    radixTreeNodes: checked((int)radixTreeNodes),
                    groups: checked((int)groups),
                    firstEntry: firstEntry,
                    lastEntry: lastEntry,
                    lastGeneratedId: lastGeneratedId,
                    maxDeletedEntryId: maxDeletedEntryId,
                    entriesAdded: entriesAdded,
                    recordedFirstEntryId: recordedFirstEntryId,
                    idmpDuration: idmpDuration,
                    idmpMaxSize: idmpMaxsize,
                    pidsTracked: pidsTracked,
                    iidsTracked: iidsTracked,
                    iidsAdded: iidsAdded,
                    iidsDuplicates: iidsDuplicates);

                SetResult(message, streamInfo);
                return true;
            }
        }

        /// <summary>
        /// Parse an <c>XPENDING</c> summary reply; <see langword="false"/> if it is not that shape.
        /// </summary>
        /// <remarks>
        /// Shared, not copied: the deferred <c>Streams.RespPendingReply</c> materialises through this, so
        /// the two shapes are two call sites of one parse - the same arrangement
        /// <see cref="ParseRedisStreamEntries(ref RespReader, bool)"/> already has.
        /// </remarks>
        internal static bool TryParseStreamPendingInfo(ref RespReader reader, out StreamPendingInfo value)
        {
                // Example:
            // > XPENDING mystream mygroup
            // 1) (integer)2
            // 2) 1526569498055 - 0
            // 3) 1526569506935 - 0
            // 4) 1) 1) "Bob"
            //       2) "2"
            // 5) 1) 1) "Joe"
            //       2) "8"
            value = default;
            if (!(reader.IsAggregate && reader.AggregateLengthIs(4)))
            {
                return false;
            }

            var iter = reader.AggregateChildren();

            // Element 0: pending message count
            iter.DemandNext();
            if (!iter.Value.TryReadInt64(out var pendingMessageCount))
            {
                return false;
            }

            // Element 1: lowest ID
            iter.DemandNext();
            var lowestId = iter.Value.ReadRedisValue();

            // Element 2: highest ID
            iter.DemandNext();
            var highestId = iter.Value.ReadRedisValue();

            // Element 3: consumers array (may be null)
            iter.DemandNext();
            StreamConsumer[]? consumers = null;

            // If there are no consumers as of yet for the given group, the last
            // item in the response array will be null.
            if (iter.Value.IsAggregate && !iter.Value.IsNull)
            {
                consumers = iter.Value.ReadPastArray(
                    static (ref RespReader consumerReader) =>
                    {
                        if (!(consumerReader.IsAggregate && consumerReader.AggregateLengthIs(2)))
                        {
                            throw new InvalidOperationException("Expected array of 2 elements for consumer");
                        }

                        var consumerIter = consumerReader.AggregateChildren();

                        consumerIter.DemandNext();
                        var name = consumerIter.Value.ReadRedisValue();

                        consumerIter.DemandNext();
                        if (!consumerIter.Value.TryReadInt64(out var count))
                        {
                            throw new InvalidOperationException("Expected integer for pending message count");
                        }

                        return new StreamConsumer(
                            name: name,
                            pendingMessageCount: checked((int)count));
                    },
                    scalar: false);
            }

            value = new StreamPendingInfo(
                pendingMessageCount: checked((int)pendingMessageCount),
                lowestId: lowestId,
                highestId: highestId,
                consumers: consumers ?? []);
            return true;
        }

        /// <summary>Parse an <c>XPENDING</c> extended reply.</summary>
        /// <remarks><inheritdoc cref="TryParseStreamPendingInfo" path="/remarks"/></remarks>
        internal static StreamPendingMessageInfo[] ParseStreamPendingMessages(ref RespReader reader)
        {
            if (!reader.IsAggregate) return [];

            return reader.ReadPastArray(
                static (ref RespReader itemReader) =>
                {
                    if (!(itemReader.IsAggregate && itemReader.AggregateLengthIs(4)))
                    {
                        throw new InvalidOperationException("Expected array of 4 elements for pending message");
                    }

                    if (!itemReader.TryMoveNext())
                    {
                        throw new InvalidOperationException("Expected message ID");
                    }
                    var messageId = itemReader.ReadRedisValue();

                    if (!itemReader.TryMoveNext())
                    {
                        throw new InvalidOperationException("Expected consumer name");
                    }
                    var consumerName = itemReader.ReadRedisValue();

                    if (!itemReader.TryMoveNext() || !itemReader.TryReadInt64(out var idleTimeInMs))
                    {
                        throw new InvalidOperationException("Expected integer for idle time");
                    }

                    if (!itemReader.TryMoveNext() || !itemReader.TryReadInt64(out var deliveryCount))
                    {
                        throw new InvalidOperationException("Expected integer for delivery count");
                    }

                    return new StreamPendingMessageInfo(
                        messageId: messageId,
                        consumerName: consumerName,
                        idleTimeInMs: idleTimeInMs,
                        deliveryCount: ParseStreamDeliveryCount(deliveryCount));
                },
                scalar: false) ?? [];
        }

        internal sealed class StreamPendingInfoProcessor : ResultProcessor<StreamPendingInfo>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (!TryParseStreamPendingInfo(ref reader, out var pendingInfo)) return false;
                SetResult(message, pendingInfo);
                return true;
            }
        }

        internal sealed class StreamPendingMessagesProcessor : ResultProcessor<StreamPendingMessageInfo[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (!reader.IsAggregate) return false;
                SetResult(message, ParseStreamPendingMessages(ref reader));
                return true;
            }
        }

        internal sealed class StreamNameValueEntryProcessor : ValuePairInterleavedProcessorBase<NameValueEntry>
        {
            public static readonly StreamNameValueEntryProcessor Instance = new();
            private StreamNameValueEntryProcessor()
            {
            }

            protected override NameValueEntry Parse(ref RespReader first, ref RespReader second, object? state)
                => new NameValueEntry(first.ReadRedisValue(), second.ReadRedisValue());

            /// <summary>Whether a stream's field pairs may arrive jagged on this protocol.</summary>
            /// <remarks>
            /// Exposed so the stream parses can convert protocol to policy <b>once</b>, rather than each
            /// restating <c>protocol &gt;= Resp3</c> and drifting from the virtual that actually decides.
            /// </remarks>
            internal bool AllowsJaggedPairs(RedisProtocol protocol) => AllowJaggedPairs(protocol);
        }

        /// <summary>
        /// Reads an <c>XRANGE</c>-shaped reply into the array shape the old surface promises.
        /// For formats, see <see href="https://redis.io/topics/streams-intro"/>.
        /// </summary>
        /// <remarks>
        /// <b>Off the generic class it used to sit on</b>, which never used its type argument - so calling
        /// it read as <c>StreamProcessorBase&lt;StreamEntry[]&gt;.ParseRedisStreamEntries(...)</c>, naming a
        /// type purely to reach a static. It lives here because the deferred-view work needs a second
        /// caller: a reply object holding the payload projects to the old shape by constructing a reader
        /// over the same buffer and calling exactly this, so the array shape never acquires a second parse.
        /// </remarks>
        internal static StreamEntry ParseRedisStreamEntry(ref RespReader reader, RedisProtocol protocol)
            => ParseRedisStreamEntry(ref reader, AllowJaggedStreamFields(protocol));

        /// <inheritdoc cref="ParseRedisStreamEntry(ref RespReader, RedisProtocol)"/>
        /// <param name="reader">The reader, positioned on the entry.</param>
        /// <param name="allowJaggedFields">Whether the entry's fields may arrive as nested pairs.</param>
        internal static StreamEntry ParseRedisStreamEntry(ref RespReader reader, bool allowJaggedFields)
        {
            if (!reader.IsAggregate || reader.IsNull)
            {
                return StreamEntry.Null;
            }
            // Process the Multibulk array for each entry. The entry contains the following elements:
            //  [0] = SimpleString (the ID of the stream entry)
            //  [1] = Multibulk array of the name/value pairs of the stream entry's data
            // optional (XREADGROUP with CLAIM):
            //  [2] = idle time (in milliseconds)
            //  [3] = delivery count
            int length = reader.AggregateLength();
            var iter = reader.AggregateChildren();

            iter.DemandNext();
            var id = iter.Value.ReadRedisValue();

            iter.DemandNext();
            var values = ParseStreamEntryValues(ref iter.Value, allowJaggedFields);

            // check for optional fields (XREADGROUP with CLAIM)
            if (length >= 4)
            {
                iter.DemandNext();
                if (iter.Value.TryReadInt64(out var idleTimeInMs))
                {
                    iter.DemandNext();
                    if (iter.Value.TryReadInt64(out var deliveryCount))
                    {
                        return new StreamEntry(
                            id: id,
                            values: values,
                            idleTime: TimeSpan.FromMilliseconds(idleTimeInMs),
                            deliveryCount: ParseStreamDeliveryCount(deliveryCount));
                    }
                }
            }

            return new StreamEntry(
                id: id,
                values: values);
        }
        internal static StreamEntry[] ParseRedisStreamEntries(ref RespReader reader, RedisProtocol protocol)
            => ParseRedisStreamEntries(ref reader, AllowJaggedStreamFields(protocol));

        /// <inheritdoc cref="ParseRedisStreamEntries(ref RespReader, RedisProtocol)"/>
        /// <param name="reader">The reader, positioned on the run of entries.</param>
        /// <param name="allowJaggedFields">Whether an entry's fields may arrive as nested pairs.</param>
        internal static StreamEntry[] ParseRedisStreamEntries(ref RespReader reader, bool allowJaggedFields)
        {
            if (!reader.IsAggregate || reader.IsNull)
            {
                return [];
            }

            return reader.ReadPastArray(
                ref allowJaggedFields,
                static (ref allowJaggedFields, ref r) => ParseRedisStreamEntry(ref r, allowJaggedFields),
                scalar: false) ?? [];
        }

        /// <inheritdoc cref="ParseStreamEntryValues(ref RespReader, bool)"/>
        internal static NameValueEntry[] ParseStreamEntryValues(ref RespReader reader, RedisProtocol protocol)
            => ParseStreamEntryValues(ref reader, AllowJaggedStreamFields(protocol));

        /// <summary>Read a stream entry's name/value fields.</summary>
        /// <param name="reader">The reader, positioned on the field list.</param>
        /// <param name="allowJaggedFields">Whether the fields may arrive as nested pairs.</param>
        internal static NameValueEntry[] ParseStreamEntryValues(ref RespReader reader, bool allowJaggedFields)
        {
            if (!reader.IsAggregate || reader.IsNull)
            {
                return [];
            }
            return StreamNameValueEntryProcessor.Instance.ParseArray(ref reader, allowJaggedFields, false, out _, null)!;
        }

        /// <summary>
        /// Turn a connection's protocol into the one policy these parses actually read it for.
        /// </summary>
        /// <remarks>
        /// <b>The protocol is threaded through the stream parses for exactly this, and nothing else looks
        /// at it.</b> Converting once, here, is what lets a caller that has no connection to ask - the
        /// deferred reply shapes hold a buffer, not a connection - state the policy directly instead of
        /// naming a protocol version it is not really claiming.
        /// </remarks>
        internal static bool AllowJaggedStreamFields(RedisProtocol protocol)
            => StreamNameValueEntryProcessor.Instance.AllowsJaggedPairs(protocol);

        private sealed class StringPairInterleavedProcessor : ValuePairInterleavedProcessorBase<KeyValuePair<string, string>>
        {
            protected override KeyValuePair<string, string> Parse(ref RespReader first, ref RespReader second, object? state) =>
                new KeyValuePair<string, string>(first.ReadString()!, second.ReadString()!);
        }

        private sealed class StringProcessor : ResultProcessor<string?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    SetResult(message, reader.ReadString());
                    return true;
                }

                if (reader.IsAggregate && reader.TryMoveNext() && reader.IsScalar)
                {
                    // treat an array of 1 like a single reply
                    var value = reader.ReadString();
                    if (!reader.TryMoveNext())
                    {
                        SetResult(message, value);
                        return true;
                    }
                }
                return false;
            }
        }

        private sealed class TieBreakerProcessor : ResultProcessor<string?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar)
                {
                    var tieBreaker = reader.ReadString();
                    connection.BridgeCouldBeNull?.ServerEndPoint?.TieBreakerResult = tieBreaker;
                    SetResult(message, tieBreaker);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Handles the reply to the maintenance-notification opt-in, which we send speculatively: a server that
        /// doesn't know the subcommand replies with an error, and that is an expected outcome rather than a
        /// fault. So the error is absorbed here rather than going through the common error path, which would
        /// raise an <see cref="ConnectionMultiplexer.ErrorMessage"/> to the consumer for something we asked for
        /// on their behalf.
        /// </summary>
        private sealed class MaintenanceNotificationsProcessor : ResultProcessor<bool>
        {
            public override bool SetResult(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                reader.MovePastBof();
                var server = connection.BridgeCouldBeNull?.ServerEndPoint;
                if (reader.IsError)
                {
                    server?.OnMaintenanceNotificationsRefused(connection, reader.ReadString() ?? "declined");
                    SetResult(message, false);
                    return true;
                }

                if (reader.IsScalar && Literals.OK.Hash.IsCS(reader.TryGetSpan(out var span) ? span : reader.Buffer(stackalloc byte[16])))
                {
                    server?.OnMaintenanceNotificationsAccepted(connection);
                    SetResult(message, true);
                    return true;
                }

                // anything else: treat as "not available" rather than a protocol fault; being liberal in what
                // we accept matters more here than pinning an unverifiable reply shape
                server?.OnMaintenanceNotificationsRefused(connection, $"unexpected reply: {reader.GetOverview()}");
                SetResult(message, false);
                return true;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
                => throw new NotSupportedException(); // SetResult is fully overridden
        }

        private sealed class TracerProcessor(bool establishConnection) : ResultProcessor<bool>
        {
            public override bool SetResult(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                reader.MovePastBof();
                bool isError = reader.IsError;
                var copy = reader;

                connection.BridgeCouldBeNull?.ServerEndPoint?.SetLatency(message.CreatedDateTime);
                connection.BridgeCouldBeNull?.Multiplexer.OnInfoMessage($"got '{reader.Prefix}' for '{message.CommandAndKey}' on '{connection}'");

                var errorKind = isError ? RedisErrorKindMetadata.Classify(copy) : RedisErrorKind.None;

                // A redirect answers the only question a tracer asks - is this server up, speaking RESP, and
                // talking to us - so it completes the handshake rather than failing it.
                //
                // This matters because the keyed EXISTS fallback, reached when ECHO, PING and TIME are all
                // disabled, cannot target a slot the node owns during its first handshake: the CLUSTER NODES
                // reply that would say which slots those are is still in flight in the same pipeline batch,
                // so ServerType is still the seeded Standalone when the key is chosen. Treating the resulting
                // MOVED as a protocol failure tore the connection down, and on an OSS cluster that left nodes
                // unestablished while the connect burned its whole ConnectTimeout. See #2970.
                bool redirected = errorKind is RedisErrorKind.Moved or RedisErrorKind.Ask;

                bool final;
                if (redirected)
                {
                    connection.BridgeCouldBeNull?.Multiplexer.Trace($"Tracer redirected ({errorKind}); the server answered, so the connection stands", ToString());
                    SetResult(message, true);
                    final = true;
                }
                else
                {
                    final = base.SetResult(connection, message, ref reader);
                }

                if (isError && !redirected)
                {
                    reader = copy; // rewind and re-parse
                    if (errorKind is RedisErrorKind.NotPermitted or RedisErrorKind.NoAuth)
                    {
                        connection.RecordConnectionFailed(ConnectionFailureType.AuthenticationFailure, new Exception(reader.GetOverview() + " Verify if the Redis password provided is correct. Attempted command: " + message.Command));
                    }
                    else if (errorKind == RedisErrorKind.Loading)
                    {
                        connection.RecordConnectionFailed(ConnectionFailureType.Loading);
                    }
                    else
                    {
                        connection.RecordConnectionFailed(ConnectionFailureType.ProtocolFailure, new RedisServerException(RedisErrorKind.ConnectionFault, message.Flags, reader.GetOverview()));
                    }
                }

                if (connection.Protocol is null)
                {
                    // If we didn't get a valid response from HELLO, then we have to assume RESP2 at some point.
                    // We need the protocol assigned before OnFullyEstablished so that the
                    // protocol is reliably known *before* we do next-steps.
                    connection.SetProtocol(RedisProtocol.Resp2);
                }

                if (final & establishConnection)
                {
                    // This is what ultimately brings us to complete a connection, by advancing the state forward from a successful tracer after connection.
                    connection.BridgeCouldBeNull?.OnFullyEstablished(connection, $"From command: {message.Command}");
                }

                return final;
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0071:Simplify interpolation", Justification = "Allocations (string.Concat vs. string.Format)")]
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                bool happy;
                switch (message.Command)
                {
                    case RedisCommand.ECHO:
                        happy = reader.Prefix == RespPrefix.BulkString && (!establishConnection || reader.Is(connection.BridgeCouldBeNull?.Multiplexer?.UniqueId));
                        break;
                    case RedisCommand.PING:
                        // there are two different PINGs; "interactive" is a +PONG or +{your message},
                        // but subscriber returns a bulk-array of [ "pong", {your message} ]
                        Span<byte> buffer = stackalloc byte[8];
                        switch (reader.Prefix)
                        {
                            case RespPrefix.SimpleString:
                                var span = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(buffer);
                                happy = Literals.PONG.Hash.IsCI(span);
                                break;
                            case RespPrefix.Array when reader.AggregateLengthIs(2):
                                var iter = reader.AggregateChildren();
                                if (iter.MoveNext() && iter.Value.IsScalar)
                                {
                                    var pongSpan = iter.Value.TryGetSpan(out var pongTmp) ? pongTmp : iter.Value.Buffer(buffer);
                                    happy = Literals.PONG.Hash.IsCI(pongSpan) && iter.MoveNext() && iter.Value.IsScalar && iter.Value.ScalarIsEmpty();
                                }
                                else
                                {
                                    happy = false;
                                }
                                break;
                            default:
                                happy = false;
                                break;
                        }
                        break;
                    case RedisCommand.TIME:
                        happy = reader.Prefix == RespPrefix.Array && reader.AggregateLengthIs(2);
                        break;
                    case RedisCommand.EXISTS:
                        happy = reader.Prefix == RespPrefix.Integer;
                        break;
                    default:
                        happy = false;
                        break;
                }
                if (happy)
                {
                    SetResult(message, happy);
                    return true;
                }
                else
                {
                    connection.RecordConnectionFailed(
                        ConnectionFailureType.ProtocolFailure,
                        new InvalidOperationException($"unexpected tracer reply to {message.Command}: {reader.GetOverview()}"));
                    return false;
                }
            }
        }

        /// <summary>
        /// Filters out null values from an endpoint array efficiently.
        /// </summary>
        /// <param name="endpoints">The array to filter, or null.</param>
        /// <returns>
        /// - null if input is null.
        /// - original array if no nulls found.
        /// - empty array if all nulls.
        /// - new array with nulls removed otherwise.
        /// </returns>
        private static EndPoint[]? FilterNullEndpoints(EndPoint?[]? endpoints)
        {
            if (endpoints is null) return null;

            // Count nulls in a single pass
            int nullCount = 0;
            for (int i = 0; i < endpoints.Length; i++)
            {
                if (endpoints[i] is null) nullCount++;
            }

            // No nulls - return original array
            if (nullCount == 0) return endpoints!;

            // All nulls - return empty array
            if (nullCount == endpoints.Length) return [];

            // Some nulls - allocate new array and copy non-nulls
            var result = new EndPoint[endpoints.Length - nullCount];
            int writeIndex = 0;
            for (int i = 0; i < endpoints.Length; i++)
            {
                if (endpoints[i] is not null)
                {
                    result[writeIndex++] = endpoints[i]!;
                }
            }

            return result;
        }

        private sealed class SentinelGetPrimaryAddressByNameProcessor : ResultProcessor<EndPoint?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate && reader.AggregateLengthIs(2))
                {
                    reader.MoveNext();
                    var host = reader.ReadString();
                    reader.MoveNext();
                    if (host is not null && reader.TryReadInt64(out var port))
                    {
                        SetResult(message, Format.ParseEndPoint(host, checked((int)port)));
                        return true;
                    }
                }
                else if (reader.IsNull || (reader.IsAggregate && reader.AggregateLengthIs(0)))
                {
                    SetResult(message, null);
                    return true;
                }
                return false;
            }
        }

        private sealed partial class SentinelGetSentinelAddressesProcessor : ResultProcessor<EndPoint[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate && !reader.IsNull)
                {
                    var endpoints = reader.ReadPastArray(
                        static (ref RespReader itemReader) =>
                        {
                            if (itemReader.IsAggregate)
                            {
                                // Parse key-value pairs by name: ["ip", "127.0.0.1", "port", "26379"]
                                // or ["port", "26379", "ip", "127.0.0.1"] - order doesn't matter
                                string? host = null;
                                long portValue = 0;

                                while (itemReader.TryMoveNext() && itemReader.IsScalar)
                                {
                                    SentinelAddressField field;
                                    unsafe
                                    {
                                        if (!itemReader.TryParseScalar(
                                                &SentinelAddressFieldMetadata.TryParse,
                                                out field))
                                        {
                                            field = SentinelAddressField.Unknown;
                                        }
                                    }

                                    // Check for second scalar value
                                    if (!(itemReader.TryMoveNext() && itemReader.IsScalar)) break;

                                    switch (field)
                                    {
                                        case SentinelAddressField.Ip:
                                            host = itemReader.ReadString();
                                            break;

                                        case SentinelAddressField.Port:
                                            itemReader.TryReadInt64(out portValue);
                                            break;
                                    }
                                }

                                if (host is not null && portValue > 0)
                                {
                                    return Format.ParseEndPoint(host, checked((int)portValue));
                                }
                            }
                            return null;
                        },
                        scalar: false);

                    var filtered = FilterNullEndpoints(endpoints);
                    if (filtered is not null)
                    {
                        SetResult(message, filtered);
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed partial class SentinelGetReplicaAddressesProcessor : ResultProcessor<EndPoint[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsAggregate)
                {
                    var endPoints = reader.ReadPastArray(
                        static (ref RespReader r) =>
                        {
                            if (r.IsAggregate)
                            {
                                // Parse key-value pairs by name: ["ip", "127.0.0.1", "port", "6380"]
                                // or ["port", "6380", "ip", "127.0.0.1"] - order doesn't matter
                                string? host = null;
                                long portValue = 0;

                                while (r.TryMoveNext() && r.IsScalar)
                                {
                                    SentinelAddressField field;
                                    unsafe
                                    {
                                        if (!r.TryParseScalar(
                                                &SentinelAddressFieldMetadata.TryParse,
                                                out field))
                                        {
                                            field = SentinelAddressField.Unknown;
                                        }
                                    }

                                    // Check for second scalar value
                                    if (!(r.TryMoveNext() && r.IsScalar)) break;

                                    switch (field)
                                    {
                                        case SentinelAddressField.Ip:
                                            host = r.ReadString();
                                            break;

                                        case SentinelAddressField.Port:
                                            r.TryReadInt64(out portValue);
                                            break;
                                    }
                                }

                                if (host is not null && portValue > 0)
                                {
                                    return Format.ParseEndPoint(host, checked((int)portValue));
                                }
                            }
                            return null;
                        },
                        scalar: false);

                    var filtered = FilterNullEndpoints(endPoints);
                    if (filtered is not null && filtered.Length > 0)
                    {
                        SetResult(message, filtered);
                        return true;
                    }
                }
                else if (reader.IsScalar && reader.IsNull)
                {
                    // We don't want to blow up if the primary is not found
                    return true;
                }

                return false;
            }
        }

        private sealed class SentinelArrayOfArraysProcessor : ResultProcessor<KeyValuePair<string, string>[][]>
        {
            private readonly struct ParseArrayState(StringPairInterleavedProcessor innerProcessor, RedisProtocol protocol, Message message)
            {
                public readonly StringPairInterleavedProcessor innerProcessor = innerProcessor;
                public readonly RedisProtocol protocol = protocol;
                public readonly Message message = message;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (StringPairInterleaved is not StringPairInterleavedProcessor innerProcessor)
                {
                    return false;
                }

                if (reader.IsAggregate && !reader.IsNull)
                {
                    var protocol = connection.Protocol.GetValueOrDefault();
                    var state = new ParseArrayState(innerProcessor, protocol, message);
                    var returnArray = reader.ReadPastArray(
                        ref state,
                        static (ref state, ref innerReader) =>
                        {
                            if (!innerReader.IsAggregate)
                            {
                                throw new ArgumentOutOfRangeException(nameof(innerReader), $"Error processing {state.message.CommandAndKey}, expected array but got scalar");
                            }
                            return state.innerProcessor.ParseArray(ref innerReader, state.protocol, false, out _, null)!;
                        },
                        scalar: false);

                    SetResult(message, returnArray!);
                    return true;
                }
                return false;
            }
        }
    }

    internal abstract class ResultProcessor<T> : ResultProcessor
    {
        protected static void SetResult(Message? message, T value)
        {
            if (message == null) return;
            var box = message.ResultBox as IResultBox<T>;
            message.SetResponseReceived();

            box?.SetResult(value);
        }
    }

    internal abstract class ArrayResultProcessor<T> : ResultProcessor<T[]>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (!reader.IsAggregate) return false;

            var self = this;
            var arr = reader.ReadPastArray(
                ref self,
                static (ref s, ref r) =>
                {
                    if (!s.TryParse(ref r, out var parsed))
                    {
                        throw new InvalidOperationException("Failed to parse array element");
                    }
                    return parsed;
                },
                scalar: false);

            SetResult(message, arr!);
            return true;
        }

        protected abstract bool TryParse(ref RespReader reader, out T parsed);
    }
}
