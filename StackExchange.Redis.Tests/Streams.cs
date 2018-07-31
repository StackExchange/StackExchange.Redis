﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Streams : TestBase
    {
        public Streams(ITestOutputHelper output) : base(output) { }

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

                var fields = new NameValueEntry[]
                {
                    new NameValueEntry("field1", "value1"),
                    new NameValueEntry("field2", "value2")
                };

                var db = conn.GetDatabase();
                var messageId = db.StreamAdd(key, fields);

                var entries = db.StreamRange(key);

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
            var id = "42-0";
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
            var id = "42-0";
            var key = GetUniqueKey("manual_id_multiple_values");

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var fields = new NameValueEntry[]
                {
                    new NameValueEntry("field1", "value1"),
                    new NameValueEntry("field2", "value2")
                };

                var messageId = db.StreamAdd(key, fields, id);
                var entries = db.StreamRange(key);

                Assert.Equal(id, messageId);
                Assert.NotNull(entries);
                Assert.True(entries.Length == 1);
                Assert.Equal(id, entries[0].Id);
            }
        }

        [Fact]
        public void StreamConsumerGroupWithNoConsumers()
        {
            var key = GetUniqueKey("group_with_no_consumers");
            var groupName = "test_group";

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

                Assert.True(consumers.Length == 0);
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
                var result = db.StreamCreateConsumerGroup(key, groupName, "-");

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
                db.StreamCreateConsumerGroup(key, groupName);

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

                db.StreamCreateConsumerGroup(key, groupName, "-");

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

                db.StreamCreateConsumerGroup(key, groupName, "-");

                // Start reading after id1.
                var entries = db.StreamReadGroup(key, groupName, "test_consumer", id1, 2);

                // Ensure we only received the requested count and that the IDs match the expected values.
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

                db.StreamCreateConsumerGroup(key, groupName, "-");

                // Read all 4 messages, they will be assigned to the consumer
                var entries = db.StreamReadGroup(key, groupName, consumer, "0-0");

                // Send XACK for 3 of the messages

                // Single message Id overload.
                var oneAck = db.StreamAcknowledge(key, groupName, id1);

                // Multiple message Id overload.
                var twoAck = db.StreamAcknowledge(key, groupName, new RedisValue[] { id3, id4 });

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
                Assert.True(pendingSummary.Consumers.Length == 1);
                Assert.Equal(4, pendingSummary.Consumers[0].PendingMessageCount);
                Assert.True(pendingMessages.Length == messages.Length);
            }
        }

        [Fact]
        public void StreamConsumerGroupClaimMessagesReturningIds()
        {
            var key = GetUniqueKey("group_claim_view_ids");
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

                db.StreamCreateConsumerGroup(key, groupName, "-");

                // Read a single message into the first consumer.
                var consumer1Messages = db.StreamReadGroup(key, groupName, consumer1, "-", 1);

                // Read the remaining messages into the second consumer.
                var consumer2Messages = db.StreamReadGroup(key, groupName, consumer2);

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
        public void StreamConsumerGroupViewPendingInfoNoConsumers()
        {
            var key = GetUniqueKey("group_pending_info_no_consumers");
            var groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");

                db.StreamCreateConsumerGroup(key, groupName, "-");

                var pendingInfo = db.StreamPending(key, groupName);

                Assert.Equal(0, pendingInfo.PendingMessageCount);
                Assert.True(pendingInfo.LowestPendingMessageId == RedisValue.Null);
                Assert.True(pendingInfo.HighestPendingMessageId == RedisValue.Null);
                Assert.NotNull(pendingInfo.Consumers);
                Assert.True(pendingInfo.Consumers.Length == 0);
            }
        }

        [Fact]
        public void StreamConsumerGroupViewPendingInfoWhenNothingPending()
        {
            var key = GetUniqueKey("group_pending_info_nothing_pending");
            var groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id1 = db.StreamAdd(key, "field1", "value1");

                db.StreamCreateConsumerGroup(key, groupName, "0-0");

                var pendingMessages = db.StreamPendingMessages(key,
                    groupName,
                    10,
                    consumerName: RedisValue.Null);

                Assert.NotNull(pendingMessages);
                Assert.True(pendingMessages.Length == 0);
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

                db.StreamCreateConsumerGroup(key, groupName, "-");

                // Read a single message into the first consumer.
                var consumer1Messages = db.StreamReadGroup(key, groupName, consumer1, "-", 1);

                // Read the remaining messages into the second consumer.
                var consumer2Messages = db.StreamReadGroup(key, groupName, consumer2);

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

                db.StreamCreateConsumerGroup(key, groupName, "-");

                // Read a single message into the first consumer.
                var consumer1Messages = db.StreamReadGroup(key, groupName, consumer1, count: 1);

                // Read the remaining messages into the second consumer.
                var consumer2Messages = db.StreamReadGroup(key, groupName, consumer2);

                await Task.Delay(10);

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

                db.StreamCreateConsumerGroup(key, groupName, "-");

                // Read a single message into the first consumer.
                var consumer1Messages = db.StreamReadGroup(key, groupName, consumer1, count: 1);

                // Read the remaining messages into the second consumer.
                var consumer2Messages = db.StreamReadGroup(key, groupName, consumer2);

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

                var deletedCount = db.StreamDelete(key, new RedisValue[] { id3 });
                var messages = db.StreamRange(key, "-", "+");

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

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                var deletedCount = db.StreamDelete(key, new RedisValue[] { id2, id3 }, CommandFlags.None);
                var messages = db.StreamRange(key, "-", "+");

                Assert.Equal(2, deletedCount);
                Assert.Equal(2, messages.Length);
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

                db.StreamCreateConsumerGroup(key, group1, "-");
                db.StreamCreateConsumerGroup(key, group2, "-");

                // Read a single message into the first consumer.
                var consumer1Messages = db.StreamReadGroup(key, group1, consumer1, count: 1);

                // Read the remaining messages into the second consumer.
                var consumer2Messages = db.StreamReadGroup(key, group2, consumer2);

                var groupInfoList = db.StreamGroupInfo(key);

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

                db.StreamCreateConsumerGroup(key, group, "-");
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
                var id2 = db.StreamAdd(key, "field2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
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
                db.StreamDelete(key, new RedisValue[] { id });

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
                Assert.True(groups.Length == 0);
            }
        }

        [Fact]
        public void StreamPendingNoMessagesOrConsumers()
        {
            var key = GetUniqueKey("stream_pending_empty");
            var groupName = "test_group";

            using (var conn = Create())
            {
                Skip.IfMissingFeature(conn, nameof(RedisFeatures.Streams), r => r.Streams);

                var db = conn.GetDatabase();

                var id = db.StreamAdd(key, "field1", "value1");
                db.StreamDelete(key, new RedisValue[] { id });

                db.StreamCreateConsumerGroup(key, groupName, "0-0");

                var pendingInfo = db.StreamPending(key, "test_group");

                Assert.Equal(0, pendingInfo.PendingMessageCount);
                Assert.Equal(RedisValue.Null, pendingInfo.LowestPendingMessageId);
                Assert.Equal(RedisValue.Null, pendingInfo.HighestPendingMessageId);
                Assert.NotNull(pendingInfo.Consumers);
                Assert.True(pendingInfo.Consumers.Length == 0);
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

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");

                // Read the entire stream from the beginning.
                var entries = db.StreamRead(key, "0-0");

                Assert.True(entries.Length == 3);
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
                db.StreamDelete(key, new RedisValue[] { id1 });
                var len = db.StreamLength(key);

                // Read the entire stream from the beginning.
                var entries = db.StreamRead(key, "0-0");

                Assert.True(entries.Length == 0);
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
                db.StreamDelete(key1, new RedisValue[] { id1 });
                db.StreamDelete(key2, new RedisValue[] { id2 });

                var len1 = db.StreamLength(key1);
                var len2 = db.StreamLength(key2);

                // Read the entire stream from the beginning.
                var entries1 = db.StreamRead(key1, "0-0");
                var entries2 = db.StreamRead(key2, "0-0");

                Assert.True(entries1.Length == 0);
                Assert.True(entries2.Length == 0);

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

                var streamPairs = new StreamIdPair[]
                {
                    new StreamIdPair("key1", "0-0"),
                    new StreamIdPair("key2", "0-0")
                };


                var db = conn.GetDatabase();
                Assert.Throws<ArgumentOutOfRangeException>(() => db.StreamRead(streamPairs, 0));
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

                var emptyList = new StreamIdPair[0];

                Assert.Throws<ArgumentOutOfRangeException>(() => db.StreamRead(emptyList));
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

                var id1 = db.StreamAdd(key1, "field1", "value1");
                var id2 = db.StreamAdd(key1, "fiedl2", "value2");
                var id3 = db.StreamAdd(key2, "field3", "value3");
                var id4 = db.StreamAdd(key2, "field4", "value4");

                // Read from both streams at the same time.
                var streamList = new StreamIdPair[2]
                {
                    new StreamIdPair(key1, "0-0"),
                    new StreamIdPair(key2, "0-0")
                };

                var streams = db.StreamRead(streamList);

                Assert.True(streams.Length == 2);

                Assert.Equal(key1, streams[0].Key);
                Assert.True(streams[0].Entries.Length == 2);
                Assert.Equal(id1, streams[0].Entries[0].Id);
                Assert.Equal(id2, streams[0].Entries[1].Id);

                Assert.Equal(key2, streams[1].Key);
                Assert.True(streams[1].Entries.Length == 2);
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
                var id2 = db.StreamAdd(key1, "fiedl2", "value2");
                var id3 = db.StreamAdd(key2, "field3", "value3");
                var id4 = db.StreamAdd(key2, "field4", "value4");

                var streamList = new StreamIdPair[2]
                {
                    new StreamIdPair(key1, "0-0"),
                    new StreamIdPair(key2, "0-0")
                };

                var streams = db.StreamRead(streamList, countPerStream: 1);

                // We should get both streams back.
                Assert.True(streams.Length == 2);

                // Ensure we only got one message per stream.
                Assert.True(streams[0].Entries.Length == 1);
                Assert.True(streams[1].Entries.Length == 1);

                // Check the message IDs as well.
                Assert.Equal(id1, streams[0].Entries[0].Id);
                Assert.Equal(id3, streams[1].Entries[0].Id);
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

                var id1 = db.StreamAdd(key1, "field1", "value1");
                var id2 = db.StreamAdd(key1, "fiedl2", "value2");
                var id3 = db.StreamAdd(key2, "field3", "value3");
                var id4 = db.StreamAdd(key2, "field4", "value4");

                var streamList = new StreamIdPair[2]
                {
                    new StreamIdPair(key1, "0-0"),

                    // read past the end of stream # 2
                    new StreamIdPair(key2, id4)
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

                var id1 = db.StreamAdd(key1, "field1", "value1");
                var id2 = db.StreamAdd(key1, "fiedl2", "value2");
                var id3 = db.StreamAdd(key2, "field3", "value3");
                var id4 = db.StreamAdd(key2, "field4", "value4");

                var streamList = new StreamIdPair[]
                {
                    // Read past the end of both streams.
                    new StreamIdPair(key1, id2),
                    new StreamIdPair(key2, id4)
                };

                var streams = db.StreamRead(streamList);

                // We expect an empty response.
                Assert.True(streams.Length == 0);
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

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");

                // Read after the final ID in the stream, we expect an empty array as a response.

                var entries = db.StreamRead(key, id2);

                Assert.True(entries.Length == 0);
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
                var id2 = db.StreamAdd(key, "fiedl2", "value2");

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
                var id2 = db.StreamAdd(key, "fiedl2", "value2");

                var deleted = db.StreamDelete(key, new RedisValue[] { id1, id2 });

                var entries = db.StreamRange(key, "-", "+");

                Assert.Equal(2, deleted);
                Assert.NotNull(entries);
                Assert.True(entries.Length == 0);
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
                var id2 = db.StreamAdd(key, "fiedl2", "value2");

                var entries = db.StreamRange(key, count: 1);

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

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");

                var entries = db.StreamRange(key, messageOrder: Order.Descending);

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

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");

                var entries = db.StreamRange(key, count: 1, messageOrder: Order.Descending);

                Assert.True(entries.Length == 1);
                Assert.Equal(id2, entries[0].Id);
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

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");

                // Only read a single item from the stream.
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

                var id1 = db.StreamAdd(key, "field1", "value1");
                var id2 = db.StreamAdd(key, "fiedl2", "value2");
                var id3 = db.StreamAdd(key, "field3", "value3");
                var id4 = db.StreamAdd(key, "field4", "value4");

                // Read multiple items from the stream.
                var entries = db.StreamRead(key, id1, 2);

                Assert.True(entries.Length == 2);
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
                db.StreamAdd(key, "fiedl2", "value2");
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
                db.StreamAdd(key, "fiedl2", "value2");

                var len = db.StreamLength(key);

                Assert.Equal(2, len);
            }
        }

        private string GetUniqueKey(string type) => $"{type}_stream_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }
}
