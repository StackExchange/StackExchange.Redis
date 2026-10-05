using System;
using System.Collections.Generic;
using System.Net;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

// The stream, role and scalar parsers, moved here from ResultProcessor unchanged; see RespParsers.cs.
internal static partial class RespParsers
{
    internal static int ParseStreamDeliveryCount(long deliveryCount)
        => deliveryCount == long.MaxValue ? int.MinValue : checked((int)deliveryCount);

    /// <summary>Whether this scalar reply says what was expected, exactly or as a prefix.</summary>
    /// <param name="reader">Positioned on the reply.</param>
    /// <param name="expected">The text the server should have said.</param>
    /// <param name="startsWith">Whether a longer reply beginning with it counts.</param>
    /// <remarks>
    /// <b>Internal and static so both cores check it the same way.</b> "Did the server agree?" is a
    /// yes/no about bytes, and two implementations of it would be two chances to accept something the
    /// other rejects - which for <c>BGSAVE</c> is the difference between "started" and silence. The
    /// shipped processor below and the context surface's handlers are callers of this one check.
    /// </remarks>
    internal static bool ScalarSays(ref RespReader reader, in AsciiHash expected, bool startsWith)
    {
        if (!reader.IsScalar) return false;

        var expectedLength = expected.Length;
        // For exact match, length must be exact
        if (startsWith)
        {
            if (reader.ScalarLength() < expectedLength) return false;
        }
        else
        {
            if (!reader.ScalarLengthIs(expectedLength)) return false;
        }

        var bytes = reader.TryGetSpan(out var tmp) ? tmp : reader.Buffer(stackalloc byte[expected.BufferLength]);
        if (startsWith) bytes = bytes.Slice(0, expectedLength);
        return expected.IsCS(bytes);
    }

