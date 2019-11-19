using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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
            TrackSubscriptions = new TrackSubscriptionsProcessor(),
            Tracer = new TracerProcessor(false),
            EstablishConnection = new TracerProcessor(true),
            BackgroundSaveStarted = new ExpectBasicStringProcessor(CommonReplies.backgroundSavingStarted_trimmed, startsWith: true);

        public static readonly ResultProcessor<byte[]>
            ByteArray = new ByteArrayProcessor(),
            ScriptLoad = new ScriptLoadProcessor();

        public static readonly ResultProcessor<ClusterConfiguration>
            ClusterNodes = new ClusterNodesProcessor();

        public static readonly ResultProcessor<EndPoint>
            ConnectionIdentity = new ConnectionIdentityProcessor();

        public static readonly ResultProcessor<DateTime>
            DateTime = new DateTimeProcessor();

        public static readonly ResultProcessor<double>
                                            Double = new DoubleProcessor();
        public static readonly ResultProcessor<IGrouping<string, KeyValuePair<string, string>>[]>
            Info = new InfoProcessor();

        public static readonly MultiStreamProcessor
            MultiStream = new MultiStreamProcessor();

        public static readonly ResultProcessor<long>
            Int64 = new Int64Processor(),
            PubSubNumSub = new PubSubNumSubProcessor();

        public static readonly ResultProcessor<double?>
                            NullableDouble = new NullableDoubleProcessor();
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

        public static readonly ResultProcessor<string[]>
            StringArray = new StringArrayProcessor();

        public static readonly ResultProcessor<GeoPosition?[]>
            RedisGeoPositionArray = new RedisValueGeoPositionArrayProcessor();
        public static readonly ResultProcessor<GeoPosition?>
            RedisGeoPosition = new RedisValueGeoPositionProcessor();

        public static readonly ResultProcessor<TimeSpan>
            ResponseTimer = new TimingProcessor();

        public static readonly ResultProcessor<RedisResult>
            ScriptResult = new ScriptResultProcessor();

        public static readonly SortedSetEntryProcessor
            SortedSetEntry = new SortedSetEntryProcessor();
        public static readonly SortedSetEntryArrayProcessor
            SortedSetWithScores = new SortedSetEntryArrayProcessor();

        public static readonly SingleStreamProcessor
            SingleStream = new SingleStreamProcessor();

        public static readonly SingleStreamProcessor
            SingleStreamWithNameSkip = new SingleStreamProcessor(skipStreamName: true);

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

        public static readonly ResultProcessor<string>
                            String = new StringProcessor(),
            ClusterNodesRaw = new ClusterNodesRawProcessor();

        #region Sentinel

        public static readonly ResultProcessor<EndPoint>
            SentinelMasterEndpoint = new SentinelGetMasterAddressByNameProcessor();

        public static readonly ResultProcessor<KeyValuePair<string, string>[][]>
            SentinelArrayOfArrays = new SentinelArrayOfArraysProcessor();

        #endregion

        public static readonly ResultProcessor<KeyValuePair<string, string>[]>
            StringPairInterleaved = new StringPairInterleavedProcessor();
        public static readonly TimeSpanProcessor
            TimeSpanFromMilliseconds = new TimeSpanProcessor(true),
            TimeSpanFromSeconds = new TimeSpanProcessor(false);
        public static readonly HashEntryArrayProcessor
            HashEntryArray = new HashEntryArrayProcessor();

        public void ConnectionFail(Message message, ConnectionFailureType fail, Exception innerException, string annotation)
        {
            PhysicalConnection.IdentifyFailureType(innerException, ref fail);

            string exMessage = fail.ToString() + (message == null ? "" : (" on " + (
                fail == ConnectionFailureType.ProtocolFailure ? message.ToString() : message.CommandAndKey)));
            if (!string.IsNullOrWhiteSpace(annotation)) exMessage += ", " + annotation;

            var ex = innerException == null ? new RedisConnectionException(fail, exMessage)
                : new RedisConnectionException(fail, exMessage, innerException);
            SetException(message, ex);
        }

        public void ConnectionFail(Message message, ConnectionFailureType fail, string errorMessage)
        {
            SetException(message, new RedisConnectionException(fail, errorMessage));
        }

        public void ServerFail(Message message, string errorMessage)
        {
            SetException(message, new RedisServerException(errorMessage));
        }

        public void SetException(Message message, Exception ex)
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
                    logging.Log?.WriteLine($"Response from {bridge} / {message.CommandAndKey}: {result}");
                }
                catch { }
            }
            if (result.IsError)
            {
                if (result.StartsWith(CommonReplies.NOAUTH)) bridge?.Multiplexer?.SetAuthSuspect();

                var server = bridge.ServerEndPoint;
                bool log = !message.IsInternalCall;
                bool isMoved = result.StartsWith(CommonReplies.MOVED);
                bool wasNoRedirect = ( message.Flags & CommandFlags.NoRedirect ) != 0;
                string err = string.Empty;
                bool unableToConnectError = false;
                if (isMoved || result.StartsWith(CommonReplies.ASK))
                {
                    message.SetResponseReceived();

                    log = false;
                    string[] parts = result.GetString().Split(StringSplits.Space, 3);
                    EndPoint endpoint;
                    if (Format.TryParseInt32(parts[1], out int hashSlot)
                        && (endpoint = Format.TryParseEndPoint(parts[2])) != null)
                    {
                        // no point sending back to same server, and no point sending to a dead server
                        if (!Equals(server.EndPoint, endpoint))
                        {
                            if (bridge == null)
                            { } // already toast
                            else if (bridge.Multiplexer.TryResend(hashSlot, message, endpoint, isMoved))
                            {
                                bridge.Multiplexer.Trace(message.Command + " re-issued to " + endpoint, isMoved ? "MOVED" : "ASK");
                                return false;
                            }
                            else
                            {
                                if (isMoved && wasNoRedirect)
                                {
                                    err = $"Key has MOVED to Endpoint {endpoint} and hashslot {hashSlot} but CommandFlags.NoRedirect was specified - redirect not followed for {message.CommandAndKey}. ";
                                }
                                else
                                {
                                    unableToConnectError = true;
                                    err = $"Endpoint {endpoint} serving hashslot {hashSlot} is not reachable at this point of time. Please check connectTimeout value. If it is low, try increasing it to give the ConnectionMultiplexer a chance to recover from the network disconnect. "
                                        + PerfCounterHelper.GetThreadPoolAndCPUSummary(bridge.Multiplexer.IncludePerformanceCountersInExceptions);
                                }
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(err))
                {
                    err = result.GetString();
                }

                if (log)
                {
                    bridge.Multiplexer.OnErrorMessage(server.EndPoint, err);
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
            ConnectionFail(message, ConnectionFailureType.ProtocolFailure, "Unexpected response to " + (message?.Command.ToString() ?? "n/a") + ": " + result.ToString());
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
                switch (result.Type)
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
            public static TimerMessage CreateMessage(int db, CommandFlags flags, RedisCommand command, RedisValue value = default(RedisValue))
            {
                return new TimerMessage(db, flags, command, value);
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Type == ResultType.Error)
                {
                    return false;
                }
                else
                {   // don't check the actual reply; there are multiple ways of constructing
                    // a timing message, and we don't actually care about what approach was used
                    TimeSpan duration;
                    if (message is TimerMessage timingMessage)
                    {
                        var watch = timingMessage.Watch;
                        watch.Stop();
                        duration = watch.Elapsed;
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
                public readonly Stopwatch Watch;
                private readonly RedisValue value;
                public TimerMessage(int db, CommandFlags flags, RedisCommand command, RedisValue value)
                    : base(db, flags, command)
                {
                    Watch = Stopwatch.StartNew();
                    this.value = value;
                }

                protected override void WriteImpl(PhysicalConnection physical)
                {
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
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Type == ResultType.MultiBulk)
                {
                    var items = result.GetItems();
                    if (items.Length >= 3 && items[2].TryGetInt64(out long count))
                    {
                        connection.SubscriptionCount = count;
                        return true;
                    }
                }
                return false;
            }
        }

        internal sealed class DemandZeroOrOneProcessor : ResultProcessor<bool>
        {
            public static bool TryGet(in RawResult result, out bool value)
            {
                switch (result.Type)
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
            private static readonly Regex sha1 = new Regex("^[0-9a-f]{40}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            internal static bool IsSHA1(string script)
            {
                return script != null && sha1.IsMatch(script);
            }

            internal const int Sha1HashLength = 20;
            internal static byte[] ParseSHA1(byte[] value)
            {
                if (value?.Length == Sha1HashLength * 2)
                {
                    var tmp = new byte[Sha1HashLength];
                    int charIndex = 0;
                    for (int i = 0; i < tmp.Length; i++)
                    {
                        int x = FromHex((char)value[charIndex++]), y = FromHex((char)value[charIndex++]);
                        if (x < 0 || y < 0) return null;
                        tmp[i] = (byte)((x << 4) | y);
                    }
                    return tmp;
                }
                return null;
            }

            internal static byte[] ParseSHA1(string value)
            {
                if (value?.Length == (Sha1HashLength * 2) && sha1.IsMatch(value))
                {
                    var tmp = new byte[Sha1HashLength];
                    int charIndex = 0;
                    for (int i = 0; i < tmp.Length; i++)
                    {
                        int x = FromHex(value[charIndex++]), y = FromHex(value[charIndex++]);
                        if (x < 0 || y < 0) return null;
                        tmp[i] = (byte)((x << 4) | y);
                    }
                    return tmp;
                }
                return null;
            }

            private static int FromHex(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }
            // note that top-level error messages still get handled by SetResult, but nested errors
            // (is that a thing?) will be wrapped in the RedisResult
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
                {
                    case ResultType.BulkString:
                        var asciiHash = result.GetBlob();
                        if (asciiHash == null || asciiHash.Length != (Sha1HashLength * 2)) return false;

                        byte[] hash = null;
                        if (!message.IsInternalCall)
                        {
                            hash = ParseSHA1(asciiHash); // external caller wants the hex bytes, not the ascii bytes
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
            public bool TryParse(in RawResult result, out SortedSetEntry? entry)
            {
                switch (result.Type)
                {
                    case ResultType.MultiBulk:
                        var arr = result.GetItems();
                        if (result.IsNull || arr.Length < 2)
                        {
                            entry = null;
                        }
                        else
                        {
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
            protected override SortedSetEntry Parse(in RawResult first, in RawResult second)
            {
                return new SortedSetEntry(first.AsRedisValue(), second.TryGetDouble(out double val) ? val : double.NaN);
            }
        }

        internal sealed class HashEntryArrayProcessor : ValuePairInterleavedProcessorBase<HashEntry>
        {
            protected override HashEntry Parse(in RawResult first, in RawResult second)
            {
                return new HashEntry(first.AsRedisValue(), second.AsRedisValue());
            }
        }

        internal abstract class ValuePairInterleavedProcessorBase<T> : ResultProcessor<T[]>
        {
            public bool TryParse(in RawResult result, out T[] pairs)
            {
                switch (result.Type)
                {
                    case ResultType.MultiBulk:
                        var arr = result.GetItems();
                        if (result.IsNull)
                        {
                            pairs = null;
                        }
                        else
                        {
                            int count = (int)arr.Length / 2;
                            if (count == 0)
                            {
                                pairs = Array.Empty<T>();
                            }
                            else
                            {
                                pairs = new T[count];
                                if (arr.IsSingleSegment)
                                {
                                    var span = arr.FirstSpan;
                                    int offset = 0;
                                    for (int i = 0; i < pairs.Length; i++)
                                    {
                                        pairs[i] = Parse(span[offset++], span[offset++]);
                                    }
                                }
                                else
                                {
                                    var iter = arr.GetEnumerator(); // simplest way of getting successive values
                                    for (int i = 0; i < pairs.Length; i++)
                                    {
                                        pairs[i] = Parse(iter.GetNext(), iter.GetNext());
                                    }
                                }
                            }
                        }
                        return true;
                    default:
                        pairs = null;
                        return false;
                }
            }

            protected abstract T Parse(in RawResult first, in RawResult second);
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (TryParse(result, out T[] arr))
                {
                    SetResult(message, arr);
                    return true;
                }
                return false;
            }
        }

        private sealed class AutoConfigureProcessor : ResultProcessor<bool>
        {
            public override bool SetResult(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.IsError && result.StartsWith(CommonReplies.READONLY))
                {
                    var bridge = connection.BridgeCouldBeNull;
                    if(bridge != null)
                    {
                        var server = bridge.ServerEndPoint;
                        server.Multiplexer.Trace("Auto-configured role: slave");
                        server.IsSlave = true;
                    }
                }
                return base.SetResult(connection, message, result);
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                var server = connection.BridgeCouldBeNull?.ServerEndPoint;
                if (server == null) return false;
                switch (result.Type)
                {
                    case ResultType.BulkString:
                        if (message?.Command == RedisCommand.INFO)
                        {
                            string info = result.GetString(), line;
                            if (string.IsNullOrWhiteSpace(info))
                            {
                                SetResult(message, true);
                                return true;
                            }
                            string masterHost = null, masterPort = null;
                            bool roleSeen = false;
                            using (var reader = new StringReader(info))
                            {
                                while ((line = reader.ReadLine()) != null)
                                {
                                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("# ")) continue;

                                    string val;
                                    if ((val = Extract(line, "role:")) != null)
                                    {
                                        roleSeen = true;
                                        switch (val)
                                        {
                                            case "master":
                                                server.IsSlave = false;
                                                server.Multiplexer.Trace("Auto-configured role: master");
                                                break;
                                            case "slave":
                                                server.IsSlave = true;
                                                server.Multiplexer.Trace("Auto-configured role: slave");
                                                break;
                                        }
                                    }
                                    else if ((val = Extract(line, "master_host:")) != null)
                                    {
                                        masterHost = val;
                                    }
                                    else if ((val = Extract(line, "master_port:")) != null)
                                    {
                                        masterPort = val;
                                    }
                                    else if ((val = Extract(line, "redis_version:")) != null)
                                    {
                                        if (Version.TryParse(val, out Version version))
                                        {
                                            server.Version = version;
                                            server.Multiplexer.Trace("Auto-configured version: " + version);
                                        }
                                    }
                                    else if ((val = Extract(line, "redis_mode:")) != null)
                                    {
                                        switch (val)
                                        {
                                            case "standalone":
                                                server.ServerType = ServerType.Standalone;
                                                server.Multiplexer.Trace("Auto-configured server-type: standalone");
                                                break;
                                            case "cluster":
                                                server.ServerType = ServerType.Cluster;
                                                server.Multiplexer.Trace("Auto-configured server-type: cluster");
                                                break;
                                            case "sentinel":
                                                server.ServerType = ServerType.Sentinel;
                                                server.Multiplexer.Trace("Auto-configured server-type: sentinel");
                                                break;
                                        }
                                    }
                                    else if ((val = Extract(line, "run_id:")) != null)
                                    {
                                        server.RunId = val;
                                    }
                                }
                                if (roleSeen)
                                { // these are in the same section, if presnt
                                    server.MasterEndPoint = Format.TryParseEndPoint(masterHost, masterPort);
                                }
                            }
                        }
                        SetResult(message, true);
                        return true;
                    case ResultType.MultiBulk:
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
                                        server.Multiplexer.Trace("Auto-configured timeout: " + targetSeconds + "s");
                                        server.WriteEverySeconds = targetSeconds;
                                    }
                                }
                                else if (key.IsEqual(CommonReplies.databases) && val.TryGetInt64(out i64))
                                {
                                    int dbCount = checked((int)i64);
                                    server.Multiplexer.Trace("Auto-configured databases: " + dbCount);
                                    server.Databases = dbCount;
                                }
                                else if (key.IsEqual(CommonReplies.slave_read_only))
                                {
                                    if (val.IsEqual(CommonReplies.yes))
                                    {
                                        server.SlaveReadOnly = true;
                                        server.Multiplexer.Trace("Auto-configured slave-read-only: true");
                                    }
                                    else if (val.IsEqual(CommonReplies.no))
                                    {
                                        server.SlaveReadOnly = false;
                                        server.Multiplexer.Trace("Auto-configured slave-read-only: false");
                                    }
                                }
                            }
                        }
                        SetResult(message, true);
                        return true;
                }
                return false;
            }

            private static string Extract(string line, string prefix)
            {
                if (line.StartsWith(prefix)) return line.Substring(prefix.Length).Trim();
                return null;
            }
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
                switch (result.Type)
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
                    case ResultType.MultiBulk:
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

        private sealed class ByteArrayProcessor : ResultProcessor<byte[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
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
                var bridge = connection.BridgeCouldBeNull;
                if (bridge == null) throw new ObjectDisposedException(connection.ToString());
                var server = bridge.ServerEndPoint;
                var config = new ClusterConfiguration(bridge.Multiplexer.ServerSelectionStrategy, nodes, server.EndPoint);
                server.SetClusterConfiguration(config);
                return config;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
                {
                    case ResultType.BulkString:
                        string nodes = result.GetString();
                        var bridge = connection.BridgeCouldBeNull;
                        if (bridge != null) bridge.ServerEndPoint.ServerType = ServerType.Cluster;
                        var config = Parse(connection, nodes);
                        SetResult(message, config);
                        return true;
                }
                return false;
            }
        }

        private sealed class ClusterNodesRawProcessor : ResultProcessor<string>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        string nodes = result.GetString();
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
                SetResult(message, connection.BridgeCouldBeNull?.ServerEndPoint?.EndPoint);
                return true;
            }
        }

        private sealed class DateTimeProcessor : ResultProcessor<DateTime>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                long unixTime;
                switch (result.Type)
                {
                    case ResultType.Integer:
                        if (result.TryGetInt64(out unixTime))
                        {
                            var time = RedisBase.UnixEpoch.AddSeconds(unixTime);
                            SetResult(message, time);
                            return true;
                        }
                        break;
                    case ResultType.MultiBulk:
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
                                    var time = RedisBase.UnixEpoch.AddSeconds(unixTime).AddTicks(micros * 10); // datetime ticks are 100ns
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

        private sealed class DoubleProcessor : ResultProcessor<double>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
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
                if(message.Command == RedisCommand.AUTH) connection?.BridgeCouldBeNull?.Multiplexer?.SetAuthSuspect();
                return false;
            }
        }

        private sealed class InfoProcessor : ResultProcessor<IGrouping<string, KeyValuePair<string, string>>[]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Type == ResultType.BulkString)
                {
                    string category = Normalize(null), line;
                    var list = new List<Tuple<string, KeyValuePair<string, string>>>();
                    using (var reader = new StringReader(result.GetString()))
                    {
                        while ((line = reader.ReadLine()) != null)
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

            private static string Normalize(string category)
            {
                return string.IsNullOrWhiteSpace(category) ? "miscellaneous" : category.Trim();
            }
        }

        private class Int64Processor : ResultProcessor<long>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
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
                if (result.Type == ResultType.MultiBulk)
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

        private sealed class NullableDoubleProcessor : ResultProcessor<double?>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
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
                switch (result.Type)
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
                public readonly byte[] Prefix;
                public readonly RedisChannel.PatternMode Mode;
                public ChannelState(byte[] prefix, RedisChannel.PatternMode mode)
                {
                    Prefix = prefix;
                    Mode = mode;
                }
            }
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
                {
                    case ResultType.MultiBulk:
                        var final = result.ToArray(
                                (in RawResult item, in ChannelState state) => item.AsRedisChannel(state.Prefix, state.Mode),
                                new ChannelState(connection.ChannelPrefix, mode));

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
                switch (result.Type)
                {
                    case ResultType.MultiBulk:
                        var arr = result.GetItemsAsKeys();
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
                switch (result.Type)
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
                switch (result.Type)
                {
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        string s = result.GetString();
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
                switch (result.Type)
                {
                    // allow a single item to pass explicitly pretending to be an array; example: SPOP {key} 1
                    case ResultType.BulkString:
                        var arr = new[] { result.AsRedisValue() };
                        SetResult(message, arr);
                        return true;
                    case ResultType.MultiBulk:
                        arr = result.GetItemsAsValues();
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
                switch (result.Type)
                {
                    case ResultType.MultiBulk:
                        var arr = result.GetItemsAsStrings();

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
                switch (result.Type)
                {
                    case ResultType.MultiBulk:
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
                switch (result.Type)
                {
                    case ResultType.MultiBulk:
                        var arr = result.GetItemsAsGeoPositionArray();

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
                switch (result.Type)
                {
                    case ResultType.MultiBulk:
                        var typed = result.ToArray(
                            (in RawResult item, in GeoRadiusOptions options) => Parse(item, options), this.options);
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

        private sealed class RedisValueProcessor : ResultProcessor<RedisValue>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
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

        private sealed class LeaseProcessor : ResultProcessor<Lease<byte>>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        SetResult(message, result.AsLease());
                        return true;
                }
                return false;
            }
        }

        private class ScriptResultProcessor : ResultProcessor<RedisResult>
        {
            public override bool SetResult(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Type == ResultType.Error && result.StartsWith(CommonReplies.NOSCRIPT))
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
                var value = Redis.RedisResult.TryCreate(connection, result);
                if (value != null)
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

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.IsNull)
                {
                    // Server returns 'nil' if no entries are returned for the given stream.
                    SetResult(message, Array.Empty<StreamEntry>());
                    return true;
                }

                if (result.Type != ResultType.MultiBulk)
                {
                    return false;
                }

                StreamEntry[] entries;

                if (skipStreamName)
                {
                    // Skip the first element in the array (i.e., the stream name).
                    // See https://redis.io/commands/xread.

                    // > XREAD COUNT 2 STREAMS mystream 0
                    // 1) 1) "mystream"                     <== Skip the stream name
                    //    2) 1) 1) 1519073278252 - 0        <== Index 1 contains the array of stream entries
                    //          2) 1) "foo"
                    //             2) "value_1"
                    //       2) 1) 1519073279157 - 0
                    //          2) 1) "foo"
                    //             2) "value_2"

                    // Retrieve the initial array. For XREAD of a single stream it will
                    // be an array of only 1 element in the response.
                    var readResult = result.GetItems();

                    // Within that single element, GetItems will return an array of
                    // 2 elements: the stream name and the stream entries.
                    // Skip the stream name (index 0) and only process the stream entries (index 1).
                    entries = ParseRedisStreamEntries(readResult[0].GetItems()[1]);
                }
                else
                {
                    entries = ParseRedisStreamEntries(result);
                }

                SetResult(message, entries);
                return true;
            }
        }

        internal sealed class MultiStreamProcessor : StreamProcessorBase<RedisStream[]>
        {
            /*
                The result is similar to the XRANGE result (see SingleStreamProcessor)
                with the addition of the stream name as the first element of top level
                Multibulk array.

                See https://redis.io/commands/xread.

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

                if (result.Type != ResultType.MultiBulk)
                {
                    return false;
                }

                var streams = result.GetItems().ToArray((in RawResult item, in MultiStreamProcessor obj) =>
                {
                    var details = item.GetItems();

                    // details[0] = Name of the Stream
                    // details[1] = Multibulk Array of Stream Entries
                    return new RedisStream(key: details[0].AsRedisKey(),
                        entries: obj.ParseRedisStreamEntries(details[1]));
                }, this);

                SetResult(message, streams);
                return true;
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

                return new StreamConsumerInfo(name: arr[1].AsRedisValue(),
                            pendingMessageCount: (int)arr[3].AsRedisValue(),
                            idleTimeInMilliseconds: (long)arr[5].AsRedisValue());
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
                // 2) 1) name
                //    2) "some-other-group"
                //    3) consumers
                //    4) (integer)1
                //    5) pending
                //    6) (integer)0

                var arr = result.GetItems();

                return new StreamGroupInfo(name: arr[1].AsRedisValue(),
                    consumerCount: (int)arr[3].AsRedisValue(),
                    pendingMessageCount: (int)arr[5].AsRedisValue());
            }
        }

        internal abstract class InterleavedStreamInfoProcessorBase<T> : ResultProcessor<T[]>
        {
            protected abstract T ParseItem(in RawResult result);

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (result.Type != ResultType.MultiBulk)
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
                if (result.Type != ResultType.MultiBulk)
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

                if (result.Type != ResultType.MultiBulk)
                {
                    return false;
                }

                var arr = result.GetItems();

                if (arr.Length != 4)
                {
                    return false;
                }

                StreamConsumer[] consumers = null;

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
                if (result.Type != ResultType.MultiBulk)
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

        internal abstract class StreamProcessorBase<T> : ResultProcessor<T>
        {
            // For command response formats see https://redis.io/topics/streams-intro.

            protected StreamEntry ParseRedisStreamEntry(in RawResult item)
            {
                if (item.IsNull || item.Type != ResultType.MultiBulk)
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
            protected StreamEntry[] ParseRedisStreamEntries(in RawResult result)
            {
                if (result.Type != ResultType.MultiBulk)
                {
                    return null;
                }

                return result.GetItems().ToArray(
                    (in RawResult item, in StreamProcessorBase<T> obj) => obj.ParseRedisStreamEntry(item), this);
            }

            protected NameValueEntry[] ParseStreamEntryValues(in RawResult result)
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

                if (result.Type != ResultType.MultiBulk || result.IsNull)
                {
                    return null;
                }

                var arr = result.GetItems();

                // Calculate how many name/value pairs are in the stream entry.
                int count = (int)arr.Length / 2;

                if (count == 0) return Array.Empty<NameValueEntry>();

                var pairs = new NameValueEntry[count];

                var iter = arr.GetEnumerator();
                for (int i = 0; i < pairs.Length; i++)
                {
                    pairs[i] = new NameValueEntry(iter.GetNext().AsRedisValue(),
                                                  iter.GetNext().AsRedisValue());
                }

                return pairs;
            }
        }

        private sealed class StringPairInterleavedProcessor : ValuePairInterleavedProcessorBase<KeyValuePair<string, string>>
        {
            protected override KeyValuePair<string, string> Parse(in RawResult first, in RawResult second)
            {
                return new KeyValuePair<string, string>(first.GetString(), second.GetString());
            }
        }

        private sealed class StringProcessor : ResultProcessor<string>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                switch (result.Type)
                {
                    case ResultType.Integer:
                    case ResultType.SimpleString:
                    case ResultType.BulkString:
                        SetResult(message, result.GetString());
                        return true;
                    case ResultType.MultiBulk:
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

        private class TracerProcessor : ResultProcessor<bool>
        {
            private readonly bool establishConnection;

            public TracerProcessor(bool establishConnection)
            {
                this.establishConnection = establishConnection;
            }

            public override bool SetResult(PhysicalConnection connection, Message message, in RawResult result)
            {
                connection?.BridgeCouldBeNull?.Multiplexer.OnInfoMessage($"got '{result}' for '{message.CommandAndKey}' on '{connection}'");
                var final = base.SetResult(connection, message, result);
                if (result.IsError)
                {
                    if (result.StartsWith(CommonReplies.authFail_trimmed) || result.StartsWith(CommonReplies.NOAUTH))
                    {
                        connection.RecordConnectionFailed(ConnectionFailureType.AuthenticationFailure, new Exception(result.ToString() + " Verify if the Redis password provided is correct."));
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
                return final;
            }

            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                bool happy;
                switch (message.Command)
                {
                    case RedisCommand.ECHO:
                        happy = result.Type == ResultType.BulkString && (!establishConnection || result.IsEqual(connection.BridgeCouldBeNull?.Multiplexer?.UniqueId));
                        break;
                    case RedisCommand.PING:
                        // there are two different PINGs; "interactive" is a +PONG or +{your message},
                        // but subscriber returns a bulk-array of [ "pong", {your message} ]
                        switch (result.Type)
                        {
                            case ResultType.SimpleString:
                                happy = result.IsEqual(CommonReplies.PONG);
                                break;
                            case ResultType.MultiBulk:
                                if (result.ItemsCount == 2)
                                {
                                    var items = result.GetItems();
                                    happy = items[0].IsEqual(CommonReplies.PONG) && items[1].Payload.IsEmpty;
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
                        happy = result.Type == ResultType.MultiBulk && result.GetItems().Length == 2;
                        break;
                    case RedisCommand.EXISTS:
                        happy = result.Type == ResultType.Integer;
                        break;
                    default:
                        happy = false;
                        break;
                }
                if (happy)
                {
                    if (establishConnection) connection.BridgeCouldBeNull?.OnFullyEstablished(connection);
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

        #region Sentinel

        private sealed class SentinelGetMasterAddressByNameProcessor : ResultProcessor<EndPoint>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                // To repro timeout fail:
                // if (result.Type == ResultType.MultiBulk) throw new Exception("Woops");
                switch (result.Type)
                {
                    case ResultType.MultiBulk:
                        var arr = result.GetItemsAsValues();

                        if (result.IsNull)
                        {
                            return true;
                        }
                        else if (arr.Length == 2 && Format.TryParseInt32(arr[1], out var port))
                        {
                            SetResult(message, Format.ParseEndPoint(arr[0], port));
                            return true;
                        }
                        else if (arr.Length == 0)
                        {
                            SetResult(message, null);
                            return true;
                        }
                        break;
                    case ResultType.SimpleString:
                        //We don't want to blow up if the master is not found
                        if (result.IsNull)
                            return true;
                        break;
                }
                return false;
            }
        }

        private sealed class SentinelArrayOfArraysProcessor : ResultProcessor<KeyValuePair<string, string>[][]>
        {
            protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
            {
                if (!(StringPairInterleaved is StringPairInterleavedProcessor innerProcessor))
                {
                    return false;
                }

                switch (result.Type)
                {
                    case ResultType.MultiBulk:
                        var arrayOfArrays = result.GetItems();

                        var returnArray = result.ToArray<KeyValuePair<string, string>[], StringPairInterleavedProcessor>(
                            (in RawResult rawInnerArray, in StringPairInterleavedProcessor proc) =>
                            {
                                proc.TryParse(rawInnerArray, out KeyValuePair<string, string>[] kvpArray);
                                return kvpArray;
                            }, innerProcessor);

                        SetResult(message, returnArray);
                        return true;
                }
                return false;
            }
        }

        #endregion
    }

    internal abstract class ResultProcessor<T> : ResultProcessor
    {
        protected void SetResult(Message message, T value)
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
            switch(result.Type)
            {
                case ResultType.MultiBulk:
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
