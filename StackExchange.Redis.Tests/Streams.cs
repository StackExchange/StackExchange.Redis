using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Streams : TestBase
    {
        public Streams(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void StreamAddSinglePairWithAutoId()
        {
            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();
                var messageId = db.StreamAdd(GetUniqueKey("auto_id"), "field1", "value1");

                Assert.True(messageId != null && messageId.Length > 0);
            }
        }

        [Fact]
        public void StreamAddMultipleValuePairsWithManualId()
        {
            var id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-0";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var key = GetUniqueKey("manual_id");

                var fields = new NameValueEntry[2]
                {
                    new NameValueEntry("field1", "value1"),
                    new NameValueEntry("field2", "value2")
                };

                var db = conn.GetDatabase();
                var messageId = db.StreamAdd(key, fields);

                var entries = db.StreamRange(key, StreamConstants.StreamMinValue, StreamConstants.StreamMaxValue);

                Assert.True(entries.Length == 1);
                Assert.Equal(messageId, entries[0].Id);
                Assert.True(entries[0].Values.Length == 2);
                Assert.True(entries[0].Values[0].Name == "field1" &&
                             entries[0].Values[0].Value == "value1");
                Assert.True(entries[0].Values[1].Name == "field2" &&
                             entries[0].Values[1].Value == "value2");
            }
        }

        [Fact]
        public void StreamAddWithManualId()
        {
            var id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-0";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();
                var messageId = db.StreamAdd(GetUniqueKey("manual_id"), id, "field1", "value1");

                Assert.Equal(id, messageId);
            }
        }

        [Fact]
        public void StreamCreateConsumerGroup()
        {
            var key = GetUniqueKey("group_create");
            var groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Create a stream
                db.StreamAdd(key, "field1", "value1");

                // Create a group
                var result = db.StreamCreateConsumerGroup(key, groupName, StreamConstants.StreamMinValue);

                Assert.True(result);
            }
        }

        [Fact]
        public void StreamConsumerGroupReadOnlyNewMessagesWithEmptyResponse()
        {
            var key = GetUniqueKey("group_read");
            var groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Create a stream
                db.StreamAdd(key, "field1", "value1");
                db.StreamAdd(key, "field2", "value2");

                // Create a group.
                db.StreamCreateConsumerGroup(key, groupName, StreamConstants.NewMessages);

                // Read, expect no messages
                var entries = db.StreamReadGroup(key, groupName, "test_consumer", "0-0");

                Assert.True(entries.Length == 0);
            }
        }

        [Fact]
        public void StreamConsumerGroupReadFromStreamBeginning()
        {
            var key = GetUniqueKey("group_read_beginning");
            var groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");

                db.StreamCreateConsumerGroup(key, groupName, StreamConstants.StreamMinValue);

                var entries = db.StreamReadGroup(key, groupName, "test_consumer", "0-0");

                Assert.True(entries.Length == 2);
                Assert.True(id1 == entries[0].Id);
                Assert.True(id2 == entries[1].Id);
            }
        }

        [Fact]
        public void StreamConsumerGroupReadFromStreamBeginningWithCount()
        {
            var key = GetUniqueKey("group_read_with_count");
            var groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                db.StreamCreateConsumerGroup(key, groupName, StreamConstants.StreamMinValue);

                // Start reading after id1.
                var entries = db.StreamReadGroup(key, groupName, "test_consumer", id1, 2);

                Assert.True(entries.Length == 2);
                Assert.True(id2 == entries[0].Id);
                Assert.True(id3 == entries[1].Id);
            }
        }

        [Fact]
        public void StreamConsumerGroupAcknowledgeMessage()
        {
            var key = GetUniqueKey("group_ack");
            var groupName = "test_group";
            var consumer = "test_consumer";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                // Read from the beginning of the stream.
                db.StreamCreateConsumerGroup(key, groupName, StreamConstants.StreamMinValue);

                // Read all 4 messages, they will be assigned to the consumer
                var entries = db.StreamReadGroup(key, groupName, consumer, "0-0");

                // Send XACK for 3 of the messages
                var oneAck = db.StreamAcknowledge(key, groupName, id1);
                var twoAck = db.StreamAcknowledge(key, groupName, new string[] { id3, id4 });

                // Read the group again, it should only return the unacknowledged message.
                var notAcknowledged = db.StreamReadGroup(key, groupName, consumer, "0-0");

                Assert.True(entries.Length == 4);
                Assert.Equal(1, oneAck);
                Assert.Equal(2, twoAck);
                Assert.True(notAcknowledged.Length == 1);
                Assert.Equal(id2, notAcknowledged[0].Id);
            }
        }

        [Fact]
        public void StreamConsumerGroupClaimMessages()
        {
            var key = GetUniqueKey("group_claim");
            var groupName = "test_group";
            var consumer1 = "test_consumer_1";
            var consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                // Read from the beginning of the stream.
                db.StreamCreateConsumerGroup(key, groupName, StreamConstants.StreamMinValue);

                // Read a single message into the first consumer.
                var consumer1Messages = db.StreamReadGroup(key, groupName, consumer1, StreamConstants.StreamMinValue, 1);

                // Read the remaining messages into the second consumer.
                var consumer2Messages = db.StreamReadGroup(key, groupName, consumer2, StreamConstants.UndeliveredMessages);

                // Claim the 3 messages consumed by consumer2 for consumer1.

                // Get the pending messages for consumer2
                var pendingMessages = db.StreamPendingMessageInfoGet(key, groupName,
                    StreamConstants.StreamMinValue,
                    StreamConstants.StreamMaxValue,
                    10,
                    consumer2);

                // Claim the messages for consumer1
                var messages = db.StreamClaimMessages(key,
                                    groupName,
                                    consumer1,
                                    0, // Min message idle time
                                    messageIds: pendingMessages.Select(pm => pm.MessageId).ToArray().ToStringArray());

                // Now see how many messages are pending for each consumer
                var pendingSummary = db.StreamPendingInfoGet(key, groupName);

                Assert.NotNull(pendingSummary);
                Assert.NotNull(pendingSummary.Consumers);
                Assert.True(pendingSummary.Consumers.Length == 1);
                Assert.Equal(4, pendingSummary.Consumers[0].PendingMessageCount);
            }
        }

        [Fact]
        public void StreamConsumerGroupViewPendingInfoSummary()
        {
            var key = GetUniqueKey("group_pending_info");
            var groupName = "test_group";
            var consumer1 = "test_consumer_1";
            var consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                // Read from the beginning of the stream.
                db.StreamCreateConsumerGroup(key, groupName, StreamConstants.StreamMinValue);

                // Read a single message into the first consumer.
                var consumer1Messages = db.StreamReadGroup(key, groupName, consumer1, StreamConstants.StreamMinValue, 1);

                // Read the remaining messages into the second consumer.
                var consumer2Messages = db.StreamReadGroup(key, groupName, consumer2, StreamConstants.UndeliveredMessages);

                var pendingInfo = db.StreamPendingInfoGet(key, groupName);

                Assert.NotNull(pendingInfo);
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
        public void StreamConsumerGroupViewPendingMessageInfo()
        {
            var key = GetUniqueKey("group_pending_messages");
            var groupName = "test_group";
            var consumer1 = "test_consumer_1";
            var consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                // Read from the beginning of the stream.
                db.StreamCreateConsumerGroup(key, groupName, StreamConstants.StreamMinValue);

                // Read a single message into the first consumer.
                var consumer1Messages = db.StreamReadGroup(key, groupName, consumer1, StreamConstants.UndeliveredMessages, 1);

                // Read the remaining messages into the second consumer.
                var consumer2Messages = db.StreamReadGroup(key, groupName, consumer2, StreamConstants.UndeliveredMessages);

                // Get the pending info about the messages themselves.
                var pendingMessageInfoList = db.StreamPendingMessageInfoGet(key, groupName, StreamConstants.StreamMinValue, StreamConstants.StreamMaxValue, 10);

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
            var groupName = "test_group";
            var consumer1 = "test_consumer_1";
            var consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                // Read from the beginning of the stream.
                db.StreamCreateConsumerGroup(key, groupName, StreamConstants.StreamMinValue);

                // Read a single message into the first consumer.
                var consumer1Messages = db.StreamReadGroup(key, groupName, consumer1, StreamConstants.UndeliveredMessages, 1);

                // Read the remaining messages into the second consumer.
                var consumer2Messages = db.StreamReadGroup(key, groupName, consumer2, StreamConstants.UndeliveredMessages);

                // Get the pending info about the messages themselves.
                var pendingMessageInfoList = db.StreamPendingMessageInfoGet(key,
                    groupName,
                    StreamConstants.StreamMinValue,
                    StreamConstants.StreamMaxValue,
                    10,
                    consumer2);

                Assert.NotNull(pendingMessageInfoList);
                Assert.Equal(3, pendingMessageInfoList.Length);
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

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                var deletedCount = db.StreamMessagesDelete(key, new string[1] { id3 }, CommandFlags.None);
                var messages = db.StreamRange(key, "-", "+");

                Assert.Equal(1, deletedCount);
                Assert.Equal(3, messages.Length);
            }
        }

        [Fact]
        public void StreamGroupInfoGet()
        {
            var key = GetUniqueKey("group_info");
            var group1 = "test_group_1";
            var group2 = "test_group_2";
            var consumer1 = "test_consumer_1";
            var consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                // Read from the beginning of the stream.
                db.StreamCreateConsumerGroup(key, group1, StreamConstants.StreamMinValue);
                db.StreamCreateConsumerGroup(key, group2, StreamConstants.StreamMinValue);

                // Read a single message into the first consumer.
                var consumer1Messages = db.StreamReadGroup(key, group1, consumer1, StreamConstants.UndeliveredMessages, 1);

                // Read the remaining messages into the second consumer.
                var consumer2Messages = db.StreamReadGroup(key, group2, consumer2, StreamConstants.UndeliveredMessages);

                var groupInfoList = db.StreamGroupInfoGet(key);

                Assert.NotNull(groupInfoList);
                Assert.Equal(2, groupInfoList.Length);

                Assert.Equal(group1, groupInfoList[0].Name);
                Assert.Equal(1, groupInfoList[0].PendingMessageCount);

                Assert.Equal(group2, groupInfoList[1].Name);
                Assert.Equal(4, groupInfoList[1].PendingMessageCount);
            }
        }

        [Fact]
        public void StreamGroupConsumerInfoGet()
        {
            var key = GetUniqueKey("group_consumer_info");
            var group = "test_group";
            var consumer1 = "test_consumer_1";
            var consumer2 = "test_consumer_2";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                // Read from the beginning of the stream.
                db.StreamCreateConsumerGroup(key, group, StreamConstants.StreamMinValue);
                db.StreamReadGroup(key, group, consumer1, StreamConstants.UndeliveredMessages, 1);
                db.StreamReadGroup(key, group, consumer2, StreamConstants.UndeliveredMessages);

                var consumerInfoList = db.StreamConsumerInfoGet(key, group);

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
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                var streamInfo = db.StreamInfoGet(key);

                Assert.NotNull(streamInfo);
                Assert.Equal(4, streamInfo.Length);
                Assert.True(streamInfo.RadixTreeKeys > 0);
                Assert.True(streamInfo.RadixTreeNodes > 0);
                Assert.Equal(id1, streamInfo.FirstEntry.Id);
                Assert.Equal(id4, streamInfo.LastEntry.Id);
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
                db.StreamAdd(key, "fiedl2", "value2");

                var len = db.StreamLength(key);

                Assert.Equal(2, len);
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

                // Add a couple items and check length.
                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");

                var entries = db.StreamRange(key, StreamConstants.StreamMinValue, StreamConstants.StreamMaxValue);

                Assert.Equal(2, entries.Length);
                Assert.Equal(id1, entries[0].Id);
                Assert.Equal(id2, entries[1].Id);
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

                // Add a couple items and check length.
                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");

                var entries = db.StreamRange(key, StreamConstants.StreamMinValue, StreamConstants.StreamMaxValue, 1);

                Assert.True(entries.Length == 1);
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

                // Add a couple items and check length.
                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");

                var entries = db.StreamRangeReverse(key, StreamConstants.StreamMaxValue, StreamConstants.StreamMinValue);

                Assert.True(entries.Length == 2);
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

                // Add a couple items and check length.
                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");

                var entries = db.StreamRangeReverse(key, StreamConstants.StreamMaxValue, StreamConstants.StreamMinValue, 1);

                Assert.True(entries.Length == 1);
                Assert.Equal(id2, entries[0].Id);
            }
        }

        [Fact]
        public void StreamRead()
        {
            var key = GetUniqueKey("read");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Add a couple items and check length.
                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");

                var entries = db.StreamRead(key, "0-0");

                Assert.True(entries.Length == 3);
                Assert.Equal(id1, entries[0].Id);
                Assert.Equal(id2, entries[1].Id);
                Assert.Equal(id3, entries[2].Id);
            }
        }

        [Fact]
        public void StreamReadWithAfterIdAndCount_1()
        {
            var key = GetUniqueKey("read");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Add a couple items and check length.
                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");

                var entries = db.StreamRead(key, id1, 1);

                Assert.True(entries.Length == 1);
                Assert.Equal(id2, entries[0].Id);
            }
        }

        [Fact]
        public void StreamReadWithAfterIdAndCount_2()
        {
            var key = GetUniqueKey("read");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Add a couple items and check length.
                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                var entries = db.StreamRead(key, id1, 2);

                Assert.True(entries.Length == 2);
                Assert.Equal(id2, entries[0].Id);
                Assert.Equal(id3, entries[1].Id);
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

                // Add a couple items and check length.
                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");

                var entries = db.StreamRead(key, id2);

                Assert.True(entries.Length == 0);
            }
        }

        [Fact]
        public void StreamReadMultipleStreams()
        {
            var key1 = GetUniqueKey("read_multi_1");
            var key2 = GetUniqueKey("read_multi_2");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Add a couple items and check length.
                var id1 = db.StreamAdd(key1, "field1", "value1");
                var id2 = db.StreamAdd(key1, "fiedl2", "value2");
                var id3 = db.StreamAdd(key2, "field3", "value3");
                var id4 = db.StreamAdd(key2, "field4", "value4");

                var streamList = new KeyValuePair<RedisKey, RedisValue>[2]
                {
                    new KeyValuePair<RedisKey, RedisValue>(key1, "0-0"),
                    new KeyValuePair<RedisKey, RedisValue>(key2, "0-0")
                };

                var streams = db.StreamRead(streamList);

                Assert.True(streams.Length == 2);

                Assert.Equal(key1, streams[0].Key);
                Assert.True(streams[0].Entries.Length == 2);

                Assert.Equal(key2, streams[1].Key);
                Assert.True(streams[1].Entries.Length == 2);
            }
        }

        [Fact]
        public void StreamReadMultipleStreamsWithReadPastSecondStream()
        {
            var key1 = GetUniqueKey("read_multi_1");
            var key2 = GetUniqueKey("read_multi_2");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Add a couple items and check length.
                var id1 = db.StreamAdd(key1, "field1", "value1");
                var id2 = db.StreamAdd(key1, "fiedl2", "value2");
                var id3 = db.StreamAdd(key2, "field3", "value3");
                var id4 = db.StreamAdd(key2, "field4", "value4");

                var streamList = new KeyValuePair<RedisKey, RedisValue>[2]
                {
                    new KeyValuePair<RedisKey, RedisValue>(key1, "0-0"),
                    new KeyValuePair<RedisKey, RedisValue>(key2, id4) // read past the end of stream # 2
                };

                var streams = db.StreamRead(streamList);

                // We should only get the first stream back.
                Assert.True(streams.Length == 1);

                Assert.Equal(key1, streams[0].Key);
                Assert.True(streams[0].Entries.Length == 2);
            }
        }

        [Fact]
        public void StreamReadMultipleStreamsWithEmptyResponse()
        {
            var key1 = GetUniqueKey("read_multi_1");
            var key2 = GetUniqueKey("read_multi_2");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                // Add a couple items and check length.
                var id1 = db.StreamAdd(key1, "field1", "value1");
                var id2 = db.StreamAdd(key1, "fiedl2", "value2");
                var id3 = db.StreamAdd(key2, "field3", "value3");
                var id4 = db.StreamAdd(key2, "field4", "value4");

                var streamList = new KeyValuePair<RedisKey, RedisValue>[2]
                {
                    new KeyValuePair<RedisKey, RedisValue>(key1, id2),
                    new KeyValuePair<RedisKey, RedisValue>(key2, id4) // read past the end of stream # 2
                };

                var streams = db.StreamRead(streamList);

                Assert.True(streams.Length == 0);
            }
        }

        private string GetUniqueKey(string type) => $"{type}_stream_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }
}