    /// <summary>Read a <c>ROLE</c> reply; null when the server described one this cannot model.</summary>
    /// <param name="reader">Positioned on the reply.</param>
    /// <remarks>
    /// <b>Internal and static so both cores read it the same way</b>, as the latency and slow-log
    /// walks are: the shipped processor below and the context surface's handler are two callers of
    /// this one function. A null reply, a non-aggregate, an empty one or a non-scalar first element
    /// all answer null, which is the shipped behaviour - <c>ROLE</c> is how a client asks what a
    /// server IS, and inventing an answer for an unreadable reply would be worse than none.
    /// </remarks>
    internal static Role? ParseRole(ref RespReader reader)
    {
        // Null, non-aggregate, empty, or non-scalar first element returns null Role
        if (!(reader.IsAggregate && !reader.IsNull && reader.TryMoveNext() && reader.IsScalar))
        {
            return null;
        }

        RoleType roleType;
        unsafe
        {
            if (!reader.TryParseScalar(&RoleTypeMetadata.TryParse, out roleType))
            {
                roleType = RoleType.Unknown;
            }
        }

        return roleType switch
        {
            RoleType.Master => ParsePrimary(ref reader),
            RoleType.Slave => ParseReplica(ref reader, "slave"),
            RoleType.Replica => ParseReplica(ref reader, "replica"),
            RoleType.Sentinel => ParseSentinel(ref reader),
            _ => new Role.Unknown(reader.ReadString()!),
        };
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
    /// is what <c>MultiStreamProcessor</c> has always tested. Keeping it a parameter means the
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

    /// <summary>Read a multi-stream <c>XREAD</c>/<c>XREADGROUP</c> reply.</summary>
    /// <param name="reader">The reader, positioned on the reply root.</param>
    /// <param name="isMap">Whether the root is a map, which is how RESP3 spells this.</param>
    /// <param name="allowJaggedFields">Whether an entry's fields may arrive as nested pairs.</param>
    /// <remarks>
    /// <para><inheritdoc cref="ParseStreamWithNameSkip" path="/remarks/para[2]"/></para>
    /// <para>
    /// The multi-stream twin of <see cref="ParseStreamWithNameSkip"/>, and it exists for the same
    /// reason: <c>MultiStreamProcessor</c> had this walk inlined in its
    /// <c>SetResultCore</c>, where a reply object cannot reach it. Both now call this, so the
    /// deferred and materialising shapes stay two call sites of one parse.
    /// </para>
    /// </remarks>
    internal static RedisStream[] ParseRedisStreams(ref RespReader reader, bool isMap, bool allowJaggedFields)
    {
        // nothing for any requested stream; the server answers nil rather than an empty aggregate
        if (reader.IsNull || !reader.IsAggregate) return [];

        if (isMap)
        {
            // a map: name, entries, name, entries - the names are children, not wrappers
            var count = reader.AggregateLength() >> 1;
            if (count == 0)
            {
                reader.SkipChildren();
                return [];
            }

            var result = new RedisStream[count];
            var mapIter = reader.AggregateChildren();
            for (var i = 0; i < count; i++)
            {
                mapIter.DemandNext();
                var key = mapIter.Value.ReadRedisKey();
                mapIter.DemandNext();
                result[i] = new RedisStream(key, ParseRedisStreamEntries(ref mapIter.Value, allowJaggedFields));
            }
            return result;
        }

        // an array of [name, entries] pairs
        return reader.ReadPastArray(
            ref allowJaggedFields,
            static (ref allowJaggedFields, ref itemReader) =>
            {
                var streamIter = itemReader.AggregateChildren();
                streamIter.DemandNext();
                var key = streamIter.Value.ReadRedisKey();
                streamIter.DemandNext();
                return new RedisStream(key, ParseRedisStreamEntries(ref streamIter.Value, allowJaggedFields));
            },
            scalar: false) ?? [];
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

    /// <summary>Read one <c>XINFO CONSUMERS</c> item; handed a reader positioned on that item.</summary>
    /// <remarks><inheritdoc cref="TryParseStreamPendingInfo" path="/remarks"/></remarks>
    internal static StreamConsumerInfo ParseStreamConsumerInfo(ref RespReader reader)
    {
            // Note: the caller passes a single consumer from the response into this method.

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

    /// <summary>Read one <c>XINFO GROUPS</c> item; handed a reader positioned on that item.</summary>
    /// <remarks><inheritdoc cref="TryParseStreamPendingInfo" path="/remarks"/></remarks>
    internal static StreamGroupInfo ParseStreamGroupInfo(ref RespReader reader)
    {
            // Note: the caller passes a single item from the response into this method.

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

    /// <summary>Read an <c>XINFO STREAM</c> reply; <see langword="false"/> if it is not that shape.</summary>
    /// <remarks><inheritdoc cref="TryParseStreamPendingInfo" path="/remarks"/></remarks>
    internal static bool TryParseStreamInfo(ref RespReader reader, bool allowJaggedFields, out StreamInfo value)
    {
            value = default;
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
                        firstEntry = ParseRedisStreamEntry(ref reader, allowJaggedFields);
                        break;
                    case StreamInfoField.LastEntry:
                        lastEntry = ParseRedisStreamEntry(ref reader, allowJaggedFields);
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

            value = new StreamInfo(
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

            return true;
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
        return NameValueEntryPairs.Instance.ParseArray(ref reader, allowJaggedFields, false, out _, null)!;
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
        => NameValueEntryPairs.Instance.AllowsJaggedPairs(protocol);

    internal static Role? ParsePrimary(ref RespReader reader)
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

    internal static Role? ParseReplica(ref RespReader reader, string role)
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

    internal static Role? ParseSentinel(ref RespReader reader)
    {
        // Expect: array of master names
        if (!(reader.TryMoveNext() && reader.IsAggregate))
        {
            return null;
        }

        var primaries = reader.ReadPastArray(static (ref r) => r.ReadString(), scalar: true);
        return new Role.Sentinel(primaries ?? []);
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
}
