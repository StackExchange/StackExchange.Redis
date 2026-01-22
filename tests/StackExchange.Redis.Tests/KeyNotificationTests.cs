using System;
using System.Buffers;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

public class KeyNotificationTests(ITestOutputHelper log)
{
    [Fact]
    public void Keyspace_Del_ParsesCorrectly()
    {
        // __keyspace@1__:mykey with payload "del"
        var channel = RedisChannel.Literal("__keyspace@1__:mykey");
        RedisValue value = "del";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);
        Assert.False(notification.IsKeyEvent);
        Assert.Equal(1, notification.Database);
        Assert.Equal(KeyNotificationType.Del, notification.Type);
        Assert.Equal("mykey", (string?)notification.GetKey());
        Assert.Equal(5, notification.KeyByteCount);
    }

    [Fact]
    public void Keyevent_Del_ParsesCorrectly()
    {
        // __keyevent@42__:del with value "mykey"
        var channel = RedisChannel.Literal("__keyevent@42__:del");
        RedisValue value = "mykey";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.False(notification.IsKeySpace);
        Assert.True(notification.IsKeyEvent);
        Assert.Equal(42, notification.Database);
        Assert.Equal(KeyNotificationType.Del, notification.Type);
        Assert.Equal("mykey", (string?)notification.GetKey());
        Assert.Equal(5, notification.KeyByteCount);
    }

    [Fact]
    public void Keyspace_Set_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyspace@0__:testkey");
        RedisValue value = "set";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);
        Assert.Equal(0, notification.Database);
        Assert.Equal(KeyNotificationType.Set, notification.Type);
        Assert.Equal("testkey", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyevent_Expire_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyevent@5__:expire");
        RedisValue value = "session:12345";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeyEvent);
        Assert.Equal(5, notification.Database);
        Assert.Equal(KeyNotificationType.Expire, notification.Type);
        Assert.Equal("session:12345", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyspace_Expired_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyspace@3__:cache:item");
        RedisValue value = "expired";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);
        Assert.Equal(3, notification.Database);
        Assert.Equal(KeyNotificationType.Expired, notification.Type);
        Assert.Equal("cache:item", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyevent_LPush_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyevent@0__:lpush");
        RedisValue value = "queue:tasks";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeyEvent);
        Assert.Equal(0, notification.Database);
        Assert.Equal(KeyNotificationType.LPush, notification.Type);
        Assert.Equal("queue:tasks", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyspace_HSet_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyspace@2__:user:1000");
        RedisValue value = "hset";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);
        Assert.Equal(2, notification.Database);
        Assert.Equal(KeyNotificationType.HSet, notification.Type);
        Assert.Equal("user:1000", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyevent_ZAdd_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyevent@7__:zadd");
        RedisValue value = "leaderboard";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeyEvent);
        Assert.Equal(7, notification.Database);
        Assert.Equal(KeyNotificationType.ZAdd, notification.Type);
        Assert.Equal("leaderboard", (string?)notification.GetKey());
    }

    [Fact]
    public void TryCopyKey_WorksCorrectly()
    {
        var channel = RedisChannel.Literal("__keyspace@0__:testkey");
        RedisValue value = "set";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        var lease = ArrayPool<byte>.Shared.Rent(20);
        Span<byte> buffer = lease.AsSpan(0, 20);
        Assert.True(notification.TryCopyKey(buffer, out var bytesWritten));
        Assert.Equal(7, bytesWritten);
        Assert.Equal("testkey", Encoding.UTF8.GetString(lease, 0, bytesWritten));
        ArrayPool<byte>.Shared.Return(lease);
    }

    [Fact]
    public void TryCopyKey_FailsWithSmallBuffer()
    {
        var channel = RedisChannel.Literal("__keyspace@0__:testkey");
        RedisValue value = "set";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Span<byte> buffer = stackalloc byte[3]; // too small
        Assert.False(notification.TryCopyKey(buffer, out var bytesWritten));
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void InvalidChannel_ReturnsFalse()
    {
        var channel = RedisChannel.Literal("regular:channel");
        RedisValue value = "data";

        Assert.False(KeyNotification.TryParse(in channel, in value, out var notification));
    }

    [Fact]
    public void InvalidKeyspaceChannel_MissingDelimiter_ReturnsFalse()
    {
        var channel = RedisChannel.Literal("__keyspace@0__"); // missing the key part
        RedisValue value = "set";

        Assert.False(KeyNotification.TryParse(in channel, in value, out var notification));
    }

    [Fact]
    public void Keyspace_UnknownEventType_ReturnsUnknown()
    {
        var channel = RedisChannel.Literal("__keyspace@0__:mykey");
        RedisValue value = "unknownevent";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);
        Assert.Equal(0, notification.Database);
        Assert.Equal(KeyNotificationType.Unknown, notification.Type);
        Assert.Equal("mykey", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyevent_UnknownEventType_ReturnsUnknown()
    {
        var channel = RedisChannel.Literal("__keyevent@0__:unknownevent");
        RedisValue value = "mykey";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeyEvent);
        Assert.Equal(0, notification.Database);
        Assert.Equal(KeyNotificationType.Unknown, notification.Type);
        Assert.Equal("mykey", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyspace_WithColonInKey_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyspace@0__:user:session:12345");
        RedisValue value = "del";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);
        Assert.Equal(0, notification.Database);
        Assert.Equal(KeyNotificationType.Del, notification.Type);
        Assert.Equal("user:session:12345", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyevent_Evicted_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyevent@1__:evicted");
        RedisValue value = "cache:old";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeyEvent);
        Assert.Equal(1, notification.Database);
        Assert.Equal(KeyNotificationType.Evicted, notification.Type);
        Assert.Equal("cache:old", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyspace_New_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyspace@0__:newkey");
        RedisValue value = "new";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);
        Assert.Equal(0, notification.Database);
        Assert.Equal(KeyNotificationType.New, notification.Type);
        Assert.Equal("newkey", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyevent_XGroupCreate_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyevent@0__:xgroup-create");
        RedisValue value = "mystream";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeyEvent);
        Assert.Equal(0, notification.Database);
        Assert.Equal(KeyNotificationType.XGroupCreate, notification.Type);
        Assert.Equal("mystream", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyspace_TypeChanged_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyspace@0__:mykey");
        RedisValue value = "type_changed";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);
        Assert.Equal(0, notification.Database);
        Assert.Equal(KeyNotificationType.TypeChanged, notification.Type);
        Assert.Equal("mykey", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyevent_HighDatabaseNumber_ParsesCorrectly()
    {
        var channel = RedisChannel.Literal("__keyevent@999__:set");
        RedisValue value = "testkey";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeyEvent);
        Assert.Equal(999, notification.Database);
        Assert.Equal(KeyNotificationType.Set, notification.Type);
        Assert.Equal("testkey", (string?)notification.GetKey());
    }

    [Fact]
    public void Keyevent_NonIntegerDatabase_ParsesWellEnough()
    {
        var channel = RedisChannel.Literal("__keyevent@abc__:set");
        RedisValue value = "testkey";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeyEvent);
        Assert.Equal(-1, notification.Database);
        Assert.Equal(KeyNotificationType.Set, notification.Type);
        Assert.Equal("testkey", (string?)notification.GetKey());
    }

    [Fact]
    public void DefaultKeyNotification_HasExpectedProperties()
    {
        var notification = default(KeyNotification);

        Assert.False(notification.IsKeySpace);
        Assert.False(notification.IsKeyEvent);
        Assert.Equal(-1, notification.Database);
        Assert.Equal(KeyNotificationType.Unknown, notification.Type);
        Assert.True(notification.GetKey().IsNull);
        Assert.Equal(0, notification.KeyByteCount);
        Assert.True(notification.Channel.IsNull);
        Assert.True(notification.Value.IsNull);

        // TryCopyKey should return false and write 0 bytes
        Span<byte> buffer = stackalloc byte[10];
        Assert.False(notification.TryCopyKey(buffer, out var bytesWritten));
        Assert.Equal(0, bytesWritten);
    }

    [Theory]
    [InlineData(KeyNotificationTypeFastHash.append.Text, KeyNotificationType.Append)]
    [InlineData(KeyNotificationTypeFastHash.copy.Text, KeyNotificationType.Copy)]
    [InlineData(KeyNotificationTypeFastHash.del.Text, KeyNotificationType.Del)]
    [InlineData(KeyNotificationTypeFastHash.expire.Text, KeyNotificationType.Expire)]
    [InlineData(KeyNotificationTypeFastHash.hdel.Text, KeyNotificationType.HDel)]
    [InlineData(KeyNotificationTypeFastHash.hexpired.Text, KeyNotificationType.HExpired)]
    [InlineData(KeyNotificationTypeFastHash.hincrbyfloat.Text, KeyNotificationType.HIncrByFloat)]
    [InlineData(KeyNotificationTypeFastHash.hincrby.Text, KeyNotificationType.HIncrBy)]
    [InlineData(KeyNotificationTypeFastHash.hpersist.Text, KeyNotificationType.HPersist)]
    [InlineData(KeyNotificationTypeFastHash.hset.Text, KeyNotificationType.HSet)]
    [InlineData(KeyNotificationTypeFastHash.incrbyfloat.Text, KeyNotificationType.IncrByFloat)]
    [InlineData(KeyNotificationTypeFastHash.incrby.Text, KeyNotificationType.IncrBy)]
    [InlineData(KeyNotificationTypeFastHash.linsert.Text, KeyNotificationType.LInsert)]
    [InlineData(KeyNotificationTypeFastHash.lpop.Text, KeyNotificationType.LPop)]
    [InlineData(KeyNotificationTypeFastHash.lpush.Text, KeyNotificationType.LPush)]
    [InlineData(KeyNotificationTypeFastHash.lrem.Text, KeyNotificationType.LRem)]
    [InlineData(KeyNotificationTypeFastHash.lset.Text, KeyNotificationType.LSet)]
    [InlineData(KeyNotificationTypeFastHash.ltrim.Text, KeyNotificationType.LTrim)]
    [InlineData(KeyNotificationTypeFastHash.move_from.Text, KeyNotificationType.MoveFrom)]
    [InlineData(KeyNotificationTypeFastHash.move_to.Text, KeyNotificationType.MoveTo)]
    [InlineData(KeyNotificationTypeFastHash.persist.Text, KeyNotificationType.Persist)]
    [InlineData(KeyNotificationTypeFastHash.rename_from.Text, KeyNotificationType.RenameFrom)]
    [InlineData(KeyNotificationTypeFastHash.rename_to.Text, KeyNotificationType.RenameTo)]
    [InlineData(KeyNotificationTypeFastHash.restore.Text, KeyNotificationType.Restore)]
    [InlineData(KeyNotificationTypeFastHash.rpop.Text, KeyNotificationType.RPop)]
    [InlineData(KeyNotificationTypeFastHash.rpush.Text, KeyNotificationType.RPush)]
    [InlineData(KeyNotificationTypeFastHash.sadd.Text, KeyNotificationType.SAdd)]
    [InlineData(KeyNotificationTypeFastHash.set.Text, KeyNotificationType.Set)]
    [InlineData(KeyNotificationTypeFastHash.setrange.Text, KeyNotificationType.SetRange)]
    [InlineData(KeyNotificationTypeFastHash.sortstore.Text, KeyNotificationType.SortStore)]
    [InlineData(KeyNotificationTypeFastHash.srem.Text, KeyNotificationType.SRem)]
    [InlineData(KeyNotificationTypeFastHash.spop.Text, KeyNotificationType.SPop)]
    [InlineData(KeyNotificationTypeFastHash.xadd.Text, KeyNotificationType.XAdd)]
    [InlineData(KeyNotificationTypeFastHash.xdel.Text, KeyNotificationType.XDel)]
    [InlineData(KeyNotificationTypeFastHash.xgroup_createconsumer.Text, KeyNotificationType.XGroupCreateConsumer)]
    [InlineData(KeyNotificationTypeFastHash.xgroup_create.Text, KeyNotificationType.XGroupCreate)]
    [InlineData(KeyNotificationTypeFastHash.xgroup_delconsumer.Text, KeyNotificationType.XGroupDelConsumer)]
    [InlineData(KeyNotificationTypeFastHash.xgroup_destroy.Text, KeyNotificationType.XGroupDestroy)]
    [InlineData(KeyNotificationTypeFastHash.xgroup_setid.Text, KeyNotificationType.XGroupSetId)]
    [InlineData(KeyNotificationTypeFastHash.xsetid.Text, KeyNotificationType.XSetId)]
    [InlineData(KeyNotificationTypeFastHash.xtrim.Text, KeyNotificationType.XTrim)]
    [InlineData(KeyNotificationTypeFastHash.zadd.Text, KeyNotificationType.ZAdd)]
    [InlineData(KeyNotificationTypeFastHash.zdiffstore.Text, KeyNotificationType.ZDiffStore)]
    [InlineData(KeyNotificationTypeFastHash.zinterstore.Text, KeyNotificationType.ZInterStore)]
    [InlineData(KeyNotificationTypeFastHash.zunionstore.Text, KeyNotificationType.ZUnionStore)]
    [InlineData(KeyNotificationTypeFastHash.zincr.Text, KeyNotificationType.ZIncr)]
    [InlineData(KeyNotificationTypeFastHash.zrembyrank.Text, KeyNotificationType.ZRemByRank)]
    [InlineData(KeyNotificationTypeFastHash.zrembyscore.Text, KeyNotificationType.ZRemByScore)]
    [InlineData(KeyNotificationTypeFastHash.zrem.Text, KeyNotificationType.ZRem)]
    [InlineData(KeyNotificationTypeFastHash.expired.Text, KeyNotificationType.Expired)]
    [InlineData(KeyNotificationTypeFastHash.evicted.Text, KeyNotificationType.Evicted)]
    [InlineData(KeyNotificationTypeFastHash._new.Text, KeyNotificationType.New)]
    [InlineData(KeyNotificationTypeFastHash.overwritten.Text, KeyNotificationType.Overwritten)]
    [InlineData(KeyNotificationTypeFastHash.type_changed.Text, KeyNotificationType.TypeChanged)]
    public unsafe void FastHashParse_AllKnownValues_ParseCorrectly(string raw, KeyNotificationType parsed)
    {
        var arr = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(raw.Length));
        int bytes;
        fixed (byte* bPtr = arr) // encode into the buffer
        {
            fixed (char* cPtr = raw)
            {
                bytes = Encoding.UTF8.GetBytes(cPtr, raw.Length, bPtr, arr.Length);
            }
        }

        var result = KeyNotificationTypeFastHash.Parse(arr.AsSpan(0, bytes));
        log.WriteLine($"Parsed '{raw}' as {result}");
        Assert.Equal(parsed, result);

        // and the other direction:
        var fetchedBytes = KeyNotificationTypeFastHash.GetRawBytes(parsed);
        string fetched;
        fixed (byte* bPtr = fetchedBytes)
        {
            fetched = Encoding.UTF8.GetString(bPtr, fetchedBytes.Length);
        }

        log.WriteLine($"Fetched '{raw}'");
        Assert.Equal(raw, fetched);

        ArrayPool<byte>.Shared.Return(arr);
    }

    [Fact]
    public void CreateKeySpaceNotification_Valid()
    {
        var channel = RedisChannel.KeySpace("abc", 42);
        Assert.Equal("__keyspace@42__:abc", channel.ToString());
        Assert.False(channel.IsMultiNode);
        Assert.False(channel.IsSharded);
        Assert.False(channel.IsPattern);
    }

    [Theory]
    [InlineData(null, null, "__keyspace@*__:*")]
    [InlineData("abc*", null, "__keyspace@*__:abc*")]
    [InlineData(null, 42, "__keyspace@42__:*")]
    [InlineData("abc*", 42, "__keyspace@42__:abc*")]
    public void CreateKeySpaceNotificationPattern(string? pattern, int? database, string expected)
    {
        var channel = RedisChannel.KeySpacePattern(pattern, database);
        Assert.Equal(expected, channel.ToString());
        Assert.True(channel.IsMultiNode);
        Assert.False(channel.IsSharded);
        Assert.True(channel.IsPattern);
    }

    [Theory]
    [InlineData(KeyNotificationType.Set, null, "__keyevent@*__:set", true)]
    [InlineData(KeyNotificationType.XGroupCreate, null, "__keyevent@*__:xgroup-create", true)]
    [InlineData(KeyNotificationType.Set, 42, "__keyevent@42__:set", false)]
    [InlineData(KeyNotificationType.XGroupCreate, 42, "__keyevent@42__:xgroup-create", false)]
    public void CreateKeyEventNotification(KeyNotificationType type, int? database, string expected, bool isPattern)
    {
        var channel = RedisChannel.KeyEvent(type, database);
        Assert.Equal(expected, channel.ToString());
        Assert.True(channel.IsMultiNode);
        Assert.False(channel.IsSharded);
        if (isPattern)
        {
            Assert.True(channel.IsPattern);
        }
        else
        {
            Assert.False(channel.IsPattern);
        }
    }
}
