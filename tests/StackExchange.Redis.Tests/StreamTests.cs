using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class StreamTests : TestBase
{
    public StreamTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture) { }

    public override string Me([CallerFilePath] string? filePath = null, [CallerMemberName] string? caller = null) =>
        base.Me(filePath, caller) + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [Fact]
    public void IsStreamType()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.StreamAdd(key, "field1", "value1");

        var keyType = db.KeyType(key);

        Assert.Equal(RedisType.Stream, keyType);
    }

    [Fact]
    public void StreamAddSinglePairWithAutoId()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        var messageId = db.StreamAdd(key, "field1", "value1");

        Assert.True(messageId != RedisValue.Null && ((string?)messageId)?.Length > 0);
    }

    [Fact]
    public void StreamAddMultipleValuePairsWithAutoId()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        var fields = new[]
        {
            new NameValueEntry("field1", "value1"),
            new NameValueEntry("field2", "value2"),
        };

        var messageId = db.StreamAdd(key, fields);

        var entries = db.StreamRange(key);

        Assert.Single(entries);
        Assert.Equal(messageId, entries[0].Id);
        var vals = entries[0].Values;
        Assert.NotNull(vals);
        Assert.Equal(2, vals.Length);
        Assert.Equal("field1", vals[0].Name);
        Assert.Equal("value1", vals[0].Value);
        Assert.Equal("field2", vals[1].Name);
        Assert.Equal("value2", vals[1].Value);
    }

    [Fact]
    public void StreamAddWithManualId()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        const string id = "42-0";
        var key = Me();

        var messageId = db.StreamAdd(key, "field1", "value1", id);

        Assert.Equal(id, messageId);
    }

    [Fact]
    public void StreamAddMultipleValuePairsWithManualId()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        const string id = "42-0";
        var key = Me();

        var fields = new[]
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

    [Fact]
    public async Task StreamAutoClaim_MissingKey()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer = "consumer";

        db.KeyDelete(key);

        var ex = Assert.Throws<RedisServerException>(() => db.StreamAutoClaim(key, group, consumer, 0, "0-0"));
        Assert.StartsWith("NOGROUP No such key", ex.Message);

        ex = await Assert.ThrowsAsync<RedisServerException>(() => db.StreamAutoClaimAsync(key, group, consumer, 0, "0-0"));
        Assert.StartsWith("NOGROUP No such key", ex.Message);
    }

    [Fact]
    public void StreamAutoClaim_ClaimsPendingMessages()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        _ = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Claim any pending messages and reassign them to consumer2.
        var result = db.StreamAutoClaim(key, group, consumer2, 0, "0-0");

        Assert.Equal("0-0", result.NextStartId);
        Assert.NotEmpty(result.ClaimedEntries);
        Assert.Empty(result.DeletedIds);
        Assert.True(result.ClaimedEntries.Length == 2);
        Assert.Equal("value1", result.ClaimedEntries[0].Values[0].Value);
        Assert.Equal("value2", result.ClaimedEntries[1].Values[0].Value);
    }

    [Fact]
    public async Task StreamAutoClaim_ClaimsPendingMessagesAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        _ = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Claim any pending messages and reassign them to consumer2.
        var result = await db.StreamAutoClaimAsync(key, group, consumer2, 0, "0-0");

        Assert.Equal("0-0", result.NextStartId);
        Assert.NotEmpty(result.ClaimedEntries);
        Assert.Empty(result.DeletedIds);
        Assert.True(result.ClaimedEntries.Length == 2);
        Assert.Equal("value1", result.ClaimedEntries[0].Values[0].Value);
        Assert.Equal("value2", result.ClaimedEntries[1].Values[0].Value);
    }

    [Fact]
    public void StreamAutoClaim_ClaimsSingleMessageWithCountOption()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        var messageIds = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Claim a single pending message and reassign it to consumer2.
        var result = db.StreamAutoClaim(key, group, consumer2, 0, "0-0", count: 1);

        // Should be the second message ID from the call to prepare.
        Assert.Equal(messageIds[1], result.NextStartId);
        Assert.NotEmpty(result.ClaimedEntries);
        Assert.Empty(result.DeletedIds);
        Assert.True(result.ClaimedEntries.Length == 1);
        Assert.Equal("value1", result.ClaimedEntries[0].Values[0].Value);
    }

    [Fact]
    public void StreamAutoClaim_ClaimsSingleMessageWithCountOptionIdsOnly()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        var messageIds = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Claim a single pending message and reassign it to consumer2.
        var result = db.StreamAutoClaimIdsOnly(key, group, consumer2, 0, "0-0", count: 1);

        // Should be the second message ID from the call to prepare.
        Assert.Equal(messageIds[1], result.NextStartId);
        Assert.NotEmpty(result.ClaimedIds);
        Assert.True(result.ClaimedIds.Length == 1);
        Assert.Equal(messageIds[0], result.ClaimedIds[0]);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public async Task StreamAutoClaim_ClaimsSingleMessageWithCountOptionAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        var messageIds = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Claim a single pending message and reassign it to consumer2.
        var result = await db.StreamAutoClaimAsync(key, group, consumer2, 0, "0-0", count: 1);

        // Should be the second message ID from the call to prepare.
        Assert.Equal(messageIds[1], result.NextStartId);
        Assert.NotEmpty(result.ClaimedEntries);
        Assert.Empty(result.DeletedIds);
        Assert.True(result.ClaimedEntries.Length == 1);
        Assert.Equal("value1", result.ClaimedEntries[0].Values[0].Value);
    }

    [Fact]
    public async Task StreamAutoClaim_ClaimsSingleMessageWithCountOptionIdsOnlyAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        var messageIds = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Claim a single pending message and reassign it to consumer2.
        var result = await db.StreamAutoClaimIdsOnlyAsync(key, group, consumer2, 0, "0-0", count: 1);

        // Should be the second message ID from the call to prepare.
        Assert.Equal(messageIds[1], result.NextStartId);
        Assert.NotEmpty(result.ClaimedIds);
        Assert.True(result.ClaimedIds.Length == 1);
        Assert.Equal(messageIds[0], result.ClaimedIds[0]);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public void StreamAutoClaim_IncludesDeletedMessageId()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        var messageIds = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Delete one of the messages, it should be included in the deleted message ID array.
        db.StreamDelete(key, new RedisValue[] { messageIds[0] });

        // Claim a single pending message and reassign it to consumer2.
        var result = db.StreamAutoClaim(key, group, consumer2, 0, "0-0", count: 2);

        Assert.Equal("0-0", result.NextStartId);
        Assert.NotEmpty(result.ClaimedEntries);
        Assert.NotEmpty(result.DeletedIds);
        Assert.True(result.ClaimedEntries.Length == 1);
        Assert.True(result.DeletedIds.Length == 1);
        Assert.Equal(messageIds[0], result.DeletedIds[0]);
    }

    [Fact]
    public async Task StreamAutoClaim_IncludesDeletedMessageIdAsync()
    {
        using var conn = Create(require: RedisFeatures.v7_0_0_rc1);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        var messageIds = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Delete one of the messages, it should be included in the deleted message ID array.
        db.StreamDelete(key, new RedisValue[] { messageIds[0] });

        // Claim a single pending message and reassign it to consumer2.
        var result = await db.StreamAutoClaimAsync(key, group, consumer2, 0, "0-0", count: 2);

        Assert.Equal("0-0", result.NextStartId);
        Assert.NotEmpty(result.ClaimedEntries);
        Assert.NotEmpty(result.DeletedIds);
        Assert.True(result.ClaimedEntries.Length == 1);
        Assert.True(result.DeletedIds.Length == 1);
        Assert.Equal(messageIds[0], result.DeletedIds[0]);
    }

    [Fact]
    public void StreamAutoClaim_NoMessagesToClaim()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup";

        // Create the group.
        db.KeyDelete(key);
        db.StreamCreateConsumerGroup(key, group, createStream: true);

        // **Don't add any messages to the stream**

        // Claim any pending messages (there aren't any) and reassign them to consumer2.
        var result = db.StreamAutoClaim(key, group, "consumer1", 0, "0-0");

        // Claimed entries should be empty
        Assert.Equal("0-0", result.NextStartId);
        Assert.Empty(result.ClaimedEntries);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public async Task StreamAutoClaim_NoMessagesToClaimAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup";

        // Create the group.
        db.KeyDelete(key);
        db.StreamCreateConsumerGroup(key, group, createStream: true);

        // **Don't add any messages to the stream**

        // Claim any pending messages (there aren't any) and reassign them to consumer2.
        var result = await db.StreamAutoClaimAsync(key, group, "consumer1", 0, "0-0");

        // Claimed entries should be empty
        Assert.Equal("0-0", result.NextStartId);
        Assert.Empty(result.ClaimedEntries);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public void StreamAutoClaim_NoMessageMeetsMinIdleTime()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        _ = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Claim messages idle for more than 5 minutes, should return an empty array.
        var result = db.StreamAutoClaim(key, group, consumer2, 300000, "0-0");

        Assert.Equal("0-0", result.NextStartId);
        Assert.Empty(result.ClaimedEntries);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public async Task StreamAutoClaim_NoMessageMeetsMinIdleTimeAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        _ = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Claim messages idle for more than 5 minutes, should return an empty array.
        var result = await db.StreamAutoClaimAsync(key, group, consumer2, 300000, "0-0");

        Assert.Equal("0-0", result.NextStartId);
        Assert.Empty(result.ClaimedEntries);
        Assert.Empty(result.DeletedIds);
    }

    [Fact]
    public void StreamAutoClaim_ReturnsMessageIdOnly()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        var messageIds = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Claim any pending messages and reassign them to consumer2.
        var result = db.StreamAutoClaimIdsOnly(key, group, consumer2, 0, "0-0");

        Assert.Equal("0-0", result.NextStartId);
        Assert.NotEmpty(result.ClaimedIds);
        Assert.Empty(result.DeletedIds);
        Assert.True(result.ClaimedIds.Length == 2);
        Assert.Equal(messageIds[0], result.ClaimedIds[0]);
        Assert.Equal(messageIds[1], result.ClaimedIds[1]);
    }

    [Fact]
    public async Task StreamAutoClaim_ReturnsMessageIdOnlyAsync()
    {
        using var conn = Create(require: RedisFeatures.v6_2_0);

        var key = Me();
        var db = conn.GetDatabase();
        const string group = "consumerGroup",
                     consumer1 = "c1",
                     consumer2 = "c2";

        // Create Consumer Group, add messages, and read messages into a consumer.
        var messageIds = StreamAutoClaim_PrepareTestData(db, key, group, consumer1);

        // Claim any pending messages and reassign them to consumer2.
        var result = await db.StreamAutoClaimIdsOnlyAsync(key, group, consumer2, 0, "0-0");

        Assert.Equal("0-0", result.NextStartId);
        Assert.NotEmpty(result.ClaimedIds);
        Assert.Empty(result.DeletedIds);
        Assert.True(result.ClaimedIds.Length == 2);
        Assert.Equal(messageIds[0], result.ClaimedIds[0]);
        Assert.Equal(messageIds[1], result.ClaimedIds[1]);
    }

    private RedisValue[] StreamAutoClaim_PrepareTestData(IDatabase db, RedisKey key, RedisValue group, RedisValue consumer)
    {
        // Create the group.
        db.KeyDelete(key);
        db.StreamCreateConsumerGroup(key, group, createStream: true);

        // Add some messages
        var id1 = db.StreamAdd(key, "field1", "value1");
        var id2 = db.StreamAdd(key, "field2", "value2");

        // Read the messages into the "c1"
        db.StreamReadGroup(key, group, consumer);

        return new RedisValue[2] { id1, id2 };
    }

    [Fact]
    public void StreamConsumerGroupSetId()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group",
                     consumer = "consumer";

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

    [Fact]
    public void StreamConsumerGroupWithNoConsumers()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group";

        // Create a stream
        db.StreamAdd(key, "field1", "value1");

        // Create a group
        db.StreamCreateConsumerGroup(key, groupName, "0-0");

        // Query redis for the group consumers, expect an empty list in response.
        var consumers = db.StreamConsumerInfo(key, groupName);

        Assert.Empty(consumers);
    }

    [Fact]
    public void StreamCreateConsumerGroup()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group";

        // Create a stream
        db.StreamAdd(key, "field1", "value1");

        // Create a group
        var result = db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);

        Assert.True(result);
    }

    [Fact]
    public void StreamCreateConsumerGroupBeforeCreatingStream()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        // Ensure the key doesn't exist.
        var keyExistsBeforeCreate = db.KeyExists(key);

        // The 'createStream' parameter is 'true' by default.
        var groupCreated = db.StreamCreateConsumerGroup(key, "consumerGroup", StreamPosition.NewMessages);

        var keyExistsAfterCreate = db.KeyExists(key);

        Assert.False(keyExistsBeforeCreate);
        Assert.True(groupCreated);
        Assert.True(keyExistsAfterCreate);
    }

    [Fact]
    public void StreamCreateConsumerGroupFailsIfKeyDoesntExist()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        // Pass 'false' for 'createStream' to ensure that an
        // exception is thrown when the stream doesn't exist.
        Assert.ThrowsAny<RedisServerException>(() => db.StreamCreateConsumerGroup(
                key,
                "consumerGroup",
                StreamPosition.NewMessages,
                createStream: false));
    }

    [Fact]
    public void StreamCreateConsumerGroupSucceedsWhenKeyExists()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

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

    [Fact]
    public void StreamConsumerGroupReadOnlyNewMessagesWithEmptyResponse()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group";

        // Create a stream
        db.StreamAdd(key, "field1", "value1");
        db.StreamAdd(key, "field2", "value2");

        // Create a group.
        db.StreamCreateConsumerGroup(key, groupName);

        // Read, expect no messages
        var entries = db.StreamReadGroup(key, groupName, "test_consumer", "0-0");

        Assert.Empty(entries);
    }

    [Fact]
    public void StreamConsumerGroupReadFromStreamBeginning()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group";

        var id1 = db.StreamAdd(key, "field1", "value1");
        var id2 = db.StreamAdd(key, "field2", "value2");

        db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);

        var entries = db.StreamReadGroup(key, groupName, "test_consumer", StreamPosition.NewMessages);

        Assert.Equal(2, entries.Length);
        Assert.True(id1 == entries[0].Id);
        Assert.True(id2 == entries[1].Id);
    }

    [Fact]
    public void StreamConsumerGroupReadFromStreamBeginningWithCount()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group";

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

    [Fact]
    public void StreamConsumerGroupAcknowledgeMessage()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group",
                     consumer = "test_consumer";

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
        var twoAck = db.StreamAcknowledge(key, groupName, new[] { id3, id4 });

        // Read the group again, it should only return the unacknowledged message.
        var notAcknowledged = db.StreamReadGroup(key, groupName, consumer, "0-0");

        Assert.Equal(4, entries.Length);
        Assert.Equal(1, oneAck);
        Assert.Equal(2, twoAck);
        Assert.Single(notAcknowledged);
        Assert.Equal(id2, notAcknowledged[0].Id);
    }

    [Fact]
    public void StreamConsumerGroupClaimMessages()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group",
                     consumer1 = "test_consumer_1",
                     consumer2 = "test_consumer_2";

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

    [Fact]
    public void StreamConsumerGroupClaimMessagesReturningIds()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group",
                     consumer1 = "test_consumer_1",
                     consumer2 = "test_consumer_2";

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

    [Fact]
    public void StreamConsumerGroupReadMultipleOneReadBeginningOneReadNew()
    {
        // Create a group for each stream. One set to read from the beginning of the
        // stream and the other to begin reading only new messages.

        // Ask redis to read from the beginning of both stream, expect messages
        // for only the stream set to read from the beginning.

        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        const string groupName = "test_group";
        var stream1 = Me() + "a";
        var stream2 = Me() + "b";

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
        var pairs = new[]
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

    [Fact]
    public void StreamConsumerGroupReadMultipleOnlyNewMessagesExpectNoResult()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        const string groupName = "test_group";
        var stream1 = Me() + "a";
        var stream2 = Me() + "b";

        db.StreamAdd(stream1, "field1-1", "value1-1");
        db.StreamAdd(stream2, "field2-1", "value2-1");

        // set both streams to read only new messages (default behavior).
        db.StreamCreateConsumerGroup(stream1, groupName);
        db.StreamCreateConsumerGroup(stream2, groupName);

        // We shouldn't get anything for either stream.
        var pairs = new[]
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

    [Fact]
    public void StreamConsumerGroupReadMultipleOnlyNewMessagesExpect1Result()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        const string groupName = "test_group";
        var stream1 = Me() + "a";
        var stream2 = Me() + "b";

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
        var pairs = new[]
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

    [Fact]
    public void StreamConsumerGroupReadMultipleRestrictCount()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        const string groupName = "test_group";
        var stream1 = Me() + "a";
        var stream2 = Me() + "b";

        var id1_1 = db.StreamAdd(stream1, "field1-1", "value1-1");
        var id1_2 = db.StreamAdd(stream1, "field1-2", "value1-2");

        var id2_1 = db.StreamAdd(stream2, "field2-1", "value2-1");
        _ = db.StreamAdd(stream2, "field2-2", "value2-2");
        _ = db.StreamAdd(stream2, "field2-3", "value2-3");

        // Set the initial read point in each stream, *after* the first ID in both streams.
        db.StreamCreateConsumerGroup(stream1, groupName, id1_1);
        db.StreamCreateConsumerGroup(stream2, groupName, id2_1);

        var pairs = new[]
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

    [Fact]
    public void StreamConsumerGroupViewPendingInfoNoConsumers()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group";

        db.StreamAdd(key, "field1", "value1");

        db.StreamCreateConsumerGroup(key, groupName, StreamPosition.Beginning);

        var pendingInfo = db.StreamPending(key, groupName);

        Assert.Equal(0, pendingInfo.PendingMessageCount);
        Assert.Equal(RedisValue.Null, pendingInfo.LowestPendingMessageId);
        Assert.Equal(RedisValue.Null, pendingInfo.HighestPendingMessageId);
        Assert.NotNull(pendingInfo.Consumers);
        Assert.Empty(pendingInfo.Consumers);
    }

    [Fact]
    public void StreamConsumerGroupViewPendingInfoWhenNothingPending()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group";

        db.StreamAdd(key, "field1", "value1");

        db.StreamCreateConsumerGroup(key, groupName, "0-0");

        var pendingMessages = db.StreamPendingMessages(key,
            groupName,
            10,
            consumerName: RedisValue.Null);

        Assert.NotNull(pendingMessages);
        Assert.Empty(pendingMessages);
    }

    [Fact]
    public void StreamConsumerGroupViewPendingInfoSummary()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group",
                     consumer1 = "test_consumer_1",
                     consumer2 = "test_consumer_2";

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

    [Fact]
    public async Task StreamConsumerGroupViewPendingMessageInfo()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group",
                     consumer1 = "test_consumer_1",
                     consumer2 = "test_consumer_2";

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

    [Fact]
    public void StreamConsumerGroupViewPendingMessageInfoForConsumer()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group",
                     consumer1 = "test_consumer_1",
                     consumer2 = "test_consumer_2";

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

    [Fact]
    public void StreamDeleteConsumer()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group",
                     consumer = "test_consumer";

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

    [Fact]
    public void StreamDeleteConsumerGroup()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group",
                     consumer = "test_consumer";

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

    [Fact]
    public void StreamDeleteMessage()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        db.StreamAdd(key, "field1", "value1");
        db.StreamAdd(key, "field2", "value2");
        var id3 = db.StreamAdd(key, "field3", "value3");
        db.StreamAdd(key, "field4", "value4");

        var deletedCount = db.StreamDelete(key, new[] { id3 });
        var messages = db.StreamRange(key);

        Assert.Equal(1, deletedCount);
        Assert.Equal(3, messages.Length);
    }

    [Fact]
    public void StreamDeleteMessages()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        db.StreamAdd(key, "field1", "value1");
        var id2 = db.StreamAdd(key, "field2", "value2");
        var id3 = db.StreamAdd(key, "field3", "value3");
        db.StreamAdd(key, "field4", "value4");

        var deletedCount = db.StreamDelete(key, new[] { id2, id3 }, CommandFlags.None);
        var messages = db.StreamRange(key);

        Assert.Equal(2, deletedCount);
        Assert.Equal(2, messages.Length);
    }

    [Fact]
    public void StreamGroupInfoGet()
    {
        var key = Me();
        const string group1 = "test_group_1",
                     group2 = "test_group_2",
                     consumer1 = "test_consumer_1",
                     consumer2 = "test_consumer_2";

        using (var conn = Create(require: RedisFeatures.v5_0_0))
        {
            var db = conn.GetDatabase();
            db.KeyDelete(key);

            db.StreamAdd(key, "field1", "value1");
            db.StreamAdd(key, "field2", "value2");
            db.StreamAdd(key, "field3", "value3");
            db.StreamAdd(key, "field4", "value4");

            db.StreamCreateConsumerGroup(key, group1, StreamPosition.Beginning);
            db.StreamCreateConsumerGroup(key, group2, StreamPosition.Beginning);

            var groupInfoList = db.StreamGroupInfo(key);
            Assert.Equal(0, groupInfoList[0].EntriesRead);
            Assert.Equal(4, groupInfoList[0].Lag);
            Assert.Equal(0, groupInfoList[0].EntriesRead);
            Assert.Equal(4, groupInfoList[1].Lag);

            // Read a single message into the first consumer.
            db.StreamReadGroup(key, group1, consumer1, count: 1);

            // Read the remaining messages into the second consumer.
            db.StreamReadGroup(key, group2, consumer2);

            groupInfoList = db.StreamGroupInfo(key);

            Assert.NotNull(groupInfoList);
            Assert.Equal(2, groupInfoList.Length);

            Assert.Equal(group1, groupInfoList[0].Name);
            Assert.Equal(1, groupInfoList[0].PendingMessageCount);
            Assert.True(IsMessageId(groupInfoList[0].LastDeliveredId)); // can't test actual - will vary
            Assert.Equal(1, groupInfoList[0].EntriesRead);
            Assert.Equal(3, groupInfoList[0].Lag);

            Assert.Equal(group2, groupInfoList[1].Name);
            Assert.Equal(4, groupInfoList[1].PendingMessageCount);
            Assert.True(IsMessageId(groupInfoList[1].LastDeliveredId)); // can't test actual - will vary
            Assert.Equal(4, groupInfoList[1].EntriesRead);
            Assert.Equal(0, groupInfoList[1].Lag);
        }

        static bool IsMessageId(string? value)
        {
            if (value.IsNullOrWhiteSpace()) return false;
            return value.Length >= 3 && value.Contains('-');
        }
    }

    [Fact]
    public void StreamGroupConsumerInfoGet()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string group = "test_group",
                     consumer1 = "test_consumer_1",
                     consumer2 = "test_consumer_2";

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

    [Fact]
    public void StreamInfoGet()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

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

    [Fact]
    public void StreamInfoGetWithEmptyStream()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        // Add an entry and then delete it so the stream is empty, then run streaminfo
        // to ensure it functions properly on an empty stream. Namely, the first-entry
        // and last-entry messages should be null.

        var id = db.StreamAdd(key, "field1", "value1");
        db.StreamDelete(key, new[] { id });

        Assert.Equal(0, db.StreamLength(key));

        var streamInfo = db.StreamInfo(key);

        Assert.True(streamInfo.FirstEntry.IsNull);
        Assert.True(streamInfo.LastEntry.IsNull);
    }

    [Fact]
    public void StreamNoConsumerGroups()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        db.StreamAdd(key, "field1", "value1");

        var groups = db.StreamGroupInfo(key);

        Assert.NotNull(groups);
        Assert.Empty(groups);
    }

    [Fact]
    public void StreamPendingNoMessagesOrConsumers()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group";

        var id = db.StreamAdd(key, "field1", "value1");
        db.StreamDelete(key, new[] { id });

        db.StreamCreateConsumerGroup(key, groupName, "0-0");

        var pendingInfo = db.StreamPending(key, "test_group");

        Assert.Equal(0, pendingInfo.PendingMessageCount);
        Assert.Equal(RedisValue.Null, pendingInfo.LowestPendingMessageId);
        Assert.Equal(RedisValue.Null, pendingInfo.HighestPendingMessageId);
        Assert.NotNull(pendingInfo.Consumers);
        Assert.Empty(pendingInfo.Consumers);
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
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

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

    [Fact]
    public void StreamReadEmptyStream()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        // Write to a stream to create the key.
        var id1 = db.StreamAdd(key, "field1", "value1");

        // Delete the key to empty the stream.
        db.StreamDelete(key, new[] { id1 });
        var len = db.StreamLength(key);

        // Read the entire stream from the beginning.
        var entries = db.StreamRead(key, "0-0");

        Assert.Empty(entries);
        Assert.Equal(0, len);
    }

    [Fact]
    public void StreamReadEmptyStreams()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key1 = Me() + "a";
        var key2 = Me() + "b";

        // Write to a stream to create the key.
        var id1 = db.StreamAdd(key1, "field1", "value1");
        var id2 = db.StreamAdd(key2, "field2", "value2");

        // Delete the key to empty the stream.
        db.StreamDelete(key1, new[] { id1 });
        db.StreamDelete(key2, new[] { id2 });

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

    [Fact]
    public void StreamReadExpectedExceptionInvalidCountMultipleStream()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var streamPositions = new[]
        {
            new StreamPosition("key1", "0-0"),
            new StreamPosition("key2", "0-0")
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => db.StreamRead(streamPositions, 0));
    }

    [Fact]
    public void StreamReadExpectedExceptionInvalidCountSingleStream()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        Assert.Throws<ArgumentOutOfRangeException>(() => db.StreamRead(key, "0-0", 0));
    }

    [Fact]
    public void StreamReadExpectedExceptionNullStreamList()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        Assert.Throws<ArgumentNullException>(() => db.StreamRead(null!));
    }

    [Fact]
    public void StreamReadExpectedExceptionEmptyStreamList()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var emptyList = Array.Empty<StreamPosition>();
        Assert.Throws<ArgumentOutOfRangeException>(() => db.StreamRead(emptyList));
    }

    [Fact]
    public void StreamReadMultipleStreams()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key1 = Me() + "a";
        var key2 = Me() + "b";

        var id1 = db.StreamAdd(key1, "field1", "value1");
        var id2 = db.StreamAdd(key1, "field2", "value2");
        var id3 = db.StreamAdd(key2, "field3", "value3");
        var id4 = db.StreamAdd(key2, "field4", "value4");

        // Read from both streams at the same time.
        var streamList = new[]
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

    [Fact]
    public void StreamReadMultipleStreamsWithCount()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key1 = Me() + "a";
        var key2 = Me() + "b";

        var id1 = db.StreamAdd(key1, "field1", "value1");
        db.StreamAdd(key1, "field2", "value2");
        var id3 = db.StreamAdd(key2, "field3", "value3");
        db.StreamAdd(key2, "field4", "value4");

        var streamList = new[]
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

    [Fact]
    public void StreamReadMultipleStreamsWithReadPastSecondStream()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key1 = Me() + "a";
        var key2 = Me() + "b";

        db.StreamAdd(key1, "field1", "value1");
        db.StreamAdd(key1, "field2", "value2");
        db.StreamAdd(key2, "field3", "value3");
        var id4 = db.StreamAdd(key2, "field4", "value4");

        var streamList = new[]
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

    [Fact]
    public void StreamReadMultipleStreamsWithEmptyResponse()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key1 = Me() + "a";
        var key2 = Me() + "b";

        db.StreamAdd(key1, "field1", "value1");
        var id2 = db.StreamAdd(key1, "field2", "value2");
        db.StreamAdd(key2, "field3", "value3");
        var id4 = db.StreamAdd(key2, "field4", "value4");

        var streamList = new[]
        {
                // Read past the end of both streams.
                new StreamPosition(key1, id2),
                new StreamPosition(key2, id4)
            };

        var streams = db.StreamRead(streamList);

        // We expect an empty response.
        Assert.Empty(streams);
    }

    [Fact]
    public void StreamReadPastEndOfStream()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        db.StreamAdd(key, "field1", "value1");
        var id2 = db.StreamAdd(key, "field2", "value2");

        // Read after the final ID in the stream, we expect an empty array as a response.

        var entries = db.StreamRead(key, id2);

        Assert.Empty(entries);
    }

    [Fact]
    public void StreamReadRange()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        var id1 = db.StreamAdd(key, "field1", "value1");
        var id2 = db.StreamAdd(key, "field2", "value2");

        var entries = db.StreamRange(key);

        Assert.Equal(2, entries.Length);
        Assert.Equal(id1, entries[0].Id);
        Assert.Equal(id2, entries[1].Id);
    }

    [Fact]
    public void StreamReadRangeOfEmptyStream()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        var id1 = db.StreamAdd(key, "field1", "value1");
        var id2 = db.StreamAdd(key, "field2", "value2");

        var deleted = db.StreamDelete(key, new[] { id1, id2 });

        var entries = db.StreamRange(key);

        Assert.Equal(2, deleted);
        Assert.NotNull(entries);
        Assert.Empty(entries);
    }

    [Fact]
    public void StreamReadRangeWithCount()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        var id1 = db.StreamAdd(key, "field1", "value1");
        db.StreamAdd(key, "field2", "value2");

        var entries = db.StreamRange(key, count: 1);

        Assert.Single(entries);
        Assert.Equal(id1, entries[0].Id);
    }

    [Fact]
    public void StreamReadRangeReverse()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        var id1 = db.StreamAdd(key, "field1", "value1");
        var id2 = db.StreamAdd(key, "field2", "value2");

        var entries = db.StreamRange(key, messageOrder: Order.Descending);

        Assert.Equal(2, entries.Length);
        Assert.Equal(id2, entries[0].Id);
        Assert.Equal(id1, entries[1].Id);
    }

    [Fact]
    public void StreamReadRangeReverseWithCount()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        var id1 = db.StreamAdd(key, "field1", "value1");
        var id2 = db.StreamAdd(key, "field2", "value2");

        var entries = db.StreamRange(key, id1, id2, 1, Order.Descending);

        Assert.Single(entries);
        Assert.Equal(id2, entries[0].Id);
    }

    [Fact]
    public void StreamReadWithAfterIdAndCount_1()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        var id1 = db.StreamAdd(key, "field1", "value1");
        var id2 = db.StreamAdd(key, "field2", "value2");
        db.StreamAdd(key, "field3", "value3");

        // Only read a single item from the stream.
        var entries = db.StreamRead(key, id1, 1);

        Assert.Single(entries);
        Assert.Equal(id2, entries[0].Id);
    }

    [Fact]
    public void StreamReadWithAfterIdAndCount_2()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

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

    [Fact]
    public void StreamTrimLength()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

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

    [Fact]
    public void StreamVerifyLength()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();

        // Add a couple items and check length.
        db.StreamAdd(key, "field1", "value1");
        db.StreamAdd(key, "field2", "value2");

        var len = db.StreamLength(key);

        Assert.Equal(2, len);
    }

    [Fact]
    public async Task AddWithApproxCountAsync()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        await db.StreamAddAsync(key, "field", "value", maxLength: 10, useApproximateMaxLength: true, flags: CommandFlags.None).ConfigureAwait(false);
    }

    [Fact]
    public void AddWithApproxCount()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        db.StreamAdd(key, "field", "value", maxLength: 10, useApproximateMaxLength: true, flags: CommandFlags.None);
    }

    [Fact]
    public void StreamReadGroupWithNoAckShowsNoPendingMessages()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key = Me();
        const string groupName = "test_group",
                     consumer = "consumer";

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

    [Fact]
    public void StreamReadGroupMultiStreamWithNoAckShowsNoPendingMessages()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var key1 = Me() + "a";
        var key2 = Me() + "b";
        const string groupName = "test_group",
                     consumer = "consumer";

        db.StreamAdd(key1, "field1", "value1");
        db.StreamAdd(key1, "field2", "value2");

        db.StreamAdd(key2, "field3", "value3");
        db.StreamAdd(key2, "field4", "value4");

        db.StreamCreateConsumerGroup(key1, groupName, StreamPosition.NewMessages);
        db.StreamCreateConsumerGroup(key2, groupName, StreamPosition.NewMessages);

        db.StreamReadGroup(new[]
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

    [Fact]
    public async Task StreamReadIndexerUsage()
    {
        using var conn = Create(require: RedisFeatures.v5_0_0);

        var db = conn.GetDatabase();
        var streamName = Me();

        await db.StreamAddAsync(streamName, new[] {
                new NameValueEntry("x", "blah"),
                new NameValueEntry("msg", /*lang=json,strict*/ @"{""name"":""test"",""id"":123}"),
                new NameValueEntry("y", "more blah"),
            });

        var streamResult = await db.StreamRangeAsync(streamName, count: 1000);
        var evntJson = streamResult
            .Select(x => (dynamic?)JsonConvert.DeserializeObject(x["msg"]!))
            .ToList();
        var obj = Assert.Single(evntJson);
        Assert.Equal(123, (int)obj!.id);
        Assert.Equal("test", (string)obj.name);
    }
}
