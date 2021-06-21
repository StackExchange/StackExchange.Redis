using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(SharedConnectionFixture.Key)]
    public class Streams : TestBase
    {
        public Streams(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

        [Fact]
        public void IsStreamType()
        {
            using (var conn = Create())
            {
                var key = GetUniqueKey("type_check");

                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();
                db.StreamAdd(key, "field1", "value1");

                var keyType = db.KeyType(key);

                Assert.Equal(RedisType.Stream, keyType);
            }
        }

        [Fact]
        public void StreamAddSinglePairWithAutoId()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();
                var messageId = db.StreamAdd(GetUniqueKey("auto_id"), "field1", "value1");

                Assert.True(messageId != RedisValue.Null && ((string)messageId).Length > 0);
            }
        }

        [Fact]
        public void StreamAddMultipleValuePairsWithAutoId()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var key = GetUniqueKey("multiple_value_pairs");

                var fields = new []
                {
                    new NameValueEntry("field1", "value1"),
                    new NameValueEntry("field2", "value2")
                };

                var db = conn.GetDatabase();
                var messageId = db.StreamAdd(key, fields);

                var entries = db.StreamRange(key);

                Assert.Single(entries);
                Assert.Equal(messageId, entries[0].Id);
                Assert.Equal(2, entries[0].Values.Length);
                Assert.Equal("field1", entries[0].Values[0].Name);
                Assert.Equal("value1", entries[0].Values[0].Value);
                Assert.Equal("field2", entries[0].Values[1].Name);
                Assert.Equal("value2", entries[0].Values[1].Value);
            }
        }

        [Fact]
        public void StreamAddWithManualId()
        {
            const string id = "42-0";
            var key = GetUniqueKey("manual_id");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();
                var messageId = db.StreamAdd(key, "field1", "value1", id);

                Assert.Equal(id, messageId);
            }
        }

        [Fact]
        public void StreamAddMultipleValuePairsWithManualId()
        {
            const string id = "42-0";
            var key = GetUniqueKey("manual_id_multiple_values");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var fields = new []
                {
                    new NameValueEntry("field1", "value1"),
                    new NameValueEntry("field2", "value2")
                };

                var messageId = db.StreamAdd(key, fields, id);
                var entries = db.StreamRange(key);

                Assert.Equal(id, messageId);
                Assert.NotNull(entries);
                Assert.Single(entries);
                Assert.Equal(id, entries[0].Id);
            }
        }

        [Fact]
        public void StreamConsumerGroupSetId()
        {
            var key = GetUniqueKey("group_set_id");
            const string groupName = "test_group";
            const string consumer = "consumer";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Create a stream
                db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");

                // Create a group and set the position to deliver new messages only.
                db.StreamCreateConsumerGroup(key, groupName, StreamPosition.NewMessages);

                // Read into the group, expect nothing
                var firstRead = db.StreamReadGroup(key, groupName, consumer, StreamPosition.NewMessages);

                // Reset the ID back to read from the beginning.
                db.StreamConsumerGroupSetPosition(key, groupName, StreamPosition.Beginning);

                var secondRead = db.StreamReadGroup(key, groupName, consumer, StreamPosition.NewMessages);

                Assert.NotNull(firstRead);
                Assert.NotNull(secondRead);
                Assert.Empty(firstRead);
                Assert.Equal(2, secondRead.Length);
            }
        }

        [Fact]
        public void StreamConsumerGroupWithNoConsumers()
        {
            var key = GetUniqueKey("group_with_no_consumers");
            const string groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Create a stream
                db.StreamAdd(key, "field1", "value1");

                // Create a group
                db.StreamCreateConsumerGroup(key, groupName, "0-0");

                // Query redis for the group consumers, expect an empty list in response.
                var consumers = db.StreamConsumerInfo(key, groupName);

                Assert.Empty(consumers);
            }
        }

        [Fact]
        public void StreamCreateConsumerGroup()
        {
            var key = GetUniqueKey("group_create");
            const string groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Create a stream
                db.StreamAdd(key, "field1", "value1");

                // Create a group
                var result = db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);

                Assert.True(result);
            }
        }

        [Fact]
        public void StreamCreateConsumerGroupBeforeCreatingStream()
        {
            var key = GetUniqueKey("group_create_before_stream");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Ensure the key doesn't exist.
                var keyExistsBeforeCreate = db.KeyExists(key);

                // The 'createStream' parameter is 'true' by default.
                var groupCreated = db.StreamCreateConsumerGroup(key, "consumerGroup", StreamPosition.NewMessages);

                var keyExistsAfterCreate = db.KeyExists(key);

                Assert.False(keyExistsBeforeCreate);
                Assert.True(groupCreated);
                Assert.True(keyExistsAfterCreate);
            }
        }

        [Fact]
        public void StreamCreateConsumerGroupFailsIfKeyDoesntExist()
        {
            var key = GetUniqueKey("group_create_before_stream_should_fail");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Pass 'false' for 'createStream' to ensure that an
                // execption is thrown when the stream doesn't exist.
                Assert.ThrowsAny<RedisServerException>(() =>
                {
                    db.StreamCreateConsumerGroup(
                        key,
                        "consumerGroup",
                        StreamPosition.NewMessages,
                        createStream: false);
                });
            }
        }

        [Fact]
        public void StreamCreateConsumerGroupSucceedsWhenKeyExists()
        {
            var key = GetUniqueKey("group_create_after_stream");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key, "f1", "v1");

                // Pass 'false' for 'createStream', should create the consumer group
                // without issue since the stream already exists.
                var groupCreated = db.StreamCreateConsumerGroup(
                    key,
                    "consumerGroup",
                    StreamPosition.NewMessages,
                    createStream: false);

                Assert.True(groupCreated);
            }
        }

        [Fact]
        public void StreamConsumerGroupReadOnlyNewMessagesWithEmptyResponse()
        {
            var key = GetUniqueKey("group_read");
            const string groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Create a stream
                db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");

                // Create a group.
                db.StreamCreateConsumerGroup(key, groupName);

                // Read, expect no messages
                var entries = db.StreamReadGroup(key, groupName, "test_consumer", "0-0");

                Assert.Empty(entries);
            }
        }

        [Fact]
        public void StreamConsumerGroupReadFromStreamBeginning()
        {
            var key = GetUniqueKey("group_read_beginning");
            const string groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");

                db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);

                var entries = db.StreamReadGroup(key, groupName, "test_consumer", StreamPosition.NewMessages);

                Assert.Equal(2, entries.Length);
                Assert.True(id1 == entries[0].Id);
                Assert.True(id2 == entries[1].Id);
            }
        }

        [Fact]
        public void StreamConsumerGroupReadFromStreamBeginningWithCount()
        {
            var key = GetUniqueKey("group_read_with_count");
            const string groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                _ = db.StreamAdd(key, "field4", "value4");

                // Start reading after id1.
                db.StreamCreateConsumerGroup(key, groupName, id1);

                var entries = db.StreamReadGroup(key, groupName, "test_consumer", StreamPosition.NewMessages, 2);

                // Ensure we only received the requested count and that the IDs match the expected values.
                Assert.Equal(2, entries.Length);
                Assert.True(id2 == entries[0].Id);
                Assert.True(id3 == entries[1].Id);
            }
        }

        [Fact]
        public void StreamConsumerGroupAcknowledgeMessage()
        {
            var key = GetUniqueKey("group_ack");
            const string groupName = "test_group";
            const string consumer = "test_consumer";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);

                // Read all 4 messages, they will be assigned to the consumer
                var entries = db.StreamReadGroup(key, groupName, consumer, StreamPosition.NewMessages);

                // Send XACK for 3 of the messages

                // Single message Id overload.
                var oneAck = db.StreamAcknowledge(key, groupName, id1);

                // Multiple message Id overload.
                var twoAck = db.StreamAcknowledge(key, groupName, new [] { id3, id4 });

                // Read the group again, it should only return the unacknowledged message.
                var notAcknowledged = db.StreamReadGroup(key, groupName, consumer, "0-0");

                Assert.Equal(4, entries.Length);
                Assert.Equal(1, oneAck);
                Assert.Equal(2, twoAck);
                Assert.Single(notAcknowledged);
                Assert.Equal(id2, notAcknowledged[0].Id);
            }
        }

        [Fact]
        public void StreamConsumerGroupClaimMessages()
        {
            var key = GetUniqueKey("group_claim");
            const string groupName = "test_group";
            const string consumer1 = "test_consumer_1";
            const string consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                _ = db.StreamAdd(key, "field1", "value1");
                _ = db.StreamAdd(key, "field2", "value2");
                _ = db.StreamAdd(key, "field3", "value3");
                _ = db.StreamAdd(key, "field4", "value4");

                db.StreamCreateConsumerGroup(key, groupName, "0-0");

                // Read a single message into the first consumer.
                db.StreamReadGroup(key, groupName, consumer1, count: 1);

                // Read the remaining messages into the second consumer.
                db.StreamReadGroup(key, groupName, consumer2);

                // Claim the 3 messages consumed by consumer2 for consumer1.

                // Get the pending messages for consumer2.
                var pendingMessages = db.StreamPendingMessages(key, groupName,
                    10,
                    consumer2);

                // Claim the messages for consumer1.
                var messages = db.StreamClaim(key,
                                    groupName,
                                    consumer1,
                                    0, // Min message idle time
                                    messageIds: pendingMessages.Select(pm => pm.MessageId).ToArray());

                // Now see how many messages are pending for each consumer
                var pendingSummary = db.StreamPending(key, groupName);

                Assert.NotNull(pendingSummary.Consumers);
                Assert.Single(pendingSummary.Consumers);
                Assert.Equal(4, pendingSummary.Consumers[0].PendingMessageCount);
                Assert.Equal(pendingMessages.Length, messages.Length);
            }
        }

        [Fact]
        public void StreamConsumerGroupClaimMessagesReturningIds()
        {
            var key = GetUniqueKey("group_claim_view_ids");
            const string groupName = "test_group";
            const string consumer1 = "test_consumer_1";
            const string consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                _ = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);

                // Read a single message into the first consumer.
                _ = db.StreamReadGroup(key, groupName, consumer1, StreamPosition.NewMessages, 1);

                // Read the remaining messages into the second consumer.
                _ = db.StreamReadGroup(key, groupName, consumer2);

                // Claim the 3 messages consumed by consumer2 for consumer1.

                // Get the pending messages for consumer2.
                var pendingMessages = db.StreamPendingMessages(key, groupName,
                    10,
                    consumer2);

                // Claim the messages for consumer1.
                var messageIds = db.StreamClaimIdsOnly(key,
                                    groupName,
                                    consumer1,
                                    0, // Min message idle time
                                    messageIds: pendingMessages.Select(pm => pm.MessageId).ToArray());

                // We should get an array of 3 message IDs.
                Assert.Equal(3, messageIds.Length);
                Assert.Equal(id2, messageIds[0]);
                Assert.Equal(id3, messageIds[1]);
                Assert.Equal(id4, messageIds[2]);
            }
        }

        [Fact]
        public void StreamConsumerGroupReadMultipleOneReadBeginningOneReadNew()
        {
            // Create a group for each stream. One set to read from the beginning of the
            // stream and the other to begin reading only new messages.

            // Ask redis to read from the beginning of both stream, expect messages
            // for only the stream set to read from the beginning.

            const string groupName = "test_group";
            var stream1 = GetUniqueKey("stream1a");
            var stream2 = GetUniqueKey("stream2a");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(stream1, "field1-1", "value1-1");
                db.StreamAdd(stream1, "field1-2", "value1-2");

                db.StreamAdd(stream2, "field2-1", "value2-1");
                db.StreamAdd(stream2, "field2-2", "value2-2");
                db.StreamAdd(stream2, "field2-3", "value2-3");

                // stream1 set up to read only new messages.
                db.StreamCreateConsumerGroup(stream1, groupName, StreamPosition.NewMessages);

                // stream2 set up to read from the beginning of the stream
                db.StreamCreateConsumerGroup(stream2, groupName, StreamPosition.Beginning);

                // Read for both streams from the beginning. We shouldn't get anything back for stream1.
                var pairs = new []
                {
                    // StreamPosition.NewMessages will send ">" which indicates "Undelivered" messages.
                    new StreamPosition(stream1, StreamPosition.NewMessages),
                    new StreamPosition(stream2, StreamPosition.NewMessages)
                };

                var streams = db.StreamReadGroup(pairs, groupName, "test_consumer");

                Assert.NotNull(streams);
                Assert.Single(streams);
                Assert.Equal(stream2, streams[0].Key);
                Assert.Equal(3, streams[0].Entries.Length);
            }
        }

        [Fact]
        public void StreamConsumerGroupReadMultipleOnlyNewMessagesExpectNoResult()
        {
            const string groupName = "test_group";
            var stream1 = GetUniqueKey("stream1b");
            var stream2 = GetUniqueKey("stream2b");
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(stream1, "field1-1", "value1-1");
                db.StreamAdd(stream2, "field2-1", "value2-1");

                // set both streams to read only new messages (default behavior).
                db.StreamCreateConsumerGroup(stream1, groupName);
                db.StreamCreateConsumerGroup(stream2, groupName);

                // We shouldn't get anything for either stream.
                var pairs = new []
                {
                    new StreamPosition(stream1, StreamPosition.Beginning),
                    new StreamPosition(stream2, StreamPosition.Beginning)
                };

                var streams = db.StreamReadGroup(pairs, groupName, "test_consumer");

                Assert.NotNull(streams);
                Assert.Equal(2, streams.Length);
                Assert.Empty(streams[0].Entries);
                Assert.Empty(streams[1].Entries);
            }
        }

        [Fact]
        public void StreamConsumerGroupReadMultipleOnlyNewMessagesExpect1Result()
        {
            const string groupName = "test_group";
            var stream1 = GetUniqueKey("stream1c");
            var stream2 = GetUniqueKey("stream2c");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // These messages won't be read.
                db.StreamAdd(stream1, "field1-1", "value1-1");
                db.StreamAdd(stream2, "field2-1", "value2-1");

                // set both streams to read only new messages (default behavior).
                db.StreamCreateConsumerGroup(stream1, groupName);
                db.StreamCreateConsumerGroup(stream2, groupName);

                // We should read these though.
                var id1 = db.StreamAdd(stream1, "field1-2", "value1-2");
                var id2 = db.StreamAdd(stream2, "field2-2", "value2-2");

                // Read the new messages (messages created after the group was created).
                var pairs = new []
                {
                    new StreamPosition(stream1, StreamPosition.NewMessages),
                    new StreamPosition(stream2, StreamPosition.NewMessages)
                };

                var streams = db.StreamReadGroup(pairs, groupName, "test_consumer");

                Assert.NotNull(streams);
                Assert.Equal(2, streams.Length);
                Assert.Single(streams[0].Entries);
                Assert.Single(streams[1].Entries);
                Assert.Equal(id1, streams[0].Entries[0].Id);
                Assert.Equal(id2, streams[1].Entries[0].Id);
            }
        }

        [Fact]
        public void StreamConsumerGroupReadMultipleRestrictCount()
        {
            const string groupName = "test_group";
            var stream1 = GetUniqueKey("stream1d");
            var stream2 = GetUniqueKey("stream2d");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1_1 = db.StreamAdd(stream1, "field1-1", "value1-1");
                var id1_2 = db.StreamAdd(stream1, "field1-2", "value1-2");

                var id2_1 = db.StreamAdd(stream2, "field2-1", "value2-1");
                _ = db.StreamAdd(stream2, "field2-2", "value2-2");
                _ = db.StreamAdd(stream2, "field2-3", "value2-3");

                // Set the initial read point in each stream, *after* the first ID in both streams.
                db.StreamCreateConsumerGroup(stream1, groupName, id1_1);
                db.StreamCreateConsumerGroup(stream2, groupName, id2_1);

                var pairs = new []
                {
                    // Read after the first id in both streams
                    new StreamPosition(stream1, StreamPosition.NewMessages),
                    new StreamPosition(stream2, StreamPosition.NewMessages)
                };

                // Restrict the count to 2 (expect only 1 message from first stream, 2 from the second).
                var streams = db.StreamReadGroup(pairs, groupName, "test_consumer", 2);

                Assert.NotNull(streams);
                Assert.Equal(2, streams.Length);
                Assert.Single(streams[0].Entries);
                Assert.Equal(2, streams[1].Entries.Length);
                Assert.Equal(id1_2, streams[0].Entries[0].Id);
            }
        }

        [Fact]
        public void StreamConsumerGroupViewPendingInfoNoConsumers()
        {
            var key = GetUniqueKey("group_pending_info_no_consumers");
            const string groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key, "field1", "value1");

                db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);

                var pendingInfo = db.StreamPending(key, groupName);

                Assert.Equal(0, pendingInfo.PendingMessageCount);
                Assert.Equal(RedisValue.Null, pendingInfo.LowestPendingMessageId);
                Assert.Equal(RedisValue.Null, pendingInfo.HighestPendingMessageId);
                Assert.NotNull(pendingInfo.Consumers);
                Assert.Empty(pendingInfo.Consumers);
            }
        }

        [Fact]
        public void StreamConsumerGroupViewPendingInfoWhenNothingPending()
        {
            var key = GetUniqueKey("group_pending_info_nothing_pending");
            const string groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key, "field1", "value1");

                db.StreamCreateConsumerGroup(key, groupName, "0-0");

                var pendingMessages = db.StreamPendingMessages(key,
                    groupName,
                    10,
                    consumerName: RedisValue.Null);

                Assert.NotNull(pendingMessages);
                Assert.Empty(pendingMessages);
            }
        }

        [Fact]
        public void StreamConsumerGroupViewPendingInfoSummary()
        {
            var key = GetUniqueKey("group_pending_info");
            const string groupName = "test_group";
            const string consumer1 = "test_consumer_1";
            const string consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");
                db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);

                // Read a single message into the first consumer.
                db.StreamReadGroup(key, groupName, consumer1, StreamPosition.NewMessages, 1);

                // Read the remaining messages into the second consumer.
                db.StreamReadGroup(key, groupName, consumer2);

                var pendingInfo = db.StreamPending(key, groupName);

                Assert.Equal(4, pendingInfo.PendingMessageCount);
                Assert.Equal(id1, pendingInfo.LowestPendingMessageId);
                Assert.Equal(id4, pendingInfo.HighestPendingMessageId);
                Assert.True(pendingInfo.Consumers.Length == 2);

                var consumer1Count = pendingInfo.Consumers.First(c => c.Name == consumer1).PendingMessageCount;
                var consumer2Count = pendingInfo.Consumers.First(c => c.Name == consumer2).PendingMessageCount;

                Assert.Equal(1, consumer1Count);
                Assert.Equal(3, consumer2Count);
            }
        }

        [Fact]
        public async Task StreamConsumerGroupViewPendingMessageInfo()
        {
            var key = GetUniqueKey("group_pending_messages");
            const string groupName = "test_group";
            const string consumer1 = "test_consumer_1";
            const string consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");
                db.StreamAdd(key, "field3", "value3");
                db.StreamAdd(key, "field4", "value4");

                db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);

                // Read a single message into the first consumer.
                db.StreamReadGroup(key, groupName, consumer1, count: 1);

                // Read the remaining messages into the second consumer.
                _ = db.StreamReadGroup(key, groupName, consumer2) ?? throw new ArgumentNullException(nameof(consumer2), "db.StreamReadGroup(key, groupName, consumer2)");

                await Task.Delay(10).ForAwait();

                // Get the pending info about the messages themselves.
                var pendingMessageInfoList = db.StreamPendingMessages(key, groupName, 10, RedisValue.Null);

                Assert.NotNull(pendingMessageInfoList);
                Assert.Equal(4, pendingMessageInfoList.Length);
                Assert.Equal(consumer1, pendingMessageInfoList[0].ConsumerName);
                Assert.Equal(1, pendingMessageInfoList[0].DeliveryCount);
                Assert.True((int)pendingMessageInfoList[0].IdleTimeInMilliseconds > 0);
                Assert.Equal(id1, pendingMessageInfoList[0].MessageId);
            }
        }

        [Fact]
        public void StreamConsumerGroupViewPendingMessageInfoForConsumer()
        {
            var key = GetUniqueKey("group_pending_for_consumer");
            const string groupName = "test_group";
            const string consumer1 = "test_consumer_1";
            const string consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");
                db.StreamAdd(key, "field3", "value3");
                db.StreamAdd(key, "field4", "value4");

                db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);

                // Read a single message into the first consumer.
                db.StreamReadGroup(key, groupName, consumer1, count: 1);

                // Read the remaining messages into the second consumer.
                db.StreamReadGroup(key, groupName, consumer2);

                // Get the pending info about the messages themselves.
                var pendingMessageInfoList = db.StreamPendingMessages(key,
                    groupName,
                    10,
                    consumer2);

                Assert.NotNull(pendingMessageInfoList);
                Assert.Equal(3, pendingMessageInfoList.Length);
            }
        }

        [Fact]
        public void StreamDeleteConsumer()
        {
            var key = GetUniqueKey("delete_consumer");
            const string groupName = "test_group";
            const string consumer = "test_consumer";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Add a message to create the stream.
                db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");

                // Create a consumer group and read the message.
                db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);
                db.StreamReadGroup(key, groupName, consumer, StreamPosition.NewMessages);

                var preDeleteConsumers = db.StreamConsumerInfo(key, groupName);

                // Delete the consumer.
                var deleteResult = db.StreamDeleteConsumer(key, groupName, consumer);

                // Should get 2 messages in the deleteResult.
                var postDeleteConsumers = db.StreamConsumerInfo(key, groupName);

                Assert.Equal(2, deleteResult);
                Assert.Single(preDeleteConsumers);
                Assert.Empty(postDeleteConsumers);
            }
        }

        [Fact]
        public void StreamDeleteConsumerGroup()
        {
            var key = GetUniqueKey("delete_consumer_group");
            const string groupName = "test_group";
            const string consumer = "test_consumer";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Add a message to create the stream.
                db.StreamAdd(key, "field1", "value1");

                // Create a consumer group and read the messages.
                db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);
                db.StreamReadGroup(key, groupName, consumer, StreamPosition.Beginning);

                var preDeleteInfo = db.StreamInfo(key);

                // Now delete the group.
                var deleteResult = db.StreamDeleteConsumerGroup(key, groupName);

                var postDeleteInfo = db.StreamInfo(key);

                Assert.True(deleteResult);
                Assert.Equal(1, preDeleteInfo.ConsumerGroupCount);
                Assert.Equal(0, postDeleteInfo.ConsumerGroupCount);
            }
        }

        [Fact]
        public void StreamDeleteMessage()
        {
            var key = GetUniqueKey("delete_msg");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                db.StreamAdd(key, "field4", "value4");

                var deletedCount = db.StreamDelete(key, new [] { id3 });
                var messages = db.StreamRange(key);

                Assert.Equal(1, deletedCount);
                Assert.Equal(3, messages.Length);
            }
        }

        [Fact]
        public void StreamDeleteMessages()
        {
            var key = GetUniqueKey("delete_msgs");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                db.StreamAdd(key, "field4", "value4");

                var deletedCount = db.StreamDelete(key, new [] { id2, id3 }, CommandFlags.None);
                var messages = db.StreamRange(key);

                Assert.Equal(2, deletedCount);
                Assert.Equal(2, messages.Length);
            }
        }

        [Fact]
        public void StreamGroupInfoGet()
        {
            var key = GetUniqueKey("group_info");
            const string group1 = "test_group_1";
            const string group2 = "test_group_2";
            const string consumer1 = "test_consumer_1";
            const string consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();
                db.KeyDelete(key);

                db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");
                db.StreamAdd(key, "field3", "value3");
                db.StreamAdd(key, "field4", "value4");

                db.StreamCreateConsumerGroup(key, group1, StreamPosition.Beginning);
                db.StreamCreateConsumerGroup(key, group2, StreamPosition.Beginning);

                // Read a single message into the first consumer.
                db.StreamReadGroup(key, group1, consumer1, count: 1);

                // Read the remaining messages into the second consumer.
                db.StreamReadGroup(key, group2, consumer2);

                var groupInfoList = db.StreamGroupInfo(key);

                Assert.NotNull(groupInfoList);
                Assert.Equal(2, groupInfoList.Length);

                Assert.Equal(group1, groupInfoList[0].Name);
                Assert.Equal(1, groupInfoList[0].PendingMessageCount);
                Assert.True(IsMessageId(groupInfoList[0].LastDeliveredId)); // can't test actual - will vary

                Assert.Equal(group2, groupInfoList[1].Name);
                Assert.Equal(4, groupInfoList[1].PendingMessageCount);
                Assert.True(IsMessageId(groupInfoList[1].LastDeliveredId)); // can't test actual - will vary
            }

            static bool IsMessageId(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return false;
                return value.Length >= 3 && value.Contains("-");
            }
        }

        [Fact]
        public void StreamGroupConsumerInfoGet()
        {
            var key = GetUniqueKey("group_consumer_info");
            const string group = "test_group";
            const string consumer1 = "test_consumer_1";
            const string consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");
                db.StreamAdd(key, "field3", "value3");
                db.StreamAdd(key, "field4", "value4");

                db.StreamCreateConsumerGroup(key, group, StreamPosition.Beginning);
                db.StreamReadGroup(key, group, consumer1, count: 1);
                db.StreamReadGroup(key, group, consumer2);

                var consumerInfoList = db.StreamConsumerInfo(key, group);

                Assert.NotNull(consumerInfoList);
                Assert.Equal(2, consumerInfoList.Length);

                Assert.Equal(consumer1, consumerInfoList[0].Name);
                Assert.Equal(consumer2, consumerInfoList[1].Name);

                Assert.Equal(1, consumerInfoList[0].PendingMessageCount);
                Assert.Equal(3, consumerInfoList[1].PendingMessageCount);
            }
        }

        [Fact]
        public void StreamInfoGet()
        {
            var key = GetUniqueKey("stream_info");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");
                db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                var streamInfo = db.StreamInfo(key);

                Assert.Equal(4, streamInfo.Length);
                Assert.True(streamInfo.RadixTreeKeys > 0);
                Assert.True(streamInfo.RadixTreeNodes > 0);
                Assert.Equal(id1, streamInfo.FirstEntry.Id);
                Assert.Equal(id4, streamInfo.LastEntry.Id);
            }
        }

        [Fact]
        public void StreamInfoGetWithEmptyStream()
        {
            var key = GetUniqueKey("stream_info_empty");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Add an entry and then delete it so the stream is empty, then run streaminfo
                // to ensure it functions properly on an empty stream. Namely, the first-entry
                // and last-entry messages should be null.

                var id = db.StreamAdd(key, "field1", "value1");
                db.StreamDelete(key, new [] { id });

                Assert.Equal(0, db.StreamLength(key));

                var streamInfo = db.StreamInfo(key);

                Assert.True(streamInfo.FirstEntry.IsNull);
                Assert.True(streamInfo.LastEntry.IsNull);
            }
        }

        [Fact]
        public void StreamNoConsumerGroups()
        {
            var key = GetUniqueKey("stream_with_no_consumers");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key, "field1", "value1");

                var groups = db.StreamGroupInfo(key);

                Assert.NotNull(groups);
                Assert.Empty(groups);
            }
        }

        [Fact]
        public void StreamPendingNoMessagesOrConsumers()
        {
            var key = GetUniqueKey("stream_pending_empty");
            const string groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id = db.StreamAdd(key, "field1", "value1");
                db.StreamDelete(key, new [] { id });

                db.StreamCreateConsumerGroup(key, groupName, "0-0");

                var pendingInfo = db.StreamPending(key, "test_group");

                Assert.Equal(0, pendingInfo.PendingMessageCount);
                Assert.Equal(RedisValue.Null, pendingInfo.LowestPendingMessageId);
                Assert.Equal(RedisValue.Null, pendingInfo.HighestPendingMessageId);
                Assert.NotNull(pendingInfo.Consumers);
                Assert.Empty(pendingInfo.Consumers);
            }
        }

        [Fact]
        public void StreamPositionDefaultValueIsBeginning()
        {
            RedisValue position = StreamPosition.Beginning;
            Assert.Equal(StreamConstants.AllMessages, StreamPosition.Resolve(position, RedisCommand.XREAD));
            Assert.Equal(StreamConstants.AllMessages, StreamPosition.Resolve(position, RedisCommand.XREADGROUP));
            Assert.Equal(StreamConstants.AllMessages, StreamPosition.Resolve(position, RedisCommand.XGROUP));
        }

        [Fact]
        public void StreamPositionValidateBeginning()
        {
            var position = StreamPosition.Beginning;

            Assert.Equal(StreamConstants.AllMessages, StreamPosition.Resolve(position, RedisCommand.XREAD));
        }

        [Fact]
        public void StreamPositionValidateExplicit()
        {
            const string explicitValue = "1-0";
            const string position = explicitValue;

            Assert.Equal(explicitValue, StreamPosition.Resolve(position, RedisCommand.XREAD));
        }

        [Fact]
        public void StreamPositionValidateNew()
        {
            var position = StreamPosition.NewMessages;

            Assert.Equal(StreamConstants.NewMessages, StreamPosition.Resolve(position, RedisCommand.XGROUP));
            Assert.Equal(StreamConstants.UndeliveredMessages, StreamPosition.Resolve(position, RedisCommand.XREADGROUP));
            Assert.ThrowsAny<InvalidOperationException>(() => StreamPosition.Resolve(position, RedisCommand.XREAD));
        }

        [Fact]
        public void StreamRead()
        {
            var key = GetUniqueKey("read");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");

                // Read the entire stream from the beginning.
                var entries = db.StreamRead(key, "0-0");

                Assert.Equal(3, entries.Length);
                Assert.Equal(id1, entries[0].Id);
                Assert.Equal(id2, entries[1].Id);
                Assert.Equal(id3, entries[2].Id);
            }
        }

        [Fact]
        public void StreamReadEmptyStream()
        {
            var key = GetUniqueKey("read_empty_stream");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Write to a stream to create the key.
                var id1 = db.StreamAdd(key, "field1", "value1");

                // Delete the key to empty the stream.
                db.StreamDelete(key, new [] { id1 });
                var len = db.StreamLength(key);

                // Read the entire stream from the beginning.
                var entries = db.StreamRead(key, "0-0");

                Assert.Empty(entries);
                Assert.Equal(0, len);
            }
        }

        [Fact]
        public void StreamReadEmptyStreams()
        {
            var key1 = GetUniqueKey("read_empty_stream_1");
            var key2 = GetUniqueKey("read_empty_stream_2");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Write to a stream to create the key.
                var id1 = db.StreamAdd(key1, "field1", "value1");
                var id2 = db.StreamAdd(key2, "field2", "value2");

                // Delete the key to empty the stream.
                db.StreamDelete(key1, new [] { id1 });
                db.StreamDelete(key2, new [] { id2 });

                var len1 = db.StreamLength(key1);
                var len2 = db.StreamLength(key2);

                // Read the entire stream from the beginning.
                var entries1 = db.StreamRead(key1, "0-0");
                var entries2 = db.StreamRead(key2, "0-0");

                Assert.Empty(entries1);
                Assert.Empty(entries2);

                Assert.Equal(0, len1);
                Assert.Equal(0, len2);
            }
        }

        [Fact]
        public void StreamReadExpectedExceptionInvalidCountMultipleStream()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var streamPositions = new []
                {
                    new StreamPosition("key1", "0-0"),
                    new StreamPosition("key2", "0-0")
                };

                var db = conn.GetDatabase();
                Assert.Throws<ArgumentOutOfRangeException>(() => db.StreamRead(streamPositions, 0));
            }
        }

        [Fact]
        public void StreamReadExpectedExceptionInvalidCountSingleStream()
        {
            var key = GetUniqueKey("read_exception_invalid_count_single");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();
                Assert.Throws<ArgumentOutOfRangeException>(() => db.StreamRead(key, "0-0", 0));
            }
        }

        [Fact]
        public void StreamReadExpectedExceptionNullStreamList()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();
                Assert.Throws<ArgumentNullException>(() => db.StreamRead(null));
            }
        }

        [Fact]
        public void StreamReadExpectedExceptionEmptyStreamList()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var emptyList = new StreamPosition[0];

                Assert.Throws<ArgumentOutOfRangeException>(() => db.StreamRead(emptyList));
            }
        }

        [Fact]
        public void StreamReadMultipleStreams()
        {
            var key1 = GetUniqueKey("read_multi_1a");
            var key2 = GetUniqueKey("read_multi_2a");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key1, "field1", "value1");
                var id2 = db.StreamAdd(key1, "field2", "value2");
                var id3 = db.StreamAdd(key2, "field3", "value3");
                var id4 = db.StreamAdd(key2, "field4", "value4");

                // Read from both streams at the same time.
                var streamList = new []
                {
                    new StreamPosition(key1, "0-0"),
                    new StreamPosition(key2, "0-0")
                };

                var streams = db.StreamRead(streamList);

                Assert.True(streams.Length == 2);

                Assert.Equal(key1, streams[0].Key);
                Assert.Equal(2, streams[0].Entries.Length);
                Assert.Equal(id1, streams[0].Entries[0].Id);
                Assert.Equal(id2, streams[0].Entries[1].Id);

                Assert.Equal(key2, streams[1].Key);
                Assert.Equal(2, streams[1].Entries.Length);
                Assert.Equal(id3, streams[1].Entries[0].Id);
                Assert.Equal(id4, streams[1].Entries[1].Id);
            }
        }

        [Fact]
        public void StreamReadMultipleStreamsWithCount()
        {
            var key1 = GetUniqueKey("read_multi_count_1");
            var key2 = GetUniqueKey("read_multi_count_2");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key1, "field1", "value1");
                db.StreamAdd(key1, "field2", "value2");
                var id3 = db.StreamAdd(key2, "field3", "value3");
                db.StreamAdd(key2, "field4", "value4");

                var streamList = new []
                {
                    new StreamPosition(key1, "0-0"),
                    new StreamPosition(key2, "0-0")
                };

                var streams = db.StreamRead(streamList, countPerStream: 1);

                // We should get both streams back.
                Assert.Equal(2, streams.Length);

                // Ensure we only got one message per stream.
                Assert.Single(streams[0].Entries);
                Assert.Single(streams[1].Entries);

                // Check the message IDs as well.
                Assert.Equal(id1, streams[0].Entries[0].Id);
                Assert.Equal(id3, streams[1].Entries[0].Id);
            }
        }

        [Fact]
        public void StreamReadMultipleStreamsWithReadPastSecondStream()
        {
            var key1 = GetUniqueKey("read_multi_1b");
            var key2 = GetUniqueKey("read_multi_2b");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key1, "field1", "value1");
                db.StreamAdd(key1, "field2", "value2");
                db.StreamAdd(key2, "field3", "value3");
                var id4 = db.StreamAdd(key2, "field4", "value4");

                var streamList = new []
                {
                    new StreamPosition(key1, "0-0"),

                    // read past the end of stream # 2
                    new StreamPosition(key2, id4)
                };

                var streams = db.StreamRead(streamList);

                // We should only get the first stream back.
                Assert.Single(streams);

                Assert.Equal(key1, streams[0].Key);
                Assert.Equal(2, streams[0].Entries.Length);
            }
        }

        [Fact]
        public void StreamReadMultipleStreamsWithEmptyResponse()
        {
            var key1 = GetUniqueKey("read_multi_1c");
            var key2 = GetUniqueKey("read_multi_2c");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key1, "field1", "value1");
                var id2 = db.StreamAdd(key1, "field2", "value2");
                db.StreamAdd(key2, "field3", "value3");
                var id4 = db.StreamAdd(key2, "field4", "value4");

                var streamList = new []
                {
                    // Read past the end of both streams.
                    new StreamPosition(key1, id2),
                    new StreamPosition(key2, id4)
                };

                var streams = db.StreamRead(streamList);

                // We expect an empty response.
                Assert.Empty(streams);
            }
        }

        [Fact]
        public void StreamReadPastEndOfStream()
        {
            var key = GetUniqueKey("read_empty");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");

                // Read after the final ID in the stream, we expect an empty array as a response.

                var entries = db.StreamRead(key, id2);

                Assert.Empty(entries);
            }
        }

        [Fact]
        public void StreamReadRange()
        {
            var key = GetUniqueKey("range");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");

                var entries = db.StreamRange(key);

                Assert.Equal(2, entries.Length);
                Assert.Equal(id1, entries[0].Id);
                Assert.Equal(id2, entries[1].Id);
            }
        }

        [Fact]
        public void StreamReadRangeOfEmptyStream()
        {
            var key = GetUniqueKey("range_empty");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");

                var deleted = db.StreamDelete(key, new [] { id1, id2 });

                var entries = db.StreamRange(key);

                Assert.Equal(2, deleted);
                Assert.NotNull(entries);
                Assert.Empty(entries);
            }
        }

        [Fact]
        public void StreamReadRangeWithCount()
        {
            var key = GetUniqueKey("range_count");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");

                var entries = db.StreamRange(key, count: 1);

                Assert.Single(entries);
                Assert.Equal(id1, entries[0].Id);
            }
        }

        [Fact]
        public void StreamReadRangeReverse()
        {
            var key = GetUniqueKey("rangerev");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");

                var entries = db.StreamRange(key, messageOrder: Order.Descending);

                Assert.Equal(2, entries.Length);
                Assert.Equal(id2, entries[0].Id);
                Assert.Equal(id1, entries[1].Id);
            }
        }

        [Fact]
        public void StreamReadRangeReverseWithCount()
        {
            var key = GetUniqueKey("rangerev_count");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");

                var entries = db.StreamRange(key, id1, id2, 1, Order.Descending);

                Assert.Single(entries);
                Assert.Equal(id2, entries[0].Id);
            }
        }

        [Fact]
        public void StreamReadWithAfterIdAndCount_1()
        {
            var key = GetUniqueKey("read1");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                db.StreamAdd(key, "field3", "value3");

                // Only read a single item from the stream.
                var entries = db.StreamRead(key, id1, 1);

                Assert.Single(entries);
                Assert.Equal(id2, entries[0].Id);
            }
        }

        [Fact]
        public void StreamReadWithAfterIdAndCount_2()
        {
            var key = GetUniqueKey("read2");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                db.StreamAdd(key, "field4", "value4");

                // Read multiple items from the stream.
                var entries = db.StreamRead(key, id1, 2);

                Assert.Equal(2, entries.Length);
                Assert.Equal(id2, entries[0].Id);
                Assert.Equal(id3, entries[1].Id);
            }
        }

        [Fact]
        public void StreamTrimLength()
        {
            var key = GetUniqueKey("trimlen");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Add a couple items and check length.
                db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");
                db.StreamAdd(key, "field3", "value3");
                db.StreamAdd(key, "field4", "value4");

                var numRemoved = db.StreamTrim(key, 1);
                var len = db.StreamLength(key);

                Assert.Equal(3, numRemoved);
                Assert.Equal(1, len);
            }
        }

        [Fact]
        public void StreamVerifyLength()
        {
            var key = GetUniqueKey("len");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Add a couple items and check length.
                db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");

                var len = db.StreamLength(key);

                Assert.Equal(2, len);
            }
        }

        [Fact]
        public async Task AddWithApproxCountAsync()
        {
            var key = GetUniqueKey("approx-async");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();
                await db.StreamAddAsync(key, "field", "value", maxLength: 10, useApproximateMaxLength: true, flags: CommandFlags.None).ConfigureAwait(false);
            }
        }

        [Fact]
        public void AddWithApproxCount()
        {
            var key = GetUniqueKey("approx");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();
                db.StreamAdd(key, "field", "value", maxLength: 10, useApproximateMaxLength: true, flags: CommandFlags.None);
            }
        }

        [Fact]
        public void StreamReadGroupWithNoAckShowsNoPendingMessages()
        {
            var key = GetUniqueKey("read_group_noack");
            const string groupName = "test_group";
            const string consumer = "consumer";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");

                db.StreamCreateConsumerGroup(key, groupName, StreamPosition.NewMessages);

                db.StreamReadGroup(key,
                    groupName,
                    consumer,
                    StreamPosition.NewMessages,
                    noAck: true);

                var pendingInfo = db.StreamPending(key, groupName);

                Assert.Equal(0, pendingInfo.PendingMessageCount);
            }
        }

        [Fact]
        public void StreamReadGroupMultiStreamWithNoAckShowsNoPendingMessages()
        {
            var key1 = GetUniqueKey("read_group_noack1");
            var key2 = GetUniqueKey("read_group_noack2");
            const string groupName = "test_group";
            const string consumer = "consumer";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                db.StreamAdd(key1, "field1", "value1");
                db.StreamAdd(key1, "field2", "value2");

                db.StreamAdd(key2, "field3", "value3");
                db.StreamAdd(key2, "field4", "value4");

                db.StreamCreateConsumerGroup(key1, groupName, StreamPosition.NewMessages);
                db.StreamCreateConsumerGroup(key2, groupName, StreamPosition.NewMessages);

                db.StreamReadGroup(
                    new []
                    {
                        new StreamPosition(key1, StreamPosition.NewMessages),
                        new StreamPosition(key2, StreamPosition.NewMessages)
                    },
                    groupName,
                    consumer,
                    noAck: true);

                var pending1 = db.StreamPending(key1, groupName);
                var pending2 = db.StreamPending(key2, groupName);

                Assert.Equal(0, pending1.PendingMessageCount);
                Assert.Equal(0, pending2.PendingMessageCount);
            }
        }

        private RedisKey GetUniqueKey(string type) => $"{type}_stream_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";


        [Fact]
        public async Task StreamReadIndexerUsage()
        {
            var streamName = GetUniqueKey("read-group-indexer");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                await db.StreamAddAsync(streamName, new[] {
                    new NameValueEntry("x", "blah"),
                    new NameValueEntry("msg", @"{""name"":""test"",""id"":123}"),
                    new NameValueEntry("y", "more blah"),
                });

                var streamResult = await db.StreamRangeAsync(streamName, count: 1000);
                var evntJson = streamResult
                    .Select(x => (dynamic)JsonConvert.DeserializeObject(x["msg"]))
                    .ToList();
                var obj = Assert.Single(evntJson);
                Assert.Equal(123, (int)obj.id);
                Assert.Equal("test", (string)obj.name);
            }
        }

    }
}
