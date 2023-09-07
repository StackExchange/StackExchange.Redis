using Microsoft.Extensions.Logging;
using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace StackExchange.Redis
{
    internal abstract class ResultProcessor
    {
        public static readonly ResultProcessor<bool>
            Boolean = new BooleanProcessor(),
            DemandOK = new ExpectBasicStringProcessor(CommonReplies.OK),
            DemandPONG = new ExpectBasicStringProcessor(CommonReplies.PONG),
            DemandZeroOrOne = new DemandZeroOrOneProcessor(),
            AutoConfigure = new AutoConfigureProcessor(),
            TrackSubscriptions = new TrackSubscriptionsProcessor(null),
            Tracer = new TracerProcessor(false),
            EstablishConnection = new TracerProcessor(true),
            BackgroundSaveStarted = new ExpectBasicStringProcessor(CommonReplies.backgroundSavingStarted_trimmed, startsWith: true),
            BackgroundSaveAOFStarted = new ExpectBasicStringProcessor(CommonReplies.backgroundSavingAOFStarted_trimmed, startsWith: true);

        public static readonly ResultProcessor<byte[]?>
            ByteArray = new ByteArrayProcessor();

        public static readonly ResultProcessor<byte[]>
            ScriptLoad = new ScriptLoadProcessor();

        public static readonly ResultProcessor<ClusterConfiguration>
            ClusterNodes = new ClusterNodesProcessor();

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

        public static readonly ResultProcessor<double?>
                            NullableDouble = new NullableDoubleProcessor();

        public static readonly ResultProcessor<double?[]>
                            NullableDoubleArray = new NullableDoubleArrayProcessor();

        public static readonly ResultProcessor<long?>
            NullableInt64 = new NullableInt64Processor();

        public static readonly ResultProcessor<RedisChannel[]>
            RedisChannelArrayLiteral = new RedisChannelArrayProcessor(RedisChannel.PatternMode.Literal);

        public static readonly ResultProcessor<RedisKey>
                    RedisKey = new RedisKeyProcessor();

        public static readonly ResultProcessor<RedisKey[]>
            RedisKeyArray = new RedisKeyArrayProcessor();

        public static readonly ResultProcessor<RedisType>
            RedisType = new RedisTypeProcessor();

        public static readonly ResultProcessor<RedisValue>
            RedisValue = new RedisValueProcessor();

        public static readonly ResultProcessor<Lease<byte>>
            Lease = new LeaseProcessor();

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
            var ex = new RedisConnectionException(fail, sb.ToString(), innerException);
            SetException(message, ex);
        }

        public static void ConnectionFail(Message message, ConnectionFailureType fail, string errorMessage) =>
            SetException(message, new RedisConnectionException(fail, errorMessage));

        public static void ServerFail(Message message, string errorMessage) =>
            SetException(message, new RedisServerException(errorMessage));

        public static void SetException(Message? message, Exception ex)
        {
            var box = message?.ResultBox;
            box?.SetException(ex);
        }
        // true if ready to be completed (i.e. false if re-issued to another server)
        public virtual bool SetResult(PhysicalConnection connection, Message message, in RawResult result)
        {
            var bridge = connection.BridgeCouldBeNull;
            if (message is LoggingMessage logging)
            {
                try
                {
                    logging.Log?.LogInformation($"Response from {bridge?.Name} / {message.CommandAndKey}: {result}");
                }
                catch { }
            }
            if (result.IsError)
            {
                if (result.StartsWith(CommonReplies.NOAUTH))
                {
                    bridge?.Multiplexer?.SetAuthSuspect(new RedisServerException("NOAUTH Returned - connection has not yet authenticated"));
                }
                else if (result.StartsWith(CommonReplies.WRONGPASS))
                {
                    bridge?.Multiplexer?.SetAuthSuspect(new RedisServerException(result.ToString()));
                }

                var server = bridge?.ServerEndPoint;
                bool log = !message.IsInternalCall;
                bool isMoved = result.StartsWith(CommonReplies.MOVED);
                bool wasNoRedirect = (message.Flags & CommandFlags.NoRedirect) != 0;
                string? err = string.Empty;
                bool unableToConnectError = false;
                if (isMoved || result.StartsWith(CommonReplies.ASK))
                {
                    message.SetResponseReceived();

                    log = false;
                    string[] parts = result.GetString()!.Split(StringSplits.Space, 3);
                    if (Format.TryParseInt32(parts[1], out int hashSlot)
                        && Format.TryParseEndPoint(parts[2], out var endpoint))
                    {
                        // no point sending back to same server, and no point sending to a dead server
                        if (!Equals(server?.EndPoint, endpoint))
                        {
                            if (bridge == null)
                            {
                                // already toast
                            }
                            else if (bridge.Multiplexer.TryResend(hashSlot, message, endpoint, isMoved))
                            {
                                bridge.Multiplexer.Trace(message.Command + " re-issued to " + endpoint, isMoved ? "MOVED" : "ASK");
                                return false;
                            }
                            else
                            {
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
                    }
                }

                if (string.IsNullOrWhiteSpace(err))
                {
                    err = result.GetString()!;
                }

                if (log && server != null)
                {
                    bridge?.Multiplexer.OnErrorMessage(server.EndPoint, err);
                }
                bridge?.Multiplexer?.Trace("Completed with error: " + err + " (" + GetType().Name + ")", ToString());
                if (unableToConnectError)
                {
                    ConnectionFail(message, ConnectionFailureType.UnableToConnect, err);
                }
                else
                {
                    ServerFail(message, err);
                }
            }
            else
            {
                bool coreResult = SetResultCore(connection, message, result);
                if (coreResult)
                {
                    bridge?.Multiplexer?.Trace("Completed with success: " + result.ToString() + " (" + GetType().Name + ")", ToString());
                }
                else
                {
                    UnexpectedResponse(message, result);
                }
            }
            return true;
        }

        protected abstract bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result);

        private void UnexpectedResponse(Message message, in RawResult result)
        {
            ConnectionMultiplexer.TraceWithoutContext("From " + GetType().Name, "Unexpected Response");
            ConnectionFail(message, ConnectionFailureType.ProtocolFailure, "Unexpected response to " + (message?.CommandString ?? "n/a") + ": " + result.ToString());
        }

        public sealed class TimeSpanProcessor : ResultProcessor<TimeSpan?>
        {
            private readonly bool isMilliseconds;
            public TimeSpanProcessor(bool isMilliseconds)
            {
                this.isMilliseconds = isMilliseconds;
            }

            public bool TryParse(in RawResult result, out TimeSpan? expiry)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                        long time;
                        if (result.TryGetInt64(out time))
                        {
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
                            return true;
                        }
                        break;
                    // e.g. OBJECT IDLETIME on a key that doesn't exist
                    case ResultType.BulkString when result.IsNull:
                        expiry = null;
                        return true;
                }
                expiry = null;
                return false;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (TryParse(result, out TimeSpan? expiry))
                {
                    SetResult(message, expiry);
                    return true;
                }
                return false;
            }
        }

        public sealed class TimingProcessor : ResultProcessor<TimeSpan>
        {
            private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

            public static TimerMessage CreateMessage(int db, CommandFlags flags, RedisCommand command, RedisValue value = default) =>
                new TimerMessage(db, flags, command, value);

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.IsError)
                {
                    return false;
                }
                else
                {   // don't check the actual reply; there are multiple ways of constructing
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

                protected override void WriteImpl(PhysicalConnection physical)
                {
                    StartedWritingTimestamp = Stopwatch.GetTimestamp();
                    if (value.IsNull)
                    {
                        physical.WriteHeader(command, 0);
                    }
                    else
                    {
                        physical.WriteHeader(command, 1);
                        physical.WriteBulkString(value);
                    }
                }
                public override int ArgCount => value.IsNull ? 0 : 1;
            }
        }

        public sealed class TrackSubscriptionsProcessor : ResultProcessor<bool>
        {
            private ConnectionMultiplexer.Subscription? Subscription { get; }
            public TrackSubscriptionsProcessor(ConnectionMultiplexer.Subscription? sub) => Subscription = sub;

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeArray == ResultType.Array)
                {
                    var items = result.GetItems();
                    if (items.Length >= 3 && items[2].TryGetInt64(out long count))
                    {
                        connection.SubscriptionCount = count;
                        SetResult(message, true);

                        var newServer = message.Command switch
                        {
                            RedisCommand.SUBSCRIBE or RedisCommand.PSUBSCRIBE => connection.BridgeCouldBeNull?.ServerEndPoint,
                            _ => null
                        };
                        Subscription?.SetCurrentServer(newServer);
                        return true;
                    }
                }
                SetResult(message, false);
                return false;
            }
        }

        internal sealed class DemandZeroOrOneProcessor : ResultProcessor<bool>
        {
            public static bool TryGet(in RawResult result, out bool value)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        if (result.IsEqual(CommonReplies.one)) { value = true; return true; }
                        else if (result.IsEqual(CommonReplies.zero)) { value = false; return true; }
                        break;
                }
                value = false;
                return false;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (TryGet(result, out bool value))
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

            internal static bool IsSHA1(string script) => script is not null && script.Length == SHA1Length && sha1.IsMatch(script);

            internal const int Sha1HashLength = 20;
            internal static byte[] ParseSHA1(byte[] value)
            {
                static int FromHex(char c)
                {
                    if (c >= '0' && c <= '9') return c - '0';
                    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                    return -1;
                }

                if (value?.Length == Sha1HashLength * 2)
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
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.BulkString:
                        var asciiHash = result.GetBlob();
                        if (asciiHash == null || asciiHash.Length != (Sha1HashLength * 2)) return false;

                        // External caller wants the hex bytes, not the ASCII bytes
                        // For nullability/consistency reasons, we always do the parse here.
                        byte[] hash = ParseSHA1(asciiHash);

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
            public static bool TryParse(in RawResult result, out SortedSetEntry? entry)
            {
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        if (result.IsNull || result.ItemsCount < 2)
                        {
                            entry = null;
                        }
                        else
                        {
                            var arr = result.GetItems();
                            entry = new SortedSetEntry(arr[0].AsRedisValue(), arr[1].TryGetDouble(out double val) ? val : double.NaN);
                        }
                        return true;
                    default:
                        entry = null;
                        return false;
                }
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (TryParse(result, out SortedSetEntry? entry))
                {
                    SetResult(message, entry);
                    return true;
                }
                return false;
            }
        }

        internal sealed class SortedSetEntryArrayProcessor : ValuePairInterleavedProcessorBase<SortedSetEntry>
        {
            protected override SortedSetEntry Parse(in RawResult first, in RawResult second, object? state) =>
                new SortedSetEntry(first.AsRedisValue(), second.TryGetDouble(out double val) ? val : double.NaN);
        }

        internal sealed class SortedSetPopResultProcessor : ResultProcessor<SortedSetPopResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeArray == ResultType.Array)
                {
                    if (result.IsNull)
                    {
                        SetResult(message, Redis.SortedSetPopResult.Null);
                        return true;
                    }

                    var arr = result.GetItems();
                    SetResult(message, new SortedSetPopResult(arr[0].AsRedisKey(), arr[1].GetItemsAsSortedSetEntryArray()!));
                    return true;
                }

                return false;
            }
        }

        internal sealed class ListPopResultProcessor : ResultProcessor<ListPopResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeArray == ResultType.Array)
                {
                    if (result.IsNull)
                    {
                        SetResult(message, Redis.ListPopResult.Null);
                        return true;
                    }

                    var arr = result.GetItems();
                    SetResult(message, new ListPopResult(arr[0].AsRedisKey(), arr[1].GetItemsAsValues()!));
                    return true;
                }

                return false;
            }
        }


        internal sealed class HashEntryArrayProcessor : ValuePairInterleavedProcessorBase<HashEntry>
        {
            protected override HashEntry Parse(in RawResult first, in RawResult second, object? state) =>
                new HashEntry(first.AsRedisValue(), second.AsRedisValue());
        }

        internal abstract class ValuePairInterleavedProcessorBase<T> : ResultProcessor<T[]>
        {
            // when RESP3 was added, some interleaved value/pair responses: became jagged instead;
            // this isn't strictly a RESP3 thing (RESP2 supports jagged), but: it is a thing that
            // happened, and we need to handle that; thus, by default, we'll detect jagged data
            // and handle it automatically; this virtual is included so we can turn it off
            // on a per-processor basis if needed
            protected virtual bool AllowJaggedPairs => true;

            public bool TryParse(in RawResult result, out T[]? pairs)
                => TryParse(result, out pairs, false, out _);

            public T[]? ParseArray(in RawResult result, bool allowOversized, out int count, object? state)
            {
                if (result.IsNull)
                {
                    count = 0;
                    return null;
                }

                var arr = result.GetItems();
                count = (int)arr.Length;
                if (count == 0)
                {
                    return Array.Empty<T>();
                }

                bool interleaved = !(result.IsResp3 && AllowJaggedPairs && IsAllJaggedPairs(arr));
                if (interleaved) count >>= 1; // so: half of that
                var pairs = allowOversized ? ArrayPool<T>.Shared.Rent(count) : new T[count];

                if (interleaved)
                {
                    // linear elements i.e. {key,value,key,value,key,value}
                    if (arr.IsSingleSegment)
                    {
                        var span = arr.FirstSpan;
                        int offset = 0;
                        for (int i = 0; i < count; i++)
                        {
                            pairs[i] = Parse(span[offset++], span[offset++], state);
                        }
                    }
                    else
                    {
                        var iter = arr.GetEnumerator(); // simplest way of getting successive values
                        for (int i = 0; i < count; i++)
                        {
                            pairs[i] = Parse(iter.GetNext(), iter.GetNext(), state);
                        }
                    }
                }
                else
                {
                    // jagged elements i.e. {{key,value},{key,value},{key,value}}
                    // to get here, we've already asserted that all elements are arrays with length 2
                    if (arr.IsSingleSegment)
                    {
                        int i = 0;
                        foreach (var el in arr.FirstSpan)
                        {
                            var inner = el.GetItems();
                            pairs[i++] = Parse(inner[0], inner[1], state);
                        }
                    }
                    else
                    {
                        var iter = arr.GetEnumerator(); // simplest way of getting successive values
                        for (int i = 0; i < count; i++)
                        {
                            var inner = iter.GetNext().GetItems();
                            pairs[i] = Parse(inner[0], inner[1], state);
                        }
                    }
                }
                return pairs;

                static bool IsAllJaggedPairs(in Sequence<RawResult> arr)
                {
                    return arr.IsSingleSegment ? CheckSpan(arr.FirstSpan) : CheckSpans(arr);

                    static bool CheckSpans(in Sequence<RawResult> arr)
                    {
                        foreach (var chunk in arr.Spans)
                        {
                            if (!CheckSpan(chunk)) return false;
                        }
                        return true;
                    }
                    static bool CheckSpan(ReadOnlySpan<RawResult> chunk)
                    {
                        // check whether each value is actually an array of length 2
                        foreach (ref readonly RawResult el in chunk)
                        {
                            if (el is not { Resp2TypeArray: ResultType.Array, ItemsCount: 2 }) return false;
                        }
                        return true;
                    }
                }
            }

            public bool TryParse(in RawResult result, out T[]? pairs, bool allowOversized, out int count)
            {
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        pairs = ParseArray(in result, allowOversized, out count, null);
                        return true;
                    default:
                        count = 0;
                        pairs = null;
                        return false;
                }
            }

            protected abstract T Parse(in RawResult first, in RawResult second, object? state);
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (TryParse(result, out T[]? arr))
                {
                    SetResult(message, arr!);
                    return true;
                }
                return false;
            }
        }

        internal sealed class AutoConfigureProcessor : ResultProcessor<bool>
        {
            private ILogger? Log { get; }
            public AutoConfigureProcessor(ILogger? log = null) => Log = log;

            public override bool SetResult(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.IsError && result.StartsWith(CommonReplies.READONLY))
                {
                    var bridge = connection.BridgeCouldBeNull;
                    if (bridge != null)
                    {
                        var server = bridge.ServerEndPoint;
                        Log?.LogInformation($"{Format.ToString(server)}: Auto-configured role: replica");
                        server.IsReplica = true;
                    }
                }

                return base.SetResult(connection, message, result);
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                var server = connection.BridgeCouldBeNull?.ServerEndPoint;
                if (server == null) return false;

                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                        if (message?.Command == RedisCommand.CLIENT)
                        {
                            if (result.TryGetInt64(out long clientId))
                            {
                                connection.ConnectionId = clientId;
                                Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (CLIENT) connection-id: {clientId}");
                            }
                        }
                        break;
                    case ResultType.BulkString:
                        if (message?.Command == RedisCommand.INFO)
                        {
                            string? info = result.GetString();
                            if (string.IsNullOrWhiteSpace(info))
                            {
                                SetResult(message, true);
                                return true;
                            }
                            string? primaryHost = null, primaryPort = null;
                            bool roleSeen = false;
                            using (var reader = new StringReader(info))
                            {
                                while (reader.ReadLine() is string line)
                                {
                                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("# "))
                                    {
                                        continue;
                                    }

                                    string? val;
                                    if ((val = Extract(line, "role:")) != null)
                                    {
                                        roleSeen = true;
                                        if (TryParseRole(val, out bool isReplica))
                                        {
                                            server.IsReplica = isReplica;
                                            Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (INFO) role: {(isReplica ? "replica" : "primary")}");
                                        }
                                    }
                                    else if ((val = Extract(line, "master_host:")) != null)
                                    {
                                        primaryHost = val;
                                    }
                                    else if ((val = Extract(line, "master_port:")) != null)
                                    {
                                        primaryPort = val;
                                    }
                                    else if ((val = Extract(line, "redis_version:")) != null)
                                    {
                                        if (Format.TryParseVersion(val, out Version? version))
                                        {
                                            server.Version = version;
                                            Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (INFO) version: " + version);
                                        }
                                    }
                                    else if ((val = Extract(line, "redis_mode:")) != null)
                                    {
                                        if (TryParseServerType(val, out var serverType))
                                        {
                                            server.ServerType = serverType;
                                            Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (INFO) server-type: {serverType}");
                                        }
                                    }
                                    else if ((val = Extract(line, "run_id:")) != null)
                                    {
                                        server.RunId = val;
                                    }
                                }
                                if (roleSeen && Format.TryParseEndPoint(primaryHost!, primaryPort, out var sep))
                                {
                                    // These are in the same section, if present
                                    server.PrimaryEndPoint = sep;
                                }
                            }
                        }
                        else if (message?.Command == RedisCommand.SENTINEL)
                        {
                            server.ServerType = ServerType.Sentinel;
                            Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (SENTINEL) server-type: sentinel");
                        }
                        SetResult(message, true);
                        return true;
                    case ResultType.Array:
                        if (message?.Command == RedisCommand.CONFIG)
                        {
                            var iter = result.GetItems().GetEnumerator();
                            while(iter.MoveNext())
                            {
                                ref RawResult key = ref iter.Current;
                                if (!iter.MoveNext()) break;
                                ref RawResult val = ref iter.Current;

                                if (key.IsEqual(CommonReplies.timeout) && val.TryGetInt64(out long i64))
                                {
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
                                        Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (CONFIG) timeout: " + targetSeconds + "s");
                                        server.WriteEverySeconds = targetSeconds;
                                    }
                                }
                                else if (key.IsEqual(CommonReplies.databases) && val.TryGetInt64(out i64))
                                {
                                    int dbCount = checked((int)i64);
                                    Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (CONFIG) databases: " + dbCount);
                                    server.Databases = dbCount;
                                }
                                else if (key.IsEqual(CommonReplies.slave_read_only) || key.IsEqual(CommonReplies.replica_read_only))
                                {
                                    if (val.IsEqual(CommonReplies.yes))
                                    {
                                        server.ReplicaReadOnly = true;
                                        Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (CONFIG) read-only replica: true");
                                    }
                                    else if (val.IsEqual(CommonReplies.no))
                                    {
                                        server.ReplicaReadOnly = false;
                                        Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (CONFIG) read-only replica: false");
                                    }
                                }
                            }
                        }
                        else if (message?.Command == RedisCommand.HELLO)
                        {
                            var iter = result.GetItems().GetEnumerator();
                            while (iter.MoveNext())
                            {
                                ref RawResult key = ref iter.Current;
                                if (!iter.MoveNext()) break;
                                ref RawResult val = ref iter.Current;

                                if (key.IsEqual(CommonReplies.version) && Format.TryParseVersion(val.GetString(), out var version))
                                {
                                    server.Version = version;
                                    Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (HELLO) server-version: {version}");
                                }
                                else if (key.IsEqual(CommonReplies.proto) && val.TryGetInt64(out var i64))
                                {
                                    connection.SetProtocol(i64 >= 3 ? RedisProtocol.Resp3 : RedisProtocol.Resp2);
                                    Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (HELLO) protocol: {connection.Protocol}");
                                }
                                else if (key.IsEqual(CommonReplies.id) && val.TryGetInt64(out i64))
                                {
                                    connection.ConnectionId = i64;
                                    Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (HELLO) connection-id: {i64}");
                                }
                                else if (key.IsEqual(CommonReplies.mode) && TryParseServerType(val.GetString(), out var serverType))
                                {
                                    server.ServerType = serverType;
                                    Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (HELLO) server-type: {serverType}");
                                }
                                else if (key.IsEqual(CommonReplies.role) && TryParseRole(val.GetString(), out bool isReplica))
                                {
                                    server.IsReplica = isReplica;
                                    Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (HELLO) role: {(isReplica ? "replica" : "primary")}");
                                }
                            }
                        }
                        else if (message?.Command == RedisCommand.SENTINEL)
                        {
                            server.ServerType = ServerType.Sentinel;
                            Log?.LogInformation($"{Format.ToString(server)}: Auto-configured (SENTINEL) server-type: sentinel");
                        }
                        SetResult(message, true);
                        return true;
                }
                return false;
            }

            private static string? Extract(string line, string prefix)
            {
                if (line.StartsWith(prefix)) return line.Substring(prefix.Length).Trim();
                return null;
            }

            private static bool TryParseServerType(string? val, out ServerType serverType)
            {
                switch (val)
                {
                    case "standalone":
                        serverType = ServerType.Standalone;
                        return true;
                    case "cluster":
                        serverType = ServerType.Cluster;
                        return true;
                    case "sentinel":
                        serverType = ServerType.Sentinel;
                        return true;
                    default:
                        serverType = default;
                        return false;
                }
            }

            private static bool TryParseRole(string? val, out bool isReplica)
            {
                switch (val)
                {
                    case "primary":
                    case "master":
                        isReplica = false;
                        return true;
                    case "replica":
                    case "slave":
                        isReplica = true;
                        return true;
                    default:
                        isReplica = default;
                        return false;
                }
            }

            internal static ResultProcessor<bool> Create(ILogger? log) => log is null ? AutoConfigure : new AutoConfigureProcessor(log);
        }

        private sealed class BooleanProcessor : ResultProcessor<bool>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.IsNull)
                {
                    SetResult(message, false); // lots of ops return (nil) when they mean "no"
                    return true;
                }
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.SimpleString:
                        if (result.IsEqual(CommonReplies.OK))
                        {
                            SetResult(message, true);
                        }
                        else
                        {
                            SetResult(message, result.GetBoolean());
                        }
                        return true;
                    case ResultType.Integer:
                    case ResultType.BulkString:
                        SetResult(message, result.GetBoolean());
                        return true;
                    case ResultType.Array:
                        var items = result.GetItems();
                        if (items.Length == 1)
                        { // treat an array of 1 like a single reply (for example, SCRIPT EXISTS)
                            SetResult(message, items[0].GetBoolean());
                            return true;
                        }
                        break;
                }
                return false;
            }
        }

        private sealed class ByteArrayProcessor : ResultProcessor<byte[]?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.BulkString:
                        SetResult(message, result.GetBlob());
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

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.BulkString:
                        string nodes = result.GetString()!;
                        var bridge = connection.BridgeCouldBeNull;
                        if (bridge != null) bridge.ServerEndPoint.ServerType = ServerType.Cluster;
                        var config = Parse(connection, nodes);
                        SetResult(message, config);
                        return true;
                }
                return false;
            }
        }

        private sealed class ClusterNodesRawProcessor : ResultProcessor<string?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        string nodes = result.GetString()!;
                        try
                        { ClusterNodesProcessor.Parse(connection, nodes); }
                        catch
                        { /* tralalalala */}
                        SetResult(message, nodes);
                        return true;
                }
                return false;
            }
        }

        private sealed class ConnectionIdentityProcessor : ResultProcessor<EndPoint>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (connection.BridgeCouldBeNull is PhysicalBridge bridge)
                {
                    SetResult(message, bridge.ServerEndPoint.EndPoint);
                    return true;
                }
                return false;
            }
        }

        private sealed class DateTimeProcessor : ResultProcessor<DateTime>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                long unixTime;
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Integer:
                        if (result.TryGetInt64(out unixTime))
                        {
                            var time = RedisBase.UnixEpoch.AddSeconds(unixTime);
                            SetResult(message, time);
                            return true;
                        }
                        break;
                    case ResultType.Array:
                        var arr = result.GetItems();
                        switch (arr.Length)
                        {
                            case 1:
                                if (arr.FirstSpan[0].TryGetInt64(out unixTime))
                                {
                                    var time = RedisBase.UnixEpoch.AddSeconds(unixTime);
                                    SetResult(message, time);
                                    return true;
                                }
                                break;
                            case 2:
                                if (arr[0].TryGetInt64(out unixTime) && arr[1].TryGetInt64(out long micros))
                                {
                                    var time = RedisBase.UnixEpoch.AddSeconds(unixTime).AddTicks(micros * 10); // DateTime ticks are 100ns
                                    SetResult(message, time);
                                    return true;
                                }
                                break;
                        }
                        break;
                }
                return false;
            }
        }

        public sealed class NullableDateTimeProcessor : ResultProcessor<DateTime?>
        {
            private readonly bool isMilliseconds;
            public NullableDateTimeProcessor(bool fromMilliseconds) => isMilliseconds = fromMilliseconds;

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer when result.TryGetInt64(out var duration):
                        DateTime? expiry = duration switch
                        {
                            // -1 means no expiry and -2 means key does not exist
                            < 0 => null,
                            _ when isMilliseconds => RedisBase.UnixEpoch.AddMilliseconds(duration),
                            _ => RedisBase.UnixEpoch.AddSeconds(duration)
                        };
                        SetResult(message, expiry);
                        return true;

                    case ResultType.BulkString when result.IsNull:
                        SetResult(message, null);
                        return true;
                }
                return false;
            }
        }

        private sealed class DoubleProcessor : ResultProcessor<double>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                        long i64;
                        if (result.TryGetInt64(out i64))
                        {
                            SetResult(message, i64);
                            return true;
                        }
                        break;
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        double val;
                        if (result.TryGetDouble(out val))
                        {
                            SetResult(message, val);
                            return true;
                        }
                        break;
                }
                return false;
            }
        }

        private sealed class ExpectBasicStringProcessor : ResultProcessor<bool>
        {
            private readonly CommandBytes _expected;
            private readonly bool _startsWith;
            public ExpectBasicStringProcessor(CommandBytes expected, bool startsWith = false)
            {
                _expected = expected;
                _startsWith = startsWith;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (_startsWith ? result.StartsWith(_expected) : result.IsEqual(_expected))
                {
                    SetResult(message, true);
                    return true;
                }
                if(message.Command == RedisCommand.AUTH) connection?.BridgeCouldBeNull?.Multiplexer?.SetAuthSuspect(new RedisException("Unknown AUTH exception"));
                return false;
            }
        }

        private sealed class InfoProcessor : ResultProcessor<IGrouping<string, KeyValuePair<string, string>>[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeBulkString == ResultType.BulkString)
                {
                    string category = Normalize(null);
                    var list = new List<Tuple<string, KeyValuePair<string, string>>>();
                    var raw = result.GetString();
                    if (raw is not null)
                    {
                        using var reader = new StringReader(raw);
                        while (reader.ReadLine() is string line)
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

        private class Int64DefaultValueProcessor : ResultProcessor<long>
        {
            private readonly long _defaultValue;

            public Int64DefaultValueProcessor(long defaultValue) => _defaultValue = defaultValue;

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.IsNull)
                {
                    SetResult(message, _defaultValue);
                    return true;
                }
                if (result.Resp2TypeBulkString == ResultType.Integer && result.TryGetInt64(out var i64))
                {
                    SetResult(message, i64);
                    return true;
                }
                return false;
            }
        }

        private class Int64Processor : ResultProcessor<long>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        long i64;
                        if (result.TryGetInt64(out i64))
                        {
                            SetResult(message, i64);
                            return true;
                        }
                        break;
                }
                return false;
            }
        }

        private class PubSubNumSubProcessor : Int64Processor
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeArray == ResultType.Array)
                {
                    var arr = result.GetItems();
                    if (arr.Length == 2 && arr[1].TryGetInt64(out long val))
                    {
                        SetResult(message, val);
                        return true;
                    }
                }
                return base.SetResultCore(connection, message, result);
            }
        }

        private sealed class NullableDoubleArrayProcessor : ResultProcessor<double?[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeArray == ResultType.Array && !result.IsNull)
                {
                    var arr = result.GetItemsAsDoubles()!;
                    SetResult(message, arr);
                    return true;
                }
                return false;
            }
        }

        private sealed class NullableDoubleProcessor : ResultProcessor<double?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        if (result.IsNull)
                        {
                            SetResult(message, null);
                            return true;
                        }
                        double val;
                        if (result.TryGetDouble(out val))
                        {
                            SetResult(message, val);
                            return true;
                        }
                        break;
                }
                return false;
            }
        }

        private sealed class NullableInt64Processor : ResultProcessor<long?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        if (result.IsNull)
                        {
                            SetResult(message, null);
                            return true;
                        }
                        long i64;
                        if (result.TryGetInt64(out i64))
                        {
                            SetResult(message, i64);
                            return true;
                        }
                        break;
                }
                return false;
            }
        }

        private sealed class RedisChannelArrayProcessor : ResultProcessor<RedisChannel[]>
        {
            private readonly RedisChannel.PatternMode mode;
            public RedisChannelArrayProcessor(RedisChannel.PatternMode mode)
            {
                this.mode = mode;
            }

            private readonly struct ChannelState // I would use a value-tuple here, but that is binding hell
            {
                public readonly byte[]? Prefix;
                public readonly RedisChannel.PatternMode Mode;
                public ChannelState(byte[]? prefix, RedisChannel.PatternMode mode)
                {
                    Prefix = prefix;
                    Mode = mode;
                }
            }
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        var final = result.ToArray(
                                (in RawResult item, in ChannelState state) => item.AsRedisChannel(state.Prefix, state.Mode),
                                new ChannelState(connection.ChannelPrefix, mode))!;

                        SetResult(message, final);
                        return true;
                }
                return false;
            }
        }

        private sealed class RedisKeyArrayProcessor : ResultProcessor<RedisKey[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        var arr = result.GetItemsAsKeys()!;
                        SetResult(message, arr);
                        return true;
                }
                return false;
            }
        }

        private sealed class RedisKeyProcessor : ResultProcessor<RedisKey>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        SetResult(message, result.AsRedisKey());
                        return true;
                }
                return false;
            }
        }

        private sealed class RedisTypeProcessor : ResultProcessor<RedisType>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        string s = result.GetString()!;
                        RedisType value;
                        if (string.Equals(s, "zset", StringComparison.OrdinalIgnoreCase)) value = Redis.RedisType.SortedSet;
                        else if (!Enum.TryParse<RedisType>(s, true, out value)) value = global::StackExchange.Redis.RedisType.Unknown;
                        SetResult(message, value);
                        return true;
                }
                return false;
            }
        }

        private sealed class RedisValueArrayProcessor : ResultProcessor<RedisValue[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    // allow a single item to pass explicitly pretending to be an array; example: SPOP {key} 1
                    case ResultType.BulkString:
                        // If the result is nil, the result should be an empty array
                        var arr = result.IsNull
                            ? Array.Empty<RedisValue>()
                            : new[] { result.AsRedisValue() };
                        SetResult(message, arr);
                        return true;
                    case ResultType.Array:
                        arr = result.GetItemsAsValues()!;
                        SetResult(message, arr);
                        return true;
                }
                return false;
            }
        }

        private sealed class Int64ArrayProcessor : ResultProcessor<long[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeArray == ResultType.Array && !result.IsNull)
                {
                    var arr = result.ToArray((in RawResult x) => (long)x.AsRedisValue())!;
                    SetResult(message, arr);
                    return true;
                }

                return false;
            }
        }

        private sealed class NullableStringArrayProcessor : ResultProcessor<string?[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        var arr = result.GetItemsAsStrings()!;

                        SetResult(message, arr);
                        return true;
                }
                return false;
            }
        }

        private sealed class StringArrayProcessor : ResultProcessor<string[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        var arr = result.GetItemsAsStringsNotNullable()!;
                        SetResult(message, arr);
                        return true;
                }
                return false;
            }
        }

        private sealed class BooleanArrayProcessor : ResultProcessor<bool[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeArray == ResultType.Array && !result.IsNull)
                {
                    var arr = result.GetItemsAsBooleans()!;
                    SetResult(message, arr);
                    return true;
                }
                return false;
            }
        }

        private sealed class RedisValueGeoPositionProcessor : ResultProcessor<GeoPosition?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        var pos = result.GetItemsAsGeoPosition();

                        SetResult(message, pos);
                        return true;
                }
                return false;
            }
        }

        private sealed class RedisValueGeoPositionArrayProcessor : ResultProcessor<GeoPosition?[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        var arr = result.GetItemsAsGeoPositionArray()!;

                        SetResult(message, arr);
                        return true;
                }
                return false;
            }
        }

        private sealed class GeoRadiusResultArrayProcessor : ResultProcessor<GeoRadiusResult[]>
        {
            private static readonly GeoRadiusResultArrayProcessor[] instances;
            private readonly GeoRadiusOptions options;

            static GeoRadiusResultArrayProcessor()
            {
                instances = new GeoRadiusResultArrayProcessor[8];
                for (int i = 0; i < 8; i++) instances[i] = new GeoRadiusResultArrayProcessor((GeoRadiusOptions)i);
            }

            public static GeoRadiusResultArrayProcessor Get(GeoRadiusOptions options)
            {
                int i = (int)options;
                if (i < 0 || i >= instances.Length) throw new ArgumentOutOfRangeException(nameof(options));
                return instances[i];
            }

            private GeoRadiusResultArrayProcessor(GeoRadiusOptions options)
            {
                this.options = options;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        var typed = result.ToArray(
                            (in RawResult item, in GeoRadiusOptions radiusOptions) => Parse(item, radiusOptions), options)!;
                        SetResult(message, typed);
                        return true;
                }
                return false;
            }

            private static GeoRadiusResult Parse(in RawResult item, GeoRadiusOptions options)
            {
                if (options == GeoRadiusOptions.None)
                {
                    // Without any WITH option specified, the command just returns a linear array like ["New York","Milan","Paris"].
                    return new GeoRadiusResult(item.AsRedisValue(), null, null, null);
                }
                // If WITHCOORD, WITHDIST or WITHHASH options are specified, the command returns an array of arrays, where each sub-array represents a single item.
                var iter = item.GetItems().GetEnumerator();

                // the first item in the sub-array is always the name of the returned item.
                var member = iter.GetNext().AsRedisValue();

                /*  The other information is returned in the following order as successive elements of the sub-array.
The distance from the center as a floating point number, in the same unit specified in the radius.
The geohash integer.
The coordinates as a two items x,y array (longitude,latitude).
                 */
                double? distance = null;
                GeoPosition? position = null;
                long? hash = null;
                if ((options & GeoRadiusOptions.WithDistance) != 0) { distance = (double?)iter.GetNext().AsRedisValue(); }
                if ((options & GeoRadiusOptions.WithGeoHash) != 0) { hash = (long?)iter.GetNext().AsRedisValue(); }
                if ((options & GeoRadiusOptions.WithCoordinates) != 0)
                {
                    var coords = iter.GetNext().GetItems();
                    double longitude = (double)coords[0].AsRedisValue(), latitude = (double)coords[1].AsRedisValue();
                    position = new GeoPosition(longitude, latitude);
                }
                return new GeoRadiusResult(member, distance, hash, position);
            }
        }

        /// <summary>
        /// Parser for the https://redis.io/commands/lcs/ format with the <see cref="RedisLiterals.IDX"/> and <see cref="RedisLiterals.WITHMATCHLEN"/> arguments.
        /// </summary>
        /// <remarks>
        /// Example response:
        /// 1) "matches"
        /// 2) 1) 1) 1) (integer) 4
        ///          2) (integer) 7
        ///       2) 1) (integer) 5
        ///          2) (integer) 8
        ///       3) (integer) 4
        /// 3) "len"
        /// 4) (integer) 6
        /// </remarks>
        private sealed class LongestCommonSubsequenceProcessor : ResultProcessor<LCSMatchResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        SetResult(message, Parse(result));
                        return true;
                }
                return false;
            }

            private static LCSMatchResult Parse(in RawResult result)
            {
                var topItems = result.GetItems();
                var matches = new LCSMatchResult.LCSMatch[topItems[1].GetItems().Length];
                int i = 0;
                var matchesRawArray = topItems[1]; // skip the first element (title "matches")
                foreach (var match in matchesRawArray.GetItems())
                {
                    var matchItems = match.GetItems();

                    matches[i++] = new LCSMatchResult.LCSMatch(
                        firstStringIndex: (long)matchItems[0].GetItems()[0].AsRedisValue(),
                        secondStringIndex: (long)matchItems[1].GetItems()[0].AsRedisValue(),
                        length: (long)matchItems[2].AsRedisValue());
                }
                var len = (long)topItems[3].AsRedisValue();

                return new LCSMatchResult(matches, len);
            }
        }

        private sealed class RedisValueProcessor : ResultProcessor<RedisValue>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        SetResult(message, result.AsRedisValue());
                        return true;
                }
                return false;
            }
        }

        private sealed class RoleProcessor : ResultProcessor<Role>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                var items = result.GetItems();
                if (items.IsEmpty)
                {
                    return false;
                }

                ref var val = ref items[0];
                Role? role;
                if (val.IsEqual(RedisLiterals.master)) role = ParsePrimary(items);
                else if (val.IsEqual(RedisLiterals.slave)) role = ParseReplica(items, RedisLiterals.slave!);
                else if (val.IsEqual(RedisLiterals.replica)) role = ParseReplica(items, RedisLiterals.replica!); // for when "slave" is deprecated
                else if (val.IsEqual(RedisLiterals.sentinel)) role = ParseSentinel(items);
                else role = new Role.Unknown(val.GetString()!);

                if (role is null) return false;
                SetResult(message, role);
                return true;
            }

            private static Role? ParsePrimary(in Sequence<RawResult> items)
            {
                if (items.Length < 3)
                {
                    return null;
                }

                if (!items[1].TryGetInt64(out var offset))
                {
                    return null;
                }

                var replicaItems = items[2].GetItems();
                ICollection<Role.Master.Replica> replicas;
                if (replicaItems.IsEmpty)
                {
                    replicas = Array.Empty<Role.Master.Replica>();
                }
                else
                {
                    replicas = new List<Role.Master.Replica>((int)replicaItems.Length);
                    for (int i = 0; i < replicaItems.Length; i++)
                    {
                        if (TryParsePrimaryReplica(replicaItems[i].GetItems(), out var replica))
                        {
                            replicas.Add(replica);
                        }
                        else
                        {
                            return null;
                        }
                    }
                }

                return new Role.Master(offset, replicas);
            }

            private static bool TryParsePrimaryReplica(in Sequence<RawResult> items, out Role.Master.Replica replica)
            {
                if (items.Length < 3)
                {
                    replica = default;
                    return false;
                }

                var primaryIp = items[0].GetString()!;

                if (!items[1].TryGetInt64(out var primaryPort) || primaryPort > int.MaxValue)
                {
                    replica = default;
                    return false;
                }

                if (!items[2].TryGetInt64(out var replicationOffset))
                {
                    replica = default;
                    return false;
                }

                replica = new Role.Master.Replica(primaryIp, (int)primaryPort, replicationOffset);
                return true;
            }

            private static Role? ParseReplica(in Sequence<RawResult> items, string role)
            {
                if (items.Length < 5)
                {
                    return null;
                }

                var primaryIp = items[1].GetString()!;

                if (!items[2].TryGetInt64(out var primaryPort) || primaryPort > int.MaxValue)
                {
                    return null;
                }

                ref var val = ref items[3];
                string replicationState;
                if (val.IsEqual(RedisLiterals.connect)) replicationState = RedisLiterals.connect!;
                else if (val.IsEqual(RedisLiterals.connecting)) replicationState = RedisLiterals.connecting!;
                else if (val.IsEqual(RedisLiterals.sync)) replicationState = RedisLiterals.sync!;
                else if (val.IsEqual(RedisLiterals.connected)) replicationState = RedisLiterals.connected!;
                else if (val.IsEqual(RedisLiterals.none)) replicationState = RedisLiterals.none!;
                else if (val.IsEqual(RedisLiterals.handshake)) replicationState = RedisLiterals.handshake!;
                else replicationState = val.GetString()!;

                if (!items[4].TryGetInt64(out var replicationOffset))
                {
                    return null;
                }

                return new Role.Replica(role, primaryIp, (int)primaryPort, replicationState, replicationOffset);
            }

            private static Role? ParseSentinel(in Sequence<RawResult> items)
            {
                if (items.Length < 2)
                {
                    return null;
                }
                var primaries = items[1].GetItemsAsStrings()!;
                return new Role.Sentinel(primaries);
            }
        }

        private sealed class LeaseProcessor : ResultProcessor<Lease<byte>>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        SetResult(message, result.AsLease()!);
                        return true;
                }
                return false;
            }
        }

        private class ScriptResultProcessor : ResultProcessor<RedisResult>
        {
            public override bool SetResult(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.IsError && result.StartsWith(CommonReplies.NOSCRIPT))
                { // scripts are not flushed individually, so assume the entire script cache is toast ("SCRIPT FLUSH")
                    connection.BridgeCouldBeNull?.ServerEndPoint?.FlushScriptCache();
                    message.SetScriptUnavailable();
                }
                // and apply usual processing for the rest
                return base.SetResult(connection, message, result);
            }

            // note that top-level error messages still get handled by SetResult, but nested errors
            // (is that a thing?) will be wrapped in the RedisResult
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (RedisResult.TryCreate(connection, result, out var value))
                {
                    SetResult(message, value);
                    return true;
                }
                return false;
            }
        }

        internal sealed class SingleStreamProcessor : StreamProcessorBase<StreamEntry[]>
        {
            private readonly bool skipStreamName;

            public SingleStreamProcessor(bool skipStreamName = false)
            {
                this.skipStreamName = skipStreamName;
            }

            /// <summary>
            /// Handles <see href="https://redis.io/commands/xread"/>.
            /// </summary>
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.IsNull)
                {
                    // Server returns 'nil' if no entries are returned for the given stream.
                    SetResult(message, Array.Empty<StreamEntry>());
                    return true;
                }

                if (result.Resp2TypeArray != ResultType.Array)
                {
                    return false;
                }

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

                    ref readonly RawResult readResult = ref (result.Resp3Type == ResultType.Map ? ref result[1] : ref result[0][1]);
                    entries = ParseRedisStreamEntries(readResult);
                }
                else
                {
                    entries = ParseRedisStreamEntries(result);
                }

                SetResult(message, entries);
                return true;
            }
        }

        /// <summary>
        /// Handles <see href="https://redis.io/commands/xread"/>.
        /// </summary>
        internal sealed class MultiStreamProcessor : StreamProcessorBase<RedisStream[]>
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
            */

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.IsNull)
                {
                    // Nothing returned for any of the requested streams. The server returns 'nil'.
                    SetResult(message, Array.Empty<RedisStream>());
                    return true;
                }

                if (result.Resp2TypeArray != ResultType.Array)
                {
                    return false;
                }

                RedisStream[] streams;
                if (result.Resp3Type == ResultType.Map) // see SetResultCore for the shape delta between RESP2 and RESP3
                {
                    // root is a map of named inner-arrays
                    streams = RedisStreamInterleavedProcessor.Instance.ParseArray(result, false, out _, this)!; // null-checked
                }
                else
                {
                    streams = result.GetItems().ToArray((in RawResult item, in MultiStreamProcessor obj) =>
                    {
                        var details = item.GetItems();

                        // details[0] = Name of the Stream
                        // details[1] = Multibulk Array of Stream Entries
                        return new RedisStream(key: details[0].AsRedisKey(),
                            entries: obj.ParseRedisStreamEntries(details[1])!);
                    }, this);
                }

                SetResult(message, streams);
                return true;
            }
        }

        private sealed class RedisStreamInterleavedProcessor : ValuePairInterleavedProcessorBase<RedisStream>
        {
            protected override bool AllowJaggedPairs => false; // we only use this on a flattened map

            public static readonly RedisStreamInterleavedProcessor Instance = new();
            private RedisStreamInterleavedProcessor() { }
            protected override RedisStream Parse(in RawResult first, in RawResult second, object? state)
                => new(key: first.AsRedisKey(), entries: ((MultiStreamProcessor)state!).ParseRedisStreamEntries(second));
        }

        /// <summary>
        /// This processor is for <see cref="RedisCommand.XAUTOCLAIM"/> *without* the <see cref="StreamConstants.JustId"/> option.
        /// </summary>
        internal sealed class StreamAutoClaimProcessor : StreamProcessorBase<StreamAutoClaimResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                // See https://redis.io/commands/xautoclaim for command documentation.
                // Note that the result should never be null, so intentionally treating it as a failure to parse here
                if (result.Resp2TypeArray == ResultType.Array && !result.IsNull)
                {
                    var items = result.GetItems();

                    // [0] The next start ID.
                    var nextStartId = items[0].AsRedisValue();
                    // [1] The array of StreamEntry's.
                    var entries = ParseRedisStreamEntries(items[1]);
                    // [2] The array of message IDs deleted from the stream that were in the PEL.
                    //     This is not available in 6.2 so we need to be defensive when reading this part of the response.
                    var deletedIds = (items.Length == 3 ? items[2].GetItemsAsValues() : null) ?? Array.Empty<RedisValue>();

                    SetResult(message, new StreamAutoClaimResult(nextStartId, entries, deletedIds));
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// This processor is for <see cref="RedisCommand.XAUTOCLAIM"/> *with* the <see cref="StreamConstants.JustId"/> option.
        /// </summary>
        internal sealed class StreamAutoClaimIdsOnlyProcessor : ResultProcessor<StreamAutoClaimIdsOnlyResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                // See https://redis.io/commands/xautoclaim for command documentation.
                // Note that the result should never be null, so intentionally treating it as a failure to parse here
                if (result.Resp2TypeArray == ResultType.Array && !result.IsNull)
                {
                    var items = result.GetItems();

                    // [0] The next start ID.
                    var nextStartId = items[0].AsRedisValue();
                    // [1] The array of claimed message IDs.
                    var claimedIds = items[1].GetItemsAsValues() ?? Array.Empty<RedisValue>();
                    // [2] The array of message IDs deleted from the stream that were in the PEL.
                    //     This is not available in 6.2 so we need to be defensive when reading this part of the response.
                    var deletedIds = (items.Length == 3 ? items[2].GetItemsAsValues() : null) ?? Array.Empty<RedisValue>();

                    SetResult(message, new StreamAutoClaimIdsOnlyResult(nextStartId, claimedIds, deletedIds));
                    return true;
                }

                return false;
            }
        }

        internal sealed class StreamConsumerInfoProcessor : InterleavedStreamInfoProcessorBase<StreamConsumerInfo>
        {
            protected override StreamConsumerInfo ParseItem(in RawResult result)
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

                var arr = result.GetItems();
                string? name = default;
                int pendingMessageCount = default;
                long idleTimeInMilliseconds = default;

                KeyValuePairParser.TryRead(arr, KeyValuePairParser.Name, ref name);
                KeyValuePairParser.TryRead(arr, KeyValuePairParser.Pending, ref pendingMessageCount);
                KeyValuePairParser.TryRead(arr, KeyValuePairParser.Idle, ref idleTimeInMilliseconds);

                return new StreamConsumerInfo(name!, pendingMessageCount, idleTimeInMilliseconds);
            }
        }

        private static class KeyValuePairParser
        {
            internal static readonly CommandBytes
                Name = "name",
                Consumers = "consumers",
                Pending = "pending",
                Idle = "idle",
                LastDeliveredId = "last-delivered-id",
                EntriesRead = "entries-read",
                Lag = "lag",
                IP = "ip",
                Port = "port";

            internal static bool TryRead(Sequence<RawResult> pairs, in CommandBytes key, ref long value)
            {
                var len = pairs.Length / 2;
                for (int i = 0; i < len; i++)
                {
                    if (pairs[i * 2].IsEqual(key) && pairs[(i * 2) + 1].TryGetInt64(out var tmp))
                    {
                        value = tmp;
                        return true;
                    }
                }
                return false;
            }

            internal static bool TryRead(Sequence<RawResult> pairs, in CommandBytes key, ref int value)
            {
                long tmp = default;
                if(TryRead(pairs, key, ref tmp)) {
                    value = checked((int)tmp);
                    return true;
                }
                return false;
            }

            internal static bool TryRead(Sequence<RawResult> pairs, in CommandBytes key, [NotNullWhen(true)] ref string? value)
            {
                var len = pairs.Length / 2;
                for (int i = 0; i < len; i++)
                {
                    if (pairs[i * 2].IsEqual(key))
                    {
                        value = pairs[(i * 2) + 1].GetString()!;
                        return true;
                    }
                }
                return false;
            }
        }

        internal sealed class StreamGroupInfoProcessor : InterleavedStreamInfoProcessorBase<StreamGroupInfo>
        {
            protected override StreamGroupInfo ParseItem(in RawResult result)
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

                var arr = result.GetItems();
                string? name = default, lastDeliveredId = default;
                int consumerCount = default, pendingMessageCount = default;
                long entriesRead = default, lag = default;

                KeyValuePairParser.TryRead(arr, KeyValuePairParser.Name, ref name);
                KeyValuePairParser.TryRead(arr, KeyValuePairParser.Consumers, ref consumerCount);
                KeyValuePairParser.TryRead(arr, KeyValuePairParser.Pending, ref pendingMessageCount);
                KeyValuePairParser.TryRead(arr, KeyValuePairParser.LastDeliveredId, ref lastDeliveredId);
                KeyValuePairParser.TryRead(arr, KeyValuePairParser.EntriesRead, ref entriesRead);
                KeyValuePairParser.TryRead(arr, KeyValuePairParser.Lag, ref lag);

                return new StreamGroupInfo(name!, consumerCount, pendingMessageCount, lastDeliveredId, entriesRead, lag);
            }
        }

        internal abstract class InterleavedStreamInfoProcessorBase<T> : ResultProcessor<T[]>
        {
            protected abstract T ParseItem(in RawResult result);

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeArray != ResultType.Array)
                {
                    return false;
                }

                var arr = result.GetItems();
                var parsedItems = arr.ToArray((in RawResult item, in InterleavedStreamInfoProcessorBase<T> obj) => obj.ParseItem(item), this);

                SetResult(message, parsedItems);
                return true;
            }
        }

        internal sealed class StreamInfoProcessor : StreamProcessorBase<StreamInfo>
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
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeArray != ResultType.Array)
                {
                    return false;
                }

                var arr = result.GetItems();
                var max = arr.Length / 2;

                long length = -1, radixTreeKeys = -1, radixTreeNodes = -1, groups = -1;
                var lastGeneratedId = Redis.RedisValue.Null;
                StreamEntry firstEntry = StreamEntry.Null, lastEntry = StreamEntry.Null;
                var iter = arr.GetEnumerator();
                for(int i = 0; i < max; i++)
                {
                    ref RawResult key = ref iter.GetNext(), value = ref iter.GetNext();
                    if (key.Payload.Length > CommandBytes.MaxLength) continue;

                    var keyBytes = new CommandBytes(key.Payload);
                    if(keyBytes.Equals(CommonReplies.length))
                    {
                        if (!value.TryGetInt64(out length)) return false;
                    }
                    else if (keyBytes.Equals(CommonReplies.radixTreeKeys))
                    {
                        if (!value.TryGetInt64(out radixTreeKeys)) return false;
                    }
                    else if (keyBytes.Equals(CommonReplies.radixTreeNodes))
                    {
                        if (!value.TryGetInt64(out radixTreeNodes)) return false;
                    }
                    else if (keyBytes.Equals(CommonReplies.groups))
                    {
                        if (!value.TryGetInt64(out groups)) return false;
                    }
                    else if (keyBytes.Equals(CommonReplies.lastGeneratedId))
                    {
                        lastGeneratedId = value.AsRedisValue();
                    }
                    else if (keyBytes.Equals(CommonReplies.firstEntry))
                    {
                        firstEntry = ParseRedisStreamEntry(value);
                    }
                    else if (keyBytes.Equals(CommonReplies.lastEntry))
                    {
                        lastEntry = ParseRedisStreamEntry(value);
                    }
                }

                var streamInfo = new StreamInfo(
                    length: checked((int)length),
                    radixTreeKeys: checked((int)radixTreeKeys),
                    radixTreeNodes: checked((int)radixTreeNodes),
                    groups: checked((int)groups),
                    firstEntry: firstEntry,
                    lastEntry: lastEntry,
                    lastGeneratedId: lastGeneratedId);

                SetResult(message, streamInfo);
                return true;
            }
        }

        internal sealed class StreamPendingInfoProcessor : ResultProcessor<StreamPendingInfo>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
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

                if (result.Resp2TypeArray != ResultType.Array)
                {
                    return false;
                }

                var arr = result.GetItems();

                if (arr.Length != 4)
                {
                    return false;
                }

                StreamConsumer[]? consumers = null;

                // If there are no consumers as of yet for the given group, the last
                // item in the response array will be null.
                ref RawResult third = ref arr[3];
                if (!third.IsNull)
                {
                    consumers = third.ToArray((in RawResult item) =>
                    {
                        var details = item.GetItems();
                        return new StreamConsumer(
                            name: details[0].AsRedisValue(),
                            pendingMessageCount: (int)details[1].AsRedisValue());
                    });
                }

                var pendingInfo = new StreamPendingInfo(pendingMessageCount: (int)arr[0].AsRedisValue(),
                    lowestId: arr[1].AsRedisValue(),
                    highestId: arr[2].AsRedisValue(),
                    consumers: consumers ?? Array.Empty<StreamConsumer>());
                    // ^^^^^
                    // Should we bother allocating an empty array only to prevent the need for a null check?

                SetResult(message, pendingInfo);
                return true;
            }
        }

        internal sealed class StreamPendingMessagesProcessor : ResultProcessor<StreamPendingMessageInfo[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Resp2TypeArray != ResultType.Array)
                {
                    return false;
                }

                var messageInfoArray = result.GetItems().ToArray((in RawResult item) =>
                {
                    var details = item.GetItems().GetEnumerator();

                    return new StreamPendingMessageInfo(messageId: details.GetNext().AsRedisValue(),
                        consumerName: details.GetNext().AsRedisValue(),
                        idleTimeInMs: (long)details.GetNext().AsRedisValue(),
                        deliveryCount: (int)details.GetNext().AsRedisValue());
                });

                SetResult(message, messageInfoArray);
                return true;
            }
        }

        internal class StreamNameValueEntryProcessor : ValuePairInterleavedProcessorBase<NameValueEntry>
        {
            public static readonly StreamNameValueEntryProcessor Instance = new();
            private StreamNameValueEntryProcessor() { }
            protected override NameValueEntry Parse(in RawResult first, in RawResult second, object? state)
                => new NameValueEntry(first.AsRedisValue(), second.AsRedisValue());
        }

        /// <summary>
        /// Handles stream responses. For formats, see <see href="https://redis.io/topics/streams-intro"/>.
        /// </summary>
        internal abstract class StreamProcessorBase<T> : ResultProcessor<T>
        {
            protected static StreamEntry ParseRedisStreamEntry(in RawResult item)
            {
                if (item.IsNull || item.Resp2TypeArray != ResultType.Array)
                {
                    return StreamEntry.Null;
                }
                // Process the Multibulk array for each entry. The entry contains the following elements:
                //  [0] = SimpleString (the ID of the stream entry)
                //  [1] = Multibulk array of the name/value pairs of the stream entry's data
                var entryDetails = item.GetItems();

                return new StreamEntry(id: entryDetails[0].AsRedisValue(),
                    values: ParseStreamEntryValues(entryDetails[1]));
            }
            protected internal StreamEntry[] ParseRedisStreamEntries(in RawResult result) =>
                result.GetItems().ToArray((in RawResult item, in StreamProcessorBase<T> _) => ParseRedisStreamEntry(item), this);

            protected static NameValueEntry[] ParseStreamEntryValues(in RawResult result)
            {
                // The XRANGE, XREVRANGE, XREAD commands return stream entries
                // in the following format.  The name/value pairs are interleaved
                // in the same fashion as the HGETALL response.
                //
                // 1) 1) 1518951480106-0
                //    2) 1) "sensor-id"
                //       2) "1234"
                //       3) "temperature"
                //       4) "19.8"
                // 2) 1) 1518951482479-0
                //    2) 1) "sensor-id"
                //       2) "9999"
                //       3) "temperature"
                //       4) "18.2"

                if (result.Resp2TypeArray != ResultType.Array || result.IsNull)
                {
                    return Array.Empty<NameValueEntry>();
                }
                return StreamNameValueEntryProcessor.Instance.ParseArray(result, false, out _, null)!; // ! because we checked null above
            }
        }

        private sealed class StringPairInterleavedProcessor : ValuePairInterleavedProcessorBase<KeyValuePair<string, string>>
        {
            protected override KeyValuePair<string, string> Parse(in RawResult first, in RawResult second, object? state) =>
                new KeyValuePair<string, string>(first.GetString()!, second.GetString()!);
        }

        private sealed class StringProcessor : ResultProcessor<string?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        SetResult(message, result.GetString());
                        return true;
                    case ResultType.Array:
                        var arr = result.GetItems();
                        if (arr.Length == 1)
                        {
                            SetResult(message, arr[0].GetString());
                            return true;
                        }
                        break;
                }
                return false;
            }
        }

        private sealed class TieBreakerProcessor : ResultProcessor<string?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        var tieBreaker = result.GetString()!;
                        SetResult(message, tieBreaker);

                        try
                        {
                            if (connection.BridgeCouldBeNull?.ServerEndPoint is ServerEndPoint endpoint)
                            {
                                endpoint.TieBreakerResult = tieBreaker;
                            }
                        }
                        catch { }

                        return true;
                }
                return false;
            }
        }

        private class TracerProcessor : ResultProcessor<bool>
        {
            private readonly bool establishConnection;

            public TracerProcessor(bool establishConnection)
            {
                this.establishConnection = establishConnection;
            }

            public override bool SetResult(PhysicalConnection connection, Message message, in RawResult result)
            {
                connection.BridgeCouldBeNull?.Multiplexer.OnInfoMessage($"got '{result}' for '{message.CommandAndKey}' on '{connection}'");
                var final = base.SetResult(connection, message, result);
                if (result.IsError)
                {
                    if (result.StartsWith(CommonReplies.authFail_trimmed) || result.StartsWith(CommonReplies.NOAUTH))
                    {
                        connection.RecordConnectionFailed(ConnectionFailureType.AuthenticationFailure, new Exception(result.ToString() + " Verify if the Redis password provided is correct. Attempted command: " + message.Command));
                    }
                    else if (result.StartsWith(CommonReplies.loading))
                    {
                        connection.RecordConnectionFailed(ConnectionFailureType.Loading);
                    }
                    else
                    {
                        connection.RecordConnectionFailed(ConnectionFailureType.ProtocolFailure, new RedisServerException(result.ToString()));
                    }
                }

                if (connection.Protocol is null)
                {
                    // if we didn't get a valid response from HELLO, then we have to assume RESP2 at some point
                    connection.SetProtocol(RedisProtocol.Resp2);
                }

                return final;
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0071:Simplify interpolation", Justification = "Allocations (string.Concat vs. string.Format)")]
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                bool happy;
                switch (message.Command)
                {
                    case RedisCommand.ECHO:
                        happy = result.Resp2TypeBulkString == ResultType.BulkString && (!establishConnection || result.IsEqual(connection.BridgeCouldBeNull?.Multiplexer?.UniqueId));
                        break;
                    case RedisCommand.PING:
                        // there are two different PINGs; "interactive" is a +PONG or +{your message},
                        // but subscriber returns a bulk-array of [ "pong", {your message} ]
                        switch (result.Resp2TypeArray)
                        {
                            case ResultType.SimpleString:
                                happy = result.IsEqual(CommonReplies.PONG);
                                break;
                            case ResultType.Array when result.ItemsCount == 2:
                                var items = result.GetItems();
                                happy = items[0].IsEqual(CommonReplies.PONG) && items[1].Payload.IsEmpty;
                                break;
                            default:
                                happy = false;
                                break;
                        }
                        break;
                    case RedisCommand.TIME:
                        happy = result.Resp2TypeArray == ResultType.Array && result.ItemsCount == 2;
                        break;
                    case RedisCommand.EXISTS:
                        happy = result.Resp2TypeBulkString == ResultType.Integer;
                        break;
                    default:
                        happy = false;
                        break;
                }
                if (happy)
                {
                    if (establishConnection)
                    {
                        // This is what ultimately brings us to complete a connection, by advancing the state forward from a successful tracer after connection.
                        connection.BridgeCouldBeNull?.OnFullyEstablished(connection, $"From command: {message.Command}");
                    }
                    SetResult(message, happy);
                    return true;
                }
                else
                {
                    connection.RecordConnectionFailed(ConnectionFailureType.ProtocolFailure,
                        new InvalidOperationException($"unexpected tracer reply to {message.Command}: {result.ToString()}"));
                    return false;
                }
            }
        }

        private sealed class SentinelGetPrimaryAddressByNameProcessor : ResultProcessor<EndPoint?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        var items = result.GetItems();
                        if (result.IsNull)
                        {
                            return true;
                        }
                        else if (items.Length == 2 && items[1].TryGetInt64(out var port))
                        {
                            SetResult(message, Format.ParseEndPoint(items[0].GetString()!, checked((int)port)));
                            return true;
                        }
                        else if (items.Length == 0)
                        {
                            SetResult(message, null);
                            return true;
                        }
                        break;
                }
                return false;
            }
        }

        private sealed class SentinelGetSentinelAddressesProcessor : ResultProcessor<EndPoint[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                List<EndPoint> endPoints = new List<EndPoint>();

                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        foreach (RawResult item in result.GetItems())
                        {
                            var pairs = item.GetItems();
                            string? ip = null;
                            int port = default;
                            if (KeyValuePairParser.TryRead(pairs, in KeyValuePairParser.IP, ref ip)
                                && KeyValuePairParser.TryRead(pairs, in KeyValuePairParser.Port, ref port))
                            {
                                endPoints.Add(Format.ParseEndPoint(ip, port));
                            }
                        }
                        SetResult(message, endPoints.ToArray());
                        return true;

                    case ResultType.SimpleString:
                        // We don't want to blow up if the primary is not found
                        if (result.IsNull)
                            return true;
                        break;
                }

                return false;
            }
        }

        private sealed class SentinelGetReplicaAddressesProcessor : ResultProcessor<EndPoint[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                List<EndPoint> endPoints = new List<EndPoint>();

                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        foreach (RawResult item in result.GetItems())
                        {
                            var pairs = item.GetItems();
                            string? ip = null;
                            int port = default;
                            if (KeyValuePairParser.TryRead(pairs, in KeyValuePairParser.IP, ref ip)
                                && KeyValuePairParser.TryRead(pairs, in KeyValuePairParser.Port, ref port))
                            {
                                endPoints.Add(Format.ParseEndPoint(ip, port));
                            }
                        }
                        break;

                    case ResultType.SimpleString:
                        // We don't want to blow up if the primary is not found
                        if (result.IsNull)
                            return true;
                        break;
                }

                if (endPoints.Count > 0)
                {
                    SetResult(message, endPoints.ToArray());
                    return true;
                }

                return false;
            }
        }

        private sealed class SentinelArrayOfArraysProcessor : ResultProcessor<KeyValuePair<string, string>[][]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (StringPairInterleaved is not StringPairInterleavedProcessor innerProcessor)
                {
                    return false;
                }

                switch (result.Resp2TypeArray)
                {
                    case ResultType.Array:
                        var arrayOfArrays = result.GetItems();

                        var returnArray = result.ToArray<KeyValuePair<string, string>[], StringPairInterleavedProcessor>(
                            (in RawResult rawInnerArray, in StringPairInterleavedProcessor proc) =>
                            {
                                if (proc.TryParse(rawInnerArray, out KeyValuePair<string, string>[]? kvpArray))
                                {
                                    return kvpArray!;
                                }
                                else
                                {
                                    throw new ArgumentOutOfRangeException(nameof(rawInnerArray), $"Error processing {message.CommandAndKey}, could not decode array '{rawInnerArray}'");
                                }
                            }, innerProcessor)!;

                        SetResult(message, returnArray);
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
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            switch(result.Resp2TypeArray)
            {
                case ResultType.Array:
                    var items = result.GetItems();
                    T[] arr;
                    if (items.IsEmpty)
                    {
                        arr = Array.Empty<T>();
                    }
                    else
                    {
                        arr = new T[checked((int)items.Length)];
                        int index = 0;
                        foreach (ref RawResult inner in items)
                        {
                            if (!TryParse(inner, out arr[index++]))
                                return false;
                        }
                    }
                    SetResult(message, arr);
                    return true;
                default:
                    return false;
            }
        }

        protected abstract bool TryParse(in RawResult raw, out T parsed);
    }
}
