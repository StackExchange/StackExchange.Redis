using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pipelines.Sockets.Unofficial.Arenas;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis
{
    internal abstract partial class ResultProcessor
    {
        public static readonly ResultProcessor<bool>
            Boolean = new BooleanProcessor(),
            DemandOK = new ExpectBasicStringProcessor(Literals.OK.Hash),
            DemandPONG = new ExpectBasicStringProcessor(Literals.PONG.Hash),
            DemandZeroOrOne = new DemandZeroOrOneProcessor(),
            AutoConfigure = new AutoConfigureProcessor(),
            TrackSubscriptions = new TrackSubscriptionsProcessor(null),
            Tracer = new TracerProcessor(false),
            EstablishConnection = new TracerProcessor(true),
            BackgroundSaveStarted = new ExpectBasicStringProcessor(Literals.background_saving_started.Hash, startsWith: true),
            BackgroundSaveAOFStarted = new ExpectBasicStringProcessor(Literals.background_aof_rewriting_started.Hash, startsWith: true);

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
        public virtual bool SetResult(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            reader.MovePastBof();
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
                return HandleCommonError(message, reader, bridge);
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

        private bool HandleCommonError(Message message, RespReader reader, PhysicalBridge? bridge)
        {
            if (reader.StartsWith(Literals.NOAUTH.U8))
            {
                bridge?.Multiplexer.SetAuthSuspect(new RedisServerException("NOAUTH Returned - connection has not yet authenticated"));
            }
            else if (reader.StartsWith(Literals.WRONGPASS.U8))
            {
                bridge?.Multiplexer.SetAuthSuspect(new RedisServerException(reader.GetOverview()));
            }

            var server = bridge?.ServerEndPoint;
            bool log = !message.IsInternalCall;
            bool isMoved = reader.StartsWith(Literals.MOVED.U8);
            bool wasNoRedirect = (message.Flags & CommandFlags.NoRedirect) != 0;
            string? err = string.Empty;
            bool unableToConnectError = false;
            if (isMoved || reader.StartsWith(Literals.ASK.U8))
            {
                message.SetResponseReceived();

                log = false;
                string[] parts = reader.ReadString()!.Split(StringSplits.Space, 3);
                if (Format.TryParseInt32(parts[1], out int hashSlot)
                    && Format.TryParseEndPoint(parts[2], out var endpoint))
                {
                    // no point sending back to same server, and no point sending to a dead server
                    if (!Equals(server?.EndPoint, endpoint))
                    {
                        if (bridge is null)
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
                ServerFail(message, err);
            }

            return true;
        }

        protected virtual bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            // spoof the old API from the new API; this is a transitional step only, and is inefficient
            var rawResult = AsRaw(ref reader, connection.Protocol is RedisProtocol.Resp3);
            return SetResultCore(connection, message, rawResult);
        }

        private static RawResult AsRaw(ref RespReader reader, bool resp3)
        {
            var flags = RawResult.ResultFlags.HasValue;
            if (!reader.IsNull) flags |= RawResult.ResultFlags.NonNull;
            if (resp3) flags |= RawResult.ResultFlags.Resp3;
            var type = reader.Prefix.ToResultType();
            if (reader.IsAggregate)
            {
                var arr = reader.ReadPastArray(
                    ref resp3,
                    static (ref resp3, ref reader) => AsRaw(ref reader, resp3),
                    scalar: false) ?? [];
                return new RawResult(type, new Sequence<RawResult>(arr), flags);
            }

            if (reader.IsScalar)
            {
                ReadOnlySequence<byte> blob = new(reader.ReadByteArray() ?? []);
                return new RawResult(type, blob, flags);
            }

            return default;
        }

        // temp hack so we can compile; this should be removed
        protected virtual bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
            => throw new NotImplementedException(GetType().Name + "." + nameof(SetResultCore));

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

            public bool TryParse(in RawResult result, out TimeSpan? expiry)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                        if (result.TryGetInt64(out long time))
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
            public static bool TryGet(in RawResult result, out bool value)
            {
                switch (result.Resp2TypeBulkString)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        if (result.IsEqual(CommonReplies.one))
                        {
                            value = true;
                            return true;
                        }
                        else if (result.IsEqual(CommonReplies.zero))
                        {
                            value = false;
                            return true;
                        }
                        break;
                }
                value = false;
                return false;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (reader.IsScalar && reader.ScalarLengthIs(1))
                {
                    var span = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(stackalloc byte[1]);
                    var value = span[0];
                    if (value == (byte)'1')
                    {
                        SetResult(message, true);
                        return true;
                    }
                    else if (value == (byte)'0')
                    {
                        SetResult(message, false);
                        return true;
                    }
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
                    var asciiHash = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(stackalloc byte[Sha1HashLength * 2]);

                    // External caller wants the hex bytes, not the ASCII bytes
                    // For nullability/consistency reasons, we always do the parse here.
                    byte[] hash;
                    try
                    {
                        hash = ParseSHA1(asciiHash.ToArray());
                    }
                    catch (ArgumentException)
                    {
                        return false; // Invalid hex characters
                    }

                    if (message is RedisDatabase.ScriptLoadMessage sl)
                    {
                        connection.BridgeCouldBeNull?.ServerEndPoint?.AddScript(sl.Script, hash);
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
                // Handle array with at least 2 elements: [element, score, ...], or null/empty array
                if (reader.IsAggregate)
                {
                    SortedSetEntry? result = null;

                    // Note: null arrays report false for TryMoveNext, so no explicit null check needed
                    if (reader.TryMoveNext() && reader.IsScalar)
                    {
                        var element = reader.ReadRedisValue();
                        if (reader.TryMoveNext() && reader.IsScalar)
                        {
                            var score = reader.TryReadDouble(out var val) ? val : double.NaN;
                            result = new SortedSetEntry(element, score);
                        }
                    }

                    SetResult(message, result);
                    return true;
                }

                return false;
            }
        }

        internal sealed class SortedSetEntryArrayProcessor : ValuePairInterleavedProcessorBase<SortedSetEntry>
        {
            protected override SortedSetEntry Parse(ref RespReader first, ref RespReader second, object? state) =>
                new SortedSetEntry(first.ReadRedisValue(), second.TryReadDouble(out double val) ? val : double.NaN);

            protected override SortedSetEntry Parse(in RawResult first, in RawResult second, object? state) =>
                new SortedSetEntry(first.AsRedisValue(), second.TryGetDouble(out double val) ? val : double.NaN);
        }

        internal sealed class SortedSetPopResultProcessor : ResultProcessor<SortedSetPopResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // Handle array of 2: [key, array of SortedSetEntry] or null aggregate
                if (reader.IsAggregate)
                {
                    // Handle null (RESP3 pure null or RESP2 null array)
                    if (reader.IsNull)
                    {
                        SetResult(message, Redis.SortedSetPopResult.Null);
                        return true;
                    }

                    if (reader.TryMoveNext() && reader.IsScalar)
                    {
                        var key = reader.ReadRedisKey();

                        // Read the second element (array of SortedSetEntry)
                        if (reader.TryMoveNext() && reader.IsAggregate)
                        {
                            var entries = reader.ReadPastArray(
                                static (ref r) =>
                                {
                                    // Each entry is an array of 2: [element, score]
                                    if (r.IsAggregate && r.TryMoveNext() && r.IsScalar)
                                    {
                                        var element = r.ReadRedisValue();
                                        if (r.TryMoveNext() && r.IsScalar)
                                        {
                                            var score = r.TryReadDouble(out var val) ? val : double.NaN;
                                            return new SortedSetEntry(element, score);
                                        }
                                    }
                                    return default;
                                },
                                scalar: false);

                            SetResult(message, new SortedSetPopResult(key, entries!));
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        internal sealed class ListPopResultProcessor : ResultProcessor<ListPopResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // Handle array of 2: [key, array of values] or null aggregate
                if (reader.IsAggregate)
                {
                    // Handle null (RESP3 pure null or RESP2 null array)
                    if (reader.IsNull)
                    {
                        SetResult(message, Redis.ListPopResult.Null);
                        return true;
                    }

                    if (reader.TryMoveNext() && reader.IsScalar)
                    {
                        var key = reader.ReadRedisKey();

                        // Read the second element (array of RedisValue)
                        if (reader.TryMoveNext() && reader.IsAggregate)
                        {
                            var values = reader.ReadPastRedisValues();
                            SetResult(message, new ListPopResult(key, values!));
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        internal sealed class HashEntryArrayProcessor : ValuePairInterleavedProcessorBase<HashEntry>
        {
            protected override HashEntry Parse(ref RespReader first, ref RespReader second, object? state) =>
                new HashEntry(first.ReadRedisValue(), second.ReadRedisValue());

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

            private static bool IsAllJaggedPairsReader(in RespReader reader)
            {
                // Check whether each child element is an array of exactly length 2
                // Use AggregateChildren to create isolated child iterators without mutating the reader
                var iter = reader.AggregateChildren();
                while (iter.MoveNext())
                {
                    // Check if this child is an array with exactly 2 elements
                    if (!(iter.Value.IsAggregate && iter.Value.AggregateLengthIs(2)))
                    {
                        return false;
                    }
                }
                return true;
            }

            public T[]? ParseArray(ref RespReader reader, RedisProtocol protocol, bool allowOversized, out int count, object? state)
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

                // Check if we have jagged pairs (RESP3 style) or interleaved (RESP2 style)
                bool isJagged = protocol == RedisProtocol.Resp3 && AllowJaggedPairs && IsAllJaggedPairsReader(reader);

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

            // Old RawResult API - kept for backwards compatibility with code not yet migrated
            protected abstract T Parse(in RawResult first, in RawResult second, object? state);

            public T[]? ParseArray(in RawResult result, bool allowOversized, out int count, object? state)
            {
                if (result.IsNull)
                {
                    count = 0;
                    return null;
                }

                var items = result.GetItems();
                count = checked((int)items.Length);
                if (count == 0)
                {
                    return [];
                }

                // Check if we have jagged pairs (RESP3 style) or interleaved (RESP2 style)
                bool isJagged = result.Resp3Type == ResultType.Array && AllowJaggedPairs && IsAllJaggedPairs(result);

                if (isJagged)
                {
                    // Jagged format: [[k1, v1], [k2, v2], ...]
                    var pairs = allowOversized ? ArrayPool<T>.Shared.Rent(count) : new T[count];
                    for (int i = 0; i < count; i++)
                    {
                        ref readonly RawResult pair = ref items[i];
                        var pairItems = pair.GetItems();
                        pairs[i] = Parse(pairItems[0], pairItems[1], state);
                    }
                    return pairs;
                }
                else
                {
                    // Interleaved format: [k1, v1, k2, v2, ...]
                    count >>= 1; // divide by 2
                    var pairs = allowOversized ? ArrayPool<T>.Shared.Rent(count) : new T[count];
                    for (int i = 0; i < count; i++)
                    {
                        pairs[i] = Parse(items[(i * 2) + 0], items[(i * 2) + 1], state);
                    }
                    return pairs;
                }
            }

            private static bool IsAllJaggedPairs(in RawResult result)
            {
                var items = result.GetItems();
                foreach (ref readonly var item in items)
                {
                    if (item.Resp2TypeArray != ResultType.Array || item.GetItems().Length != 2)
                    {
                        return false;
                    }
                }
                return true;
            }

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

            public override bool SetResult(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                var copy = reader;
                reader.MovePastBof();
                if (reader.IsError && reader.StartsWith(Literals.READONLY.U8))
                {
                    var bridge = connection.BridgeCouldBeNull;
                    if (bridge != null)
                    {
                        var server = bridge.ServerEndPoint;
                        Log?.LogInformationAutoConfiguredRoleReplica(new(server));
                        server.IsReplica = true;
                    }
                }

                return base.SetResult(connection, message, ref copy);
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
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
                                Log?.LogInformationAutoConfiguredClientConnectionId(new(server), clientId);

                                SetResult(message, true);
                                return true;
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
                                            Log?.LogInformationAutoConfiguredInfoRole(new(server), isReplica ? "replica" : "primary");
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
                                            Log?.LogInformationAutoConfiguredInfoVersion(new(server), version);
                                        }
                                    }
                                    else if ((val = Extract(line, "redis_mode:")) != null)
                                    {
                                        if (TryParseServerType(val, out var serverType))
                                        {
                                            server.ServerType = serverType;
                                            Log?.LogInformationAutoConfiguredInfoServerType(new(server), serverType);
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
                            Log?.LogInformationAutoConfiguredSentinelServerType(new(server));
                        }
                        SetResult(message, true);
                        return true;
                    case ResultType.Array:
                        if (message?.Command == RedisCommand.CONFIG)
                        {
                            var iter = result.GetItems().GetEnumerator();
                            while (iter.MoveNext())
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
                                        Log?.LogInformationAutoConfiguredConfigTimeout(new(server), targetSeconds);
                                        server.WriteEverySeconds = targetSeconds;
                                    }
                                }
                                else if (key.IsEqual(CommonReplies.databases) && val.TryGetInt64(out i64))
                                {
                                    int dbCount = checked((int)i64);
                                    Log?.LogInformationAutoConfiguredConfigDatabases(new(server), dbCount);
                                    server.Databases = dbCount;
                                    if (dbCount > 1)
                                    {
                                        connection.MultiDatabasesOverride = true;
                                    }
                                }
                                else if (key.IsEqual(CommonReplies.slave_read_only) || key.IsEqual(CommonReplies.replica_read_only))
                                {
                                    if (val.IsEqual(CommonReplies.yes))
                                    {
                                        server.ReplicaReadOnly = true;
                                        Log?.LogInformationAutoConfiguredConfigReadOnlyReplica(new(server), true);
                                    }
                                    else if (val.IsEqual(CommonReplies.no))
                                    {
                                        server.ReplicaReadOnly = false;
                                        Log?.LogInformationAutoConfiguredConfigReadOnlyReplica(new(server), false);
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
                                    Log?.LogInformationAutoConfiguredHelloServerVersion(new(server), version);
                                }
                                else if (key.IsEqual(CommonReplies.proto) && val.TryGetInt64(out var i64))
                                {
                                    connection.SetProtocol(i64 >= 3 ? RedisProtocol.Resp3 : RedisProtocol.Resp2);
                                    Log?.LogInformationAutoConfiguredHelloProtocol(new(server), connection.Protocol ?? RedisProtocol.Resp2);
                                }
                                else if (key.IsEqual(CommonReplies.id) && val.TryGetInt64(out i64))
                                {
                                    connection.ConnectionId = i64;
                                    Log?.LogInformationAutoConfiguredHelloConnectionId(new(server), i64);
                                }
                                else if (key.IsEqual(CommonReplies.mode) && TryParseServerType(val.GetString(), out var serverType))
                                {
                                    server.ServerType = serverType;
                                    Log?.LogInformationAutoConfiguredHelloServerType(new(server), serverType);
                                }
                                else if (key.IsEqual(CommonReplies.role) && TryParseRole(val.GetString(), out bool isReplica))
                                {
                                    server.IsReplica = isReplica;
                                    Log?.LogInformationAutoConfiguredHelloRole(new(server), isReplica ? "replica" : "primary");
                                }
                            }
                        }
                        else if (message?.Command == RedisCommand.SENTINEL)
                        {
                            server.ServerType = ServerType.Sentinel;
                            Log?.LogInformationAutoConfiguredSentinelServerType(new(server));
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

        private sealed class ExpectBasicStringProcessor : ResultProcessor<bool>
        {
            private readonly FastHash _expected;
            private readonly bool _startsWith;

            public ExpectBasicStringProcessor(in FastHash expected, bool startsWith = false)
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

                var bytes = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(stackalloc byte[expectedLength]);
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

#pragma warning disable SA1300, SA1134
        // ReSharper disable InconsistentNaming
        [FastHash("string")] private static partial class redistype_string { }
        [FastHash("list")] private static partial class redistype_list { }
        [FastHash("set")] private static partial class redistype_set { }
        [FastHash("zset")] private static partial class redistype_zset { }
        [FastHash("hash")] private static partial class redistype_hash { }
        [FastHash("stream")] private static partial class redistype_stream { }
        [FastHash("vectorset")] private static partial class redistype_vectorset { }
        // ReSharper restore InconsistentNaming
#pragma warning restore SA1300, SA1134

        private sealed class RedisTypeProcessor : ResultProcessor<RedisType>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                static RedisType FastParse(ReadOnlySpan<byte> span)
                {
                    if (span.IsEmpty) return Redis.RedisType.None; // includes null
                    var hash = FastHash.HashCS(span);
                    return hash switch
                    {
                        redistype_string.HashCS when redistype_string.IsCS(hash, span) => Redis.RedisType.String,
                        redistype_list.HashCS when redistype_list.IsCS(hash, span) => Redis.RedisType.List,
                        redistype_set.HashCS when redistype_set.IsCS(hash, span) => Redis.RedisType.Set,
                        redistype_zset.HashCS when redistype_zset.IsCS(hash, span) => Redis.RedisType.SortedSet,
                        redistype_hash.HashCS when redistype_hash.IsCS(hash, span) => Redis.RedisType.Hash,
                        redistype_stream.HashCS when redistype_stream.IsCS(hash, span) => Redis.RedisType.Stream,
                        redistype_vectorset.HashCS when redistype_vectorset.IsCS(hash, span) => Redis.RedisType.VectorSet,
                        _ => Redis.RedisType.Unknown,
                    };
                }
                if (reader.IsScalar)
                {
                    const int MAX_STACK = 16;
                    Debug.Assert(reader.ScalarLength() <= MAX_STACK); // we don't expect anything huge here
                    var span = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(stackalloc byte[MAX_STACK]);
                    var value = FastParse(span);

                    SetResult(message, value);
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
                    var arr = reader.ReadPastRedisValues()!;
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

        private static GeoPosition? ParseGeoPosition(ref RespReader reader)
        {
            if (reader.IsAggregate && reader.AggregateLengthIs(2)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadDouble(out var longitude)
                && reader.TryMoveNext() && reader.IsScalar && reader.TryReadDouble(out var latitude)
                && !reader.TryMoveNext())
            {
                return new GeoPosition(longitude, latitude);
            }
            return null;
        }

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

            private static GeoRadiusResult Parse(ref RespReader reader, GeoRadiusOptions options)
            {
                if (options == GeoRadiusOptions.None)
                {
                    // Without any WITH option specified, the command just returns a linear array like ["New York","Milan","Paris"].
                    return new GeoRadiusResult(reader.ReadString(), null, null, null);
                }

                // If WITHCOORD, WITHDIST or WITHHASH options are specified, the command returns an array of arrays, where each sub-array represents a single item.
                if (!reader.IsAggregate)
                {
                    return default;
                }

                reader.MoveNext(); // Move to first element in the sub-array

                // the first item in the sub-array is always the name of the returned item.
                var member = reader.ReadString();

                /*  The other information is returned in the following order as successive elements of the sub-array.
The distance from the center as a floating point number, in the same unit specified in the radius.
The geohash integer.
The coordinates as a two items x,y array (longitude,latitude).
                 */
                double? distance = null;
                GeoPosition? position = null;
                long? hash = null;

                if ((options & GeoRadiusOptions.WithDistance) != 0)
                {
                    reader.MoveNextScalar();
                    distance = reader.ReadDouble();
                }

                if ((options & GeoRadiusOptions.WithGeoHash) != 0)
                {
                    reader.MoveNextScalar();
                    hash = reader.TryReadInt64(out var h) ? h : null;
                }

                if ((options & GeoRadiusOptions.WithCoordinates) != 0)
                {
                    reader.MoveNextAggregate();
                    position = ParseGeoPosition(ref reader);
                }

                return new GeoRadiusResult(member, distance, hash, position);
            }
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
                if (reader.IsAggregate)
                {
                    // Top-level array: ["matches", matches_array, "len", length_value]
                    // Use nominal access instead of positional
                    LCSMatchResult.LCSMatch[]? matchesArray = null;
                    long longestMatchLength = 0;

                    Span<byte> keyBuffer = stackalloc byte[16]; // Buffer for key names
                    var iter = reader.AggregateChildren();
                    while (iter.MoveNext() && iter.Value.IsScalar)
                    {
                        // Capture the scalar key
                        var keyBytes = iter.Value.TryGetSpan(out var tmp) ? tmp : iter.Value.Buffer(keyBuffer);
                        var hash = FastHash.HashCS(keyBytes);

                        if (!iter.MoveNext()) break; // out of data

                        // Use FastHash pattern to identify "matches" vs "len"
                        switch (hash)
                        {
                            case Literals.matches.HashCS when Literals.matches.IsCS(hash, keyBytes):
                                // Read the matches array
                                if (iter.Value.IsAggregate)
                                {
                                    bool failed = false;
                                    matchesArray = iter.Value.ReadPastArray(ref failed, static (ref failed, ref reader) =>
                                    {
                                        // Don't even bother if we've already failed
                                        if (!failed && reader.IsAggregate)
                                        {
                                            var matchChildren = reader.AggregateChildren();
                                            if (matchChildren.MoveNext() && TryReadPosition(ref matchChildren.Value, out var firstPos)
                                                && matchChildren.MoveNext() && TryReadPosition(ref matchChildren.Value, out var secondPos)
                                                && matchChildren.MoveNext() && matchChildren.Value.IsScalar && matchChildren.Value.TryReadInt64(out var length))
                                            {
                                                return new LCSMatchResult.LCSMatch(firstPos, secondPos, length);
                                            }
                                        }
                                        failed = true;
                                        return default;
                                    });

                                    // Check if anything went wrong
                                    if (failed) matchesArray = null;
                                }
                                break;

                            case Literals.len.HashCS when Literals.len.IsCS(hash, keyBytes):
                                // Read the length value
                                if (iter.Value.IsScalar)
                                {
                                    longestMatchLength = iter.Value.TryReadInt64(out var totalLen) ? totalLen : 0;
                                }
                                break;
                        }
                    }

                    if (matchesArray is not null)
                    {
                        SetResult(message, new LCSMatchResult(matchesArray, longestMatchLength));
                        return true;
                    }
                }
                return false;
            }

            private static bool TryReadPosition(ref RespReader reader, out LCSMatchResult.LCSPosition position)
            {
                // Expecting a 2-element array: [start, end]
                position = default;
                if (!reader.IsAggregate) return false;

                if (!(reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var start))) return false;

                if (!(reader.TryMoveNext() && reader.IsScalar && reader.TryReadInt64(out var end))) return false;

                position = new LCSMatchResult.LCSPosition(start, end);
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

                ReadOnlySpan<byte> roleBytes = reader.TryGetSpan(out var span)
                    ? span
                    : reader.Buffer(stackalloc byte[16]); // word-aligned, enough for longest role type

                var hash = FastHash.HashCS(roleBytes);
                var role = hash switch
                {
                    Literals.master.HashCS when Literals.master.IsCS(hash, roleBytes) => ParsePrimary(ref reader),
                    Literals.slave.HashCS when Literals.slave.IsCS(hash, roleBytes) => ParseReplica(ref reader, Literals.slave.Text),
                    Literals.replica.HashCS when Literals.replica.IsCS(hash, roleBytes) => ParseReplica(ref reader, Literals.replica.Text),
                    Literals.sentinel.HashCS when Literals.sentinel.IsCS(hash, roleBytes) => ParseSentinel(ref reader),
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

                ReadOnlySpan<byte> stateBytes = reader.TryGetSpan(out var span)
                    ? span
                    : reader.Buffer(stackalloc byte[16]); // word-aligned, enough for longest state

                var hash = FastHash.HashCS(stateBytes);
                var replicationState = hash switch
                {
                    Literals.connect.HashCS when Literals.connect.IsCS(hash, stateBytes) => Literals.connect.Text,
                    Literals.connecting.HashCS when Literals.connecting.IsCS(hash, stateBytes) => Literals.connecting.Text,
                    Literals.sync.HashCS when Literals.sync.IsCS(hash, stateBytes) => Literals.sync.Text,
                    Literals.connected.HashCS when Literals.connected.IsCS(hash, stateBytes) => Literals.connected.Text,
                    Literals.none.HashCS when Literals.none.IsCS(hash, stateBytes) => Literals.none.Text,
                    Literals.handshake.HashCS when Literals.handshake.IsCS(hash, stateBytes) => Literals.handshake.Text,
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
            public override bool SetResult(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                var copy = reader;
                reader.MovePastBof();
                if (reader.IsError && reader.StartsWith(Literals.NOSCRIPT.U8))
                { // scripts are not flushed individually, so assume the entire script cache is toast ("SCRIPT FLUSH")
                    connection.BridgeCouldBeNull?.ServerEndPoint?.FlushScriptCache();
                    message.SetScriptUnavailable();
                }
                // and apply usual processing for the rest
                return base.SetResult(connection, message, ref copy);
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

                    if (protocol == RedisProtocol.Resp3)
                    {
                        // RESP3: map - skip the key, read the value
                        reader.MoveNext(); // skip key
                        reader.MoveNext(); // move to value
                        entries = ParseRedisStreamEntries(ref reader, protocol);
                    }
                    else
                    {
                        // RESP2: array - first element is array with [name, entries]
                        var iter = reader.AggregateChildren();
                        iter.DemandNext(); // first stream
                        var streamIter = iter.Value.AggregateChildren();
                        streamIter.DemandNext(); // skip stream name
                        streamIter.DemandNext(); // entries array
                        entries = ParseRedisStreamEntries(ref streamIter.Value, protocol);
                    }
                }
                else
                {
                    entries = ParseRedisStreamEntries(ref reader, protocol);
                }

                SetResult(message, entries);
                return true;
            }
        }

        private readonly struct MultiStreamState(MultiStreamProcessor processor, RedisProtocol protocol)
        {
            public MultiStreamProcessor Processor { get; } = processor;
            public RedisProtocol Protocol { get; } = protocol;
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
                    streams = RedisStreamInterleavedProcessor.Instance.ParseArray(ref reader, protocol, false, out _, this)!; // null-checked
                }
                else
                {
                    var state = new MultiStreamState(this, protocol);
                    streams = reader.ReadPastArray(
                        ref state,
                        static (ref state, ref itemReader) =>
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
                            var entries = state.Processor.ParseRedisStreamEntries(ref itemReader, state.Protocol);

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
            protected override bool AllowJaggedPairs => false; // we only use this on a flattened map

            public static readonly RedisStreamInterleavedProcessor Instance = new();
            private RedisStreamInterleavedProcessor()
            {
            }

            protected override RedisStream Parse(ref RespReader first, ref RespReader second, object? state)
                => throw new NotImplementedException("RedisStreamInterleavedProcessor.Parse(ref RespReader) should not be called - MultiStreamProcessor handles this directly");

            protected override RedisStream Parse(in RawResult first, in RawResult second, object? state)
            {
                var processor = (MultiStreamProcessor)state!;
                var key = first.AsRedisKey();
                var entries = processor.ParseRedisStreamEntries(second);
                return new RedisStream(key, entries);
            }
        }

        /// <summary>
        /// This processor is for <see cref="RedisCommand.XAUTOCLAIM"/> *without* the <see cref="StreamConstants.JustId"/> option.
        /// </summary>
        internal sealed class StreamAutoClaimProcessor : StreamProcessorBase<StreamAutoClaimResult>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // See https://redis.io/commands/xautoclaim for command documentation.
                // Note that the result should never be null, so intentionally treating it as a failure to parse here
                if (reader.IsAggregate && !reader.IsNull)
                {
                    int length = reader.AggregateLength();
                    if (!(length == 2 || length == 3))
                    {
                        return false;
                    }

                    var iter = reader.AggregateChildren();
                    var protocol = connection.Protocol.GetValueOrDefault();

                    // [0] The next start ID.
                    iter.DemandNext();
                    var nextStartId = iter.Value.ReadRedisValue();

                    // [1] The array of StreamEntry's.
                    iter.DemandNext();
                    var entries = ParseRedisStreamEntries(ref iter.Value, protocol);

                    // [2] The array of message IDs deleted from the stream that were in the PEL.
                    //     This is not available in 6.2 so we need to be defensive when reading this part of the response.
                    RedisValue[] deletedIds = [];
                    if (length == 3)
                    {
                        iter.DemandNext();
                        if (iter.Value.IsAggregate && !iter.Value.IsNull)
                        {
                            deletedIds = iter.Value.ReadPastArray(
                                static (ref RespReader r) => r.ReadRedisValue(),
                                scalar: true)!;
                        }
                    }

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
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                // See https://redis.io/commands/xautoclaim for command documentation.
                // Note that the result should never be null, so intentionally treating it as a failure to parse here
                if (reader.IsAggregate && !reader.IsNull)
                {
                    int length = reader.AggregateLength();
                    if (!(length == 2 || length == 3))
                    {
                        return false;
                    }

                    var iter = reader.AggregateChildren();

                    // [0] The next start ID.
                    iter.DemandNext();
                    var nextStartId = iter.Value.ReadRedisValue();

                    // [1] The array of claimed message IDs.
                    iter.DemandNext();
                    RedisValue[] claimedIds = [];
                    if (iter.Value.IsAggregate && !iter.Value.IsNull)
                    {
                        claimedIds = iter.Value.ReadPastArray(
                            static (ref RespReader r) => r.ReadRedisValue(),
                            scalar: true)!;
                    }

                    // [2] The array of message IDs deleted from the stream that were in the PEL.
                    //     This is not available in 6.2 so we need to be defensive when reading this part of the response.
                    RedisValue[] deletedIds = [];
                    if (length == 3)
                    {
                        iter.DemandNext();
                        if (iter.Value.IsAggregate && !iter.Value.IsNull)
                        {
                            deletedIds = iter.Value.ReadPastArray(
                                static (ref RespReader r) => r.ReadRedisValue(),
                                scalar: true)!;
                        }
                    }

                    SetResult(message, new StreamAutoClaimIdsOnlyResult(nextStartId, claimedIds, deletedIds));
                    return true;
                }

                return false;
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

                Span<byte> keyBuffer = stackalloc byte[CommandBytes.MaxLength];
                while (reader.TryMoveNext() && reader.IsScalar)
                {
                    var keyBytes = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(keyBuffer);
                    if (keyBytes.Length > CommandBytes.MaxLength)
                    {
                        if (!reader.TryMoveNext()) break;
                        continue;
                    }

                    var hash = FastHash.HashCS(keyBytes);
                    if (!reader.TryMoveNext()) break;

                    switch (hash)
                    {
                        case Literals.name.HashCS when Literals.name.IsCS(hash, keyBytes):
                            name = reader.ReadString();
                            break;
                        case Literals.pending.HashCS when Literals.pending.IsCS(hash, keyBytes):
                            if (reader.TryReadInt64(out var pending))
                            {
                                pendingMessageCount = checked((int)pending);
                            }
                            break;
                        case Literals.idle.HashCS when Literals.idle.IsCS(hash, keyBytes):
                            reader.TryReadInt64(out idleTimeInMilliseconds);
                            break;
                    }
                }

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
            internal static bool TryRead(Sequence<RawResult> pairs, in CommandBytes key, ref long? value)
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
                if (TryRead(pairs, key, ref tmp))
                {
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

                Span<byte> keyBuffer = stackalloc byte[CommandBytes.MaxLength];
                while (reader.TryMoveNext() && reader.IsScalar)
                {
                    var keyBytes = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(keyBuffer);
                    if (keyBytes.Length > CommandBytes.MaxLength)
                    {
                        if (!reader.TryMoveNext()) break;
                        continue;
                    }

                    var hash = FastHash.HashCS(keyBytes);
                    if (!reader.TryMoveNext()) break;

                    switch (hash)
                    {
                        case Literals.name.HashCS when Literals.name.IsCS(hash, keyBytes):
                            name = reader.ReadString();
                            break;
                        case Literals.consumers.HashCS when Literals.consumers.IsCS(hash, keyBytes):
                            if (reader.TryReadInt64(out var consumers))
                            {
                                consumerCount = checked((int)consumers);
                            }
                            break;
                        case Literals.pending.HashCS when Literals.pending.IsCS(hash, keyBytes):
                            if (reader.TryReadInt64(out var pending))
                            {
                                pendingMessageCount = checked((int)pending);
                            }
                            break;
                        case Literals.last_delivered_id.HashCS when Literals.last_delivered_id.IsCS(hash, keyBytes):
                            lastDeliveredId = reader.ReadString();
                            break;
                        case Literals.entries_read.HashCS when Literals.entries_read.IsCS(hash, keyBytes):
                            reader.TryReadInt64(out entriesRead);
                            break;
                        case Literals.lag.HashCS when Literals.lag.IsCS(hash, keyBytes):
                            if (reader.TryReadInt64(out var lagValue))
                            {
                                lag = lagValue;
                            }
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
                Span<byte> keyBuffer = stackalloc byte[CommandBytes.MaxLength];

                while (reader.TryMoveNext() && reader.IsScalar)
                {
                    var keyBytes = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(keyBuffer);
                    if (keyBytes.Length > CommandBytes.MaxLength)
                    {
                        // Skip this key-value pair
                        if (!reader.TryMoveNext()) break;
                        continue;
                    }

                    var hash = FastHash.HashCS(keyBytes);

                    // Move to value
                    if (!reader.TryMoveNext()) break;

                    switch (hash)
                    {
                        case Literals.length.HashCS when Literals.length.IsCS(hash, keyBytes):
                            if (!reader.TryReadInt64(out length)) return false;
                            break;
                        case Literals.radix_tree_keys.HashCS when Literals.radix_tree_keys.IsCS(hash, keyBytes):
                            if (!reader.TryReadInt64(out radixTreeKeys)) return false;
                            break;
                        case Literals.radix_tree_nodes.HashCS when Literals.radix_tree_nodes.IsCS(hash, keyBytes):
                            if (!reader.TryReadInt64(out radixTreeNodes)) return false;
                            break;
                        case Literals.groups.HashCS when Literals.groups.IsCS(hash, keyBytes):
                            if (!reader.TryReadInt64(out groups)) return false;
                            break;
                        case Literals.last_generated_id.HashCS when Literals.last_generated_id.IsCS(hash, keyBytes):
                            lastGeneratedId = reader.ReadRedisValue();
                            break;
                        case Literals.first_entry.HashCS when Literals.first_entry.IsCS(hash, keyBytes):
                            firstEntry = ParseRedisStreamEntry(ref reader, protocol);
                            break;
                        case Literals.last_entry.HashCS when Literals.last_entry.IsCS(hash, keyBytes):
                            lastEntry = ParseRedisStreamEntry(ref reader, protocol);
                            break;
                        // 7.0
                        case Literals.max_deleted_entry_id.HashCS when Literals.max_deleted_entry_id.IsCS(hash, keyBytes):
                            maxDeletedEntryId = reader.ReadRedisValue();
                            break;
                        case Literals.recorded_first_entry_id.HashCS when Literals.recorded_first_entry_id.IsCS(hash, keyBytes):
                            recordedFirstEntryId = reader.ReadRedisValue();
                            break;
                        case Literals.entries_added.HashCS when Literals.entries_added.IsCS(hash, keyBytes):
                            if (!reader.TryReadInt64(out entriesAdded)) return false;
                            break;
                        // 8.6
                        case Literals.idmp_duration.HashCS when Literals.idmp_duration.IsCS(hash, keyBytes):
                            if (!reader.TryReadInt64(out idmpDuration)) return false;
                            break;
                        case Literals.idmp_maxsize.HashCS when Literals.idmp_maxsize.IsCS(hash, keyBytes):
                            if (!reader.TryReadInt64(out idmpMaxsize)) return false;
                            break;
                        case Literals.pids_tracked.HashCS when Literals.pids_tracked.IsCS(hash, keyBytes):
                            if (!reader.TryReadInt64(out pidsTracked)) return false;
                            break;
                        case Literals.iids_tracked.HashCS when Literals.iids_tracked.IsCS(hash, keyBytes):
                            if (!reader.TryReadInt64(out iidsTracked)) return false;
                            break;
                        case Literals.iids_added.HashCS when Literals.iids_added.IsCS(hash, keyBytes):
                            if (!reader.TryReadInt64(out iidsAdded)) return false;
                            break;
                        case Literals.iids_duplicates.HashCS when Literals.iids_duplicates.IsCS(hash, keyBytes):
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

        internal sealed class StreamPendingInfoProcessor : ResultProcessor<StreamPendingInfo>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
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

                var pendingInfo = new StreamPendingInfo(
                    pendingMessageCount: checked((int)pendingMessageCount),
                    lowestId: lowestId,
                    highestId: highestId,
                    consumers: consumers ?? []);

                SetResult(message, pendingInfo);
                return true;
            }
        }

        internal sealed class StreamPendingMessagesProcessor : ResultProcessor<StreamPendingMessageInfo[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (!reader.IsAggregate)
                {
                    return false;
                }

                var messageInfoArray = reader.ReadPastArray(
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
                            deliveryCount: checked((int)deliveryCount));
                    },
                    scalar: false);

                SetResult(message, messageInfoArray!);
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

            protected override NameValueEntry Parse(in RawResult first, in RawResult second, object? state)
                => new NameValueEntry(first.AsRedisValue(), second.AsRedisValue());
        }

        /// <summary>
        /// Handles stream responses. For formats, see <see href="https://redis.io/topics/streams-intro"/>.
        /// </summary>
        /// <typeparam name="T">The type of the stream result.</typeparam>
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
                // optional (XREADGROUP with CLAIM):
                //  [2] = idle time (in milliseconds)
                //  [3] = delivery count
                var entryDetails = item.GetItems();

                var id = entryDetails[0].AsRedisValue();
                var values = ParseStreamEntryValues(entryDetails[1]);
                // check for optional fields (XREADGROUP with CLAIM)
                if (entryDetails.Length >= 4 && entryDetails[2].TryGetInt64(out var idleTimeInMs) && entryDetails[3].TryGetInt64(out var deliveryCount))
                {
                    return new StreamEntry(
                        id: id,
                        values: values,
                        idleTime: TimeSpan.FromMilliseconds(idleTimeInMs),
                        deliveryCount: checked((int)deliveryCount));
                }
                return new StreamEntry(
                    id: id,
                    values: values);
            }

            protected static StreamEntry ParseRedisStreamEntry(ref RespReader reader, RedisProtocol protocol)
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
                var values = ParseStreamEntryValues(ref iter.Value, protocol);

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
                                deliveryCount: checked((int)deliveryCount));
                        }
                    }
                }

                return new StreamEntry(
                    id: id,
                    values: values);
            }
            protected internal StreamEntry[] ParseRedisStreamEntries(in RawResult result) =>
                result.GetItems().ToArray((in item, in _) => ParseRedisStreamEntry(item), this);

            protected internal StreamEntry[] ParseRedisStreamEntries(ref RespReader reader, RedisProtocol protocol)
            {
                if (!reader.IsAggregate || reader.IsNull)
                {
                    return [];
                }

                return reader.ReadPastArray(
                    ref protocol,
                    static (ref protocol, ref r) => ParseRedisStreamEntry(ref r, protocol),
                    scalar: false) ?? [];
            }

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
                    return [];
                }
                return StreamNameValueEntryProcessor.Instance.ParseArray(result, false, out _, null)!; // ! because we checked null above
            }

            protected static NameValueEntry[] ParseStreamEntryValues(ref RespReader reader, RedisProtocol protocol)
            {
                if (!reader.IsAggregate || reader.IsNull)
                {
                    return [];
                }
                return StreamNameValueEntryProcessor.Instance.ParseArray(ref reader, protocol, false, out _, null)!;
            }
        }

        private sealed class StringPairInterleavedProcessor : ValuePairInterleavedProcessorBase<KeyValuePair<string, string>>
        {
            protected override KeyValuePair<string, string> Parse(ref RespReader first, ref RespReader second, object? state) =>
                new KeyValuePair<string, string>(first.ReadString()!, second.ReadString()!);

            protected override KeyValuePair<string, string> Parse(in RawResult first, in RawResult second, object? state) =>
                new KeyValuePair<string, string>(first.GetString()!, second.GetString()!);
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

        private sealed class TracerProcessor : ResultProcessor<bool>
        {
            private readonly bool establishConnection;

            public TracerProcessor(bool establishConnection)
            {
                this.establishConnection = establishConnection;
            }

            public override bool SetResult(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                reader.MovePastBof();
                bool isError = reader.IsError;
                var copy = reader;

                connection.BridgeCouldBeNull?.Multiplexer.OnInfoMessage($"got '{reader.Prefix}' for '{message.CommandAndKey}' on '{connection}'");
                var final = base.SetResult(connection, message, ref reader);

                if (isError)
                {
                    reader = copy; // rewind and re-parse
                    if (reader.StartsWith(Literals.ERR_not_permitted.U8) || reader.StartsWith(Literals.NOAUTH.U8))
                    {
                        connection.RecordConnectionFailed(ConnectionFailureType.AuthenticationFailure, new Exception(reader.GetOverview() + " Verify if the Redis password provided is correct. Attempted command: " + message.Command));
                    }
                    else if (reader.StartsWith(Literals.LOADING.U8))
                    {
                        connection.RecordConnectionFailed(ConnectionFailureType.Loading);
                    }
                    else
                    {
                        connection.RecordConnectionFailed(ConnectionFailureType.ProtocolFailure, new RedisServerException(reader.GetOverview()));
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
            protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
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
                    connection.RecordConnectionFailed(
                        ConnectionFailureType.ProtocolFailure,
                        new InvalidOperationException($"unexpected tracer reply to {message.Command}: {result.ToString()}"));
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

                                Span<byte> keyBuffer = stackalloc byte[16]; // Buffer for key names
                                while (itemReader.TryMoveNext() && itemReader.IsScalar)
                                {
                                    // Capture the scalar key
                                    var keyBytes = itemReader.TryGetSpan(out var tmp) ? tmp : itemReader.Buffer(keyBuffer);
                                    var hash = FastHash.HashCS(keyBytes);

                                    // Check for second scalar value
                                    if (!(itemReader.TryMoveNext() && itemReader.IsScalar)) break;

                                    // Use FastHash pattern to identify "ip" vs "port"
                                    switch (hash)
                                    {
                                        case Literals.ip.HashCS when Literals.ip.IsCS(hash, keyBytes):
                                            host = itemReader.ReadString();
                                            break;

                                        case Literals.port.HashCS when Literals.port.IsCS(hash, keyBytes):
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

                                Span<byte> keyBuffer = stackalloc byte[16]; // Buffer for key names
                                while (r.TryMoveNext() && r.IsScalar)
                                {
                                    // Capture the scalar key
                                    var keyBytes = r.TryGetSpan(out var tmp) ? tmp : r.Buffer(keyBuffer);
                                    var hash = FastHash.HashCS(keyBytes);

                                    // Check for second scalar value
                                    if (!(r.TryMoveNext() && r.IsScalar)) break;

                                    // Use FastHash pattern to identify "ip" vs "port"
                                    switch (hash)
                                    {
                                        case Literals.ip.HashCS when Literals.ip.IsCS(hash, keyBytes):
                                            host = r.ReadString();
                                            break;

                                        case Literals.port.HashCS when Literals.port.IsCS(hash, keyBytes):
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
            protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
            {
                if (StringPairInterleaved is not StringPairInterleavedProcessor innerProcessor)
                {
                    return false;
                }

                if (reader.IsAggregate && !reader.IsNull)
                {
                    var protocol = connection.Protocol.GetValueOrDefault();
                    var state = (innerProcessor, protocol, message);
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
