using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Xunit.Sdk;

namespace StackExchange.Redis.Tests;

public class KeyNotificationTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData("foo", "foo")]
    [InlineData("__foo__", "__foo__")]
    [InlineData("__keyspace@4__:", "__keyspace@4__:")] // not long enough
    [InlineData("__keyspace@4__:f", "f")]
    [InlineData("__keyspace@4__:fo", "fo")]
    [InlineData("__keyspace@4__:foo", "foo")]
    [InlineData("__keyspace@42__:foo", "foo")] // check multi-char db
    [InlineData("__keyevent@4__:foo", "__keyevent@4__:foo")] // key-event
    [InlineData("__keyevent@42__:foo", "__keyevent@42__:foo")] // key-event
    public void RoutingSpan_StripKeySpacePrefix(string raw, string routed)
    {
        ReadOnlySpan<byte> srcBytes = Encoding.UTF8.GetBytes(raw);
        var strippedBytes = RedisChannel.StripKeySpacePrefix(srcBytes);
        var result = Encoding.UTF8.GetString(strippedBytes);
        Assert.Equal(routed, result);
    }

    [Fact]
    public void Keyspace_Del_ParsesCorrectly()
    {
        // __keyspace@1__:mykey with payload "del"
        var channel = RedisChannel.Literal("__keyspace@1__:mykey");
        Assert.False(channel.IgnoreChannelPrefix); // because constructed manually
        RedisValue value = "del";

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);
        Assert.False(notification.IsKeyEvent);
        Assert.Equal(1, notification.Database);
        Assert.Equal(KeyNotificationType.Del, notification.Type);
        Assert.True(notification.IsType("del"u8));
        Assert.Equal("mykey", (string?)notification.GetKey());
        Assert.Equal(5, notification.GetKeyByteCount());
        Assert.Equal(5, notification.GetKeyMaxByteCount());
        Assert.Equal(5, notification.GetKeyCharCount());
        Assert.Equal(6, notification.GetKeyMaxCharCount());
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
        Assert.True(notification.IsType("del"u8));
        Assert.Equal("mykey", (string?)notification.GetKey());
        Assert.Equal(5, notification.GetKeyByteCount());
        Assert.Equal(18, notification.GetKeyMaxByteCount());
        Assert.Equal(5, notification.GetKeyCharCount());
        Assert.Equal(5, notification.GetKeyMaxCharCount());
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
        Assert.True(notification.IsType("set"u8));
        Assert.Equal("testkey", (string?)notification.GetKey());
        Assert.Equal(7, notification.GetKeyByteCount());
        Assert.Equal(7, notification.GetKeyMaxByteCount());
        Assert.Equal(7, notification.GetKeyCharCount());
        Assert.Equal(8, notification.GetKeyMaxCharCount());
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
        Assert.True(notification.IsType("expire"u8));
        Assert.Equal("session:12345", (string?)notification.GetKey());
        Assert.Equal(13, notification.GetKeyByteCount());
        Assert.Equal(42, notification.GetKeyMaxByteCount());
        Assert.Equal(13, notification.GetKeyCharCount());
        Assert.Equal(13, notification.GetKeyMaxCharCount());
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
        Assert.True(notification.IsType("expired"u8));
        Assert.Equal("cache:item", (string?)notification.GetKey());
        Assert.Equal(10, notification.GetKeyByteCount());
        Assert.Equal(10, notification.GetKeyMaxByteCount());
        Assert.Equal(10, notification.GetKeyCharCount());
        Assert.Equal(11, notification.GetKeyMaxCharCount());
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
        Assert.True(notification.IsType("lpush"u8));
        Assert.Equal("queue:tasks", (string?)notification.GetKey());
        Assert.Equal(11, notification.GetKeyByteCount());
        Assert.Equal(36, notification.GetKeyMaxByteCount());
        Assert.Equal(11, notification.GetKeyCharCount());
        Assert.Equal(11, notification.GetKeyMaxCharCount());
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
        Assert.True(notification.IsType("hset"u8));
        Assert.Equal("user:1000", (string?)notification.GetKey());
        Assert.Equal(9, notification.GetKeyByteCount());
        Assert.Equal(9, notification.GetKeyMaxByteCount());
        Assert.Equal(9, notification.GetKeyCharCount());
        Assert.Equal(10, notification.GetKeyMaxCharCount());
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
        Assert.True(notification.IsType("zadd"u8));
        Assert.Equal("leaderboard", (string?)notification.GetKey());
        Assert.Equal(11, notification.GetKeyByteCount());
        Assert.Equal(36, notification.GetKeyMaxByteCount());
        Assert.Equal(11, notification.GetKeyCharCount());
        Assert.Equal(11, notification.GetKeyMaxCharCount());
    }

    [Fact]
    public void CustomEventWithUnusualValue_Works()
    {
        var channel = RedisChannel.Literal("__keyevent@7__:flooble");
        RedisValue value = 17.5;

        Assert.True(KeyNotification.TryParse(in channel, in value, out var notification));

        Assert.True(notification.IsKeyEvent);
        Assert.Equal(7, notification.Database);
        Assert.Equal(KeyNotificationType.Unknown, notification.Type);
        Assert.False(notification.IsType("zadd"u8));
        Assert.True(notification.IsType("flooble"u8));
        Assert.Equal("17.5", (string?)notification.GetKey());
        Assert.Equal(4, notification.GetKeyByteCount());
        Assert.Equal(40, notification.GetKeyMaxByteCount());
        Assert.Equal(4, notification.GetKeyCharCount());
        Assert.Equal(40, notification.GetKeyMaxCharCount());
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
        Assert.False(notification.IsType("del"u8));
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
        Assert.False(notification.IsType("del"u8));
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
        Assert.True(notification.IsType("del"u8));
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
        Assert.True(notification.IsType("evicted"u8));
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
        Assert.True(notification.IsType("new"u8));
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
        Assert.True(notification.IsType("xgroup-create"u8));
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
        Assert.True(notification.IsType("type_changed"u8));
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
        Assert.True(notification.IsType("set"u8));
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
        Assert.True(notification.IsType("set"u8));
        Assert.Equal("testkey", (string?)notification.GetKey());
    }

    [Fact]
    public void DefaultKeyNotification_HasExpectedProperties()
    {
        var notification = default(KeyNotification);

        Assert.False(notification.IsKeySpace);
        Assert.False(notification.IsKeyEvent);
        Assert.False(notification.IsSubKeySpace);
        Assert.False(notification.IsSubKeyEvent);
        Assert.False(notification.IsSubKeySpaceItem);
        Assert.False(notification.IsSubKeySpaceEvent);
        Assert.Equal(-1, notification.Database);
        Assert.Equal(KeyNotificationType.Unknown, notification.Type);
        Assert.False(notification.IsType("del"u8));
        Assert.True(notification.GetKey().IsNull);
        Assert.True(notification.GetSubKey().IsNull);
        Assert.Equal(0, notification.GetKeyByteCount());
        Assert.Equal(0, notification.GetKeyMaxByteCount());
        Assert.Equal(0, notification.GetKeyCharCount());
        Assert.Equal(0, notification.GetKeyMaxCharCount());
        Assert.True(notification.GetChannel().IsNull);
        Assert.True(notification.GetValue().IsNull);

        // TryCopyKey should return false and write 0 bytes
        Span<byte> buffer = stackalloc byte[10];
        Assert.False(notification.TryCopyKey(buffer, out var bytesWritten));
        Assert.Equal(0, bytesWritten);
    }

    [Theory]
    [InlineData("append", KeyNotificationType.Append)]
    [InlineData("copy", KeyNotificationType.Copy)]
    [InlineData("del", KeyNotificationType.Del)]
    [InlineData("expire", KeyNotificationType.Expire)]
    [InlineData("hdel", KeyNotificationType.HDel)]
    [InlineData("hexpired", KeyNotificationType.HExpired)]
    [InlineData("hincrbyfloat", KeyNotificationType.HIncrByFloat)]
    [InlineData("hincrby", KeyNotificationType.HIncrBy)]
    [InlineData("hpersist", KeyNotificationType.HPersist)]
    [InlineData("hset", KeyNotificationType.HSet)]
    [InlineData("incrbyfloat", KeyNotificationType.IncrByFloat)]
    [InlineData("incrby", KeyNotificationType.IncrBy)]
    [InlineData("linsert", KeyNotificationType.LInsert)]
    [InlineData("lpop", KeyNotificationType.LPop)]
    [InlineData("lpush", KeyNotificationType.LPush)]
    [InlineData("lrem", KeyNotificationType.LRem)]
    [InlineData("lset", KeyNotificationType.LSet)]
    [InlineData("ltrim", KeyNotificationType.LTrim)]
    [InlineData("move_from", KeyNotificationType.MoveFrom)]
    [InlineData("move_to", KeyNotificationType.MoveTo)]
    [InlineData("persist", KeyNotificationType.Persist)]
    [InlineData("rename_from", KeyNotificationType.RenameFrom)]
    [InlineData("rename_to", KeyNotificationType.RenameTo)]
    [InlineData("restore", KeyNotificationType.Restore)]
    [InlineData("rpop", KeyNotificationType.RPop)]
    [InlineData("rpush", KeyNotificationType.RPush)]
    [InlineData("sadd", KeyNotificationType.SAdd)]
    [InlineData("set", KeyNotificationType.Set)]
    [InlineData("setrange", KeyNotificationType.SetRange)]
    [InlineData("sortstore", KeyNotificationType.SortStore)]
    [InlineData("srem", KeyNotificationType.SRem)]
    [InlineData("spop", KeyNotificationType.SPop)]
    [InlineData("xadd", KeyNotificationType.XAdd)]
    [InlineData("xdel", KeyNotificationType.XDel)]
    [InlineData("xgroup-createconsumer", KeyNotificationType.XGroupCreateConsumer)]
    [InlineData("xgroup-create", KeyNotificationType.XGroupCreate)]
    [InlineData("xgroup-delconsumer", KeyNotificationType.XGroupDelConsumer)]
    [InlineData("xgroup-destroy", KeyNotificationType.XGroupDestroy)]
    [InlineData("xgroup-setid", KeyNotificationType.XGroupSetId)]
    [InlineData("xsetid", KeyNotificationType.XSetId)]
    [InlineData("xtrim", KeyNotificationType.XTrim)]
    [InlineData("zadd", KeyNotificationType.ZAdd)]
    [InlineData("zdiffstore", KeyNotificationType.ZDiffStore)]
    [InlineData("zinterstore", KeyNotificationType.ZInterStore)]
    [InlineData("zunionstore", KeyNotificationType.ZUnionStore)]
    [InlineData("zincr", KeyNotificationType.ZIncr)]
    [InlineData("zrembyrank", KeyNotificationType.ZRemByRank)]
    [InlineData("zrembyscore", KeyNotificationType.ZRemByScore)]
    [InlineData("zrem", KeyNotificationType.ZRem)]
    [InlineData("hexpire", KeyNotificationType.HExpire)]
    [InlineData("expired", KeyNotificationType.Expired)]
    [InlineData("evicted", KeyNotificationType.Evicted)]
    [InlineData("new", KeyNotificationType.New)]
    [InlineData("overwritten", KeyNotificationType.Overwritten)]
    [InlineData("type_changed", KeyNotificationType.TypeChanged)]
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

        var result = KeyNotificationTypeMetadata.Parse(arr.AsSpan(0, bytes));
        log.WriteLine($"Parsed '{raw}' as {result}");
        Assert.Equal(parsed, result);

        // and the other direction:
        var fetchedBytes = KeyNotificationTypeMetadata.GetRawBytes(parsed);
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
        var channel = RedisChannel.KeySpaceSingleKey("abc", 42);
        Assert.Equal("__keyspace@42__:abc", channel.ToString());
        Assert.False(channel.IsMultiNode);
        Assert.True(channel.IsKeyRouted);
        Assert.False(channel.IsSharded);
        Assert.False(channel.IsPattern);
        Assert.True(channel.IgnoreChannelPrefix);
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
        Assert.False(channel.IsKeyRouted);
        Assert.False(channel.IsSharded);
        Assert.True(channel.IsPattern);
        Assert.True(channel.IgnoreChannelPrefix);
    }

    [Theory]
    [InlineData("abc", null, "__keyspace@*__:abc*")]
    [InlineData("abc", 42, "__keyspace@42__:abc*")]
    public void CreateKeySpaceNotificationPrefix_Key(string prefix, int? database, string expected)
    {
        var channel = RedisChannel.KeySpacePrefix((RedisKey)prefix, database);
        Assert.Equal(expected, channel.ToString());
        Assert.True(channel.IsMultiNode);
        Assert.False(channel.IsKeyRouted);
        Assert.False(channel.IsSharded);
        Assert.True(channel.IsPattern);
        Assert.True(channel.IgnoreChannelPrefix);
    }

    [Theory]
    [InlineData("abc", null, "__keyspace@*__:abc*")]
    [InlineData("abc", 42, "__keyspace@42__:abc*")]
    public void CreateKeySpaceNotificationPrefix_Span(string prefix, int? database, string expected)
    {
        var channel = RedisChannel.KeySpacePrefix((ReadOnlySpan<byte>)Encoding.UTF8.GetBytes(prefix), database);
        Assert.Equal(expected, channel.ToString());
        Assert.True(channel.IsMultiNode);
        Assert.False(channel.IsKeyRouted);
        Assert.False(channel.IsSharded);
        Assert.True(channel.IsPattern);
        Assert.True(channel.IgnoreChannelPrefix);
    }

    [Theory]
    [InlineData("a?bc", null)]
    [InlineData("a?bc", 42)]
    [InlineData("a*bc", null)]
    [InlineData("a*bc", 42)]
    [InlineData("a[bc", null)]
    [InlineData("a[bc", 42)]
    public void CreateKeySpaceNotificationPrefix_DisallowGlob(string prefix, int? database)
    {
        var bytes = Encoding.UTF8.GetBytes(prefix);
        var ex = Assert.Throws<ArgumentException>(() =>
            RedisChannel.KeySpacePrefix((RedisKey)bytes, database));
        Assert.StartsWith("The supplied key contains pattern characters, but patterns are not supported in this context.", ex.Message);

        ex = Assert.Throws<ArgumentException>(() =>
            RedisChannel.KeySpacePrefix((ReadOnlySpan<byte>)bytes, database));
        Assert.StartsWith("The supplied key contains pattern characters, but patterns are not supported in this context.", ex.Message);
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
        Assert.False(channel.IsKeyRouted);
        Assert.False(channel.IsSharded);
        Assert.True(channel.IgnoreChannelPrefix);
        if (isPattern)
        {
            Assert.True(channel.IsPattern);
        }
        else
        {
            Assert.False(channel.IsPattern);
        }
    }

    [Theory]
    [InlineData("abc", "__keyspace@42__:abc")]
    [InlineData("a*bc", "__keyspace@42__:a*bc")] // pattern-like is allowed, since not using PSUBSCRIBE
    public void Cannot_KeyRoute_KeySpace_SingleKeyIsKeyRouted(string key, string pattern)
    {
        var channel = RedisChannel.KeySpaceSingleKey(key, 42);
        Assert.Equal(pattern, channel.ToString());
        Assert.False(channel.IsMultiNode);
        Assert.False(channel.IsPattern);
        Assert.False(channel.IsSharded);
        Assert.True(channel.IgnoreChannelPrefix);
        Assert.True(channel.IsKeyRouted);
        Assert.True(channel.WithKeyRouting().IsKeyRouted); // no change, still key-routed
        Assert.Equal(RedisCommand.PUBLISH, channel.GetPublishCommand());
    }

    [Fact]
    public void Cannot_KeyRoute_KeySpacePattern()
    {
        var channel = RedisChannel.KeySpacePattern("abc", 42);
        Assert.True(channel.IsMultiNode);
        Assert.False(channel.IsKeyRouted);
        Assert.True(channel.IgnoreChannelPrefix);
        Assert.StartsWith("Key routing is not supported for multi-node channels", Assert.Throws<InvalidOperationException>(() => channel.WithKeyRouting()).Message);
        Assert.StartsWith("Publishing is not supported for multi-node channels", Assert.Throws<InvalidOperationException>(() => channel.GetPublishCommand()).Message);
    }

    [Fact]
    public void Cannot_KeyRoute_KeyEvent()
    {
        var channel = RedisChannel.KeyEvent(KeyNotificationType.Set, 42);
        Assert.True(channel.IsMultiNode);
        Assert.False(channel.IsKeyRouted);
        Assert.True(channel.IgnoreChannelPrefix);
        Assert.StartsWith("Key routing is not supported for multi-node channels", Assert.Throws<InvalidOperationException>(() => channel.WithKeyRouting()).Message);
        Assert.StartsWith("Publishing is not supported for multi-node channels", Assert.Throws<InvalidOperationException>(() => channel.GetPublishCommand()).Message);
    }

    [Fact]
    public void Cannot_KeyRoute_KeyEvent_Custom()
    {
        var channel = RedisChannel.KeyEvent("foo"u8, 42);
        Assert.True(channel.IsMultiNode);
        Assert.False(channel.IsKeyRouted);
        Assert.True(channel.IgnoreChannelPrefix);
        Assert.StartsWith("Key routing is not supported for multi-node channels", Assert.Throws<InvalidOperationException>(() => channel.WithKeyRouting()).Message);
        Assert.StartsWith("Publishing is not supported for multi-node channels", Assert.Throws<InvalidOperationException>(() => channel.GetPublishCommand()).Message);
    }

    [Fact]
    public void KeyEventPrefix_KeySpacePrefix_Length_Matches()
    {
        // this is a sanity check for the parsing step in KeyNotification.TryParse
        Assert.Equal(KeyNotificationChannels.KeySpacePrefix.Length, KeyNotificationChannels.KeyEventPrefix.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyNotificationKeyStripping(bool asString)
    {
        Span<byte> blob = stackalloc byte[32];
        Span<char> clob = stackalloc char[32];

        RedisChannel channel = RedisChannel.Literal("__keyevent@0__:sadd");
        RedisValue value = asString ? "mykey:abc" : "mykey:abc"u8.ToArray();
        KeyNotification.TryParse(in channel, in value, out var notification);
        Assert.Equal("mykey:abc", (string?)notification.GetKey());
        Assert.True(notification.KeyStartsWith("mykey:"u8));
        Assert.Equal(0, notification.KeyOffset);

        Assert.Equal(9, notification.GetKeyByteCount());
        Assert.Equal(asString ? 30 : 9, notification.GetKeyMaxByteCount());
        Assert.Equal(9, notification.GetKeyCharCount());
        Assert.Equal(asString ? 9 : 10, notification.GetKeyMaxCharCount());

        Assert.True(notification.TryCopyKey(blob, out var bytesWritten));
        Assert.Equal(9, bytesWritten);
        Assert.Equal("mykey:abc", Encoding.UTF8.GetString(blob.Slice(0, bytesWritten)));

        Assert.True(notification.TryCopyKey(clob, out var charsWritten));
        Assert.Equal(9, charsWritten);
        Assert.Equal("mykey:abc", clob.Slice(0, charsWritten).ToString());

        // now with a prefix
        notification = notification.WithKeySlice("mykey:"u8.Length);
        Assert.Equal("abc", (string?)notification.GetKey());
        Assert.False(notification.KeyStartsWith("mykey:"u8));
        Assert.Equal(6, notification.KeyOffset);

        Assert.Equal(3, notification.GetKeyByteCount());
        Assert.Equal(asString ? 24 : 3, notification.GetKeyMaxByteCount());
        Assert.Equal(3, notification.GetKeyCharCount());
        Assert.Equal(asString ? 3 : 4, notification.GetKeyMaxCharCount());

        Assert.True(notification.TryCopyKey(blob, out bytesWritten));
        Assert.Equal(3, bytesWritten);
        Assert.Equal("abc", Encoding.UTF8.GetString(blob.Slice(0, bytesWritten)));

        Assert.True(notification.TryCopyKey(clob, out charsWritten));
        Assert.Equal(3, charsWritten);
        Assert.Equal("abc", clob.Slice(0, charsWritten).ToString());
    }

    [Fact]
    public void SubKeySpace_HSet_ParsesCorrectly()
    {
        // __subkeyspace@4__:mykey with payload hset|6:field1
        var channel = RedisChannel.Literal("__subkeyspace@4__:mykey");
        RedisValue value = "hset|6:field1";

        Assert.True(KeyNotification.TryParse(channel, value, out var notification));

        Assert.False(notification.IsKeySpace);
        Assert.False(notification.IsKeyEvent);
        Assert.True(notification.IsSubKeySpace);
        Assert.False(notification.IsSubKeyEvent);
        Assert.False(notification.IsSubKeySpaceItem);
        Assert.False(notification.IsSubKeySpaceEvent);

        Assert.Equal(4, notification.Database);
        Assert.Equal(KeyNotificationType.HSet, notification.Type);
        Assert.True(notification.IsType("hset"u8));
        Assert.Equal("mykey", (string?)notification.GetKey());
        Assert.Equal("field1", (string?)notification.GetSubKey());
    }

    [Fact]
    public void SubKeyEvent_HSet_ParsesCorrectly()
    {
        // __subkeyevent@4__:hset with payload 5:mykey|6:field1
        var channel = RedisChannel.Literal("__subkeyevent@4__:hset");
        RedisValue value = "5:mykey|6:field1";

        Assert.True(KeyNotification.TryParse(channel, value, out var notification));

        Assert.False(notification.IsKeySpace);
        Assert.False(notification.IsKeyEvent);
        Assert.False(notification.IsSubKeySpace);
        Assert.True(notification.IsSubKeyEvent);
        Assert.False(notification.IsSubKeySpaceItem);
        Assert.False(notification.IsSubKeySpaceEvent);

        Assert.Equal(4, notification.Database);
        Assert.Equal(KeyNotificationType.HSet, notification.Type);
        Assert.True(notification.IsType("hset"u8));
        Assert.Equal("mykey", (string?)notification.GetKey());
        Assert.Equal("field1", (string?)notification.GetSubKey());
    }

    [Fact]
    public void SubKeySpaceItem_HSet_ParsesCorrectly()
    {
        // __subkeyspaceitem@4__:mykey\nfield1 with payload hset
        var channel = RedisChannel.Literal("__subkeyspaceitem@4__:mykey\nfield1");
        RedisValue value = "hset";

        Assert.True(KeyNotification.TryParse(channel, value, out var notification));

        Assert.False(notification.IsKeySpace);
        Assert.False(notification.IsKeyEvent);
        Assert.False(notification.IsSubKeySpace);
        Assert.False(notification.IsSubKeyEvent);
        Assert.True(notification.IsSubKeySpaceItem);
        Assert.False(notification.IsSubKeySpaceEvent);

        Assert.Equal(4, notification.Database);
        Assert.Equal(KeyNotificationType.HSet, notification.Type);
        Assert.True(notification.IsType("hset"u8));
        Assert.Equal("mykey", (string?)notification.GetKey());
        Assert.Equal("field1", (string?)notification.GetSubKey());
    }

    [Fact]
    public void SubKeySpaceEvent_HSet_ParsesCorrectly()
    {
        // __subkeyspaceevent@4__:hset|mykey with payload 6:field1
        var channel = RedisChannel.Literal("__subkeyspaceevent@4__:hset|mykey");
        RedisValue value = "6:field1";

        Assert.True(KeyNotification.TryParse(channel, value, out var notification));

        Assert.False(notification.IsKeySpace);
        Assert.False(notification.IsKeyEvent);
        Assert.False(notification.IsSubKeySpace);
        Assert.False(notification.IsSubKeyEvent);
        Assert.False(notification.IsSubKeySpaceItem);
        Assert.True(notification.IsSubKeySpaceEvent);

        Assert.Equal(4, notification.Database);
        Assert.Equal(KeyNotificationType.HSet, notification.Type);
        Assert.True(notification.IsType("hset"u8));
        Assert.Equal("mykey", (string?)notification.GetKey());
        Assert.Equal("field1", (string?)notification.GetSubKey());
    }

    [Fact]
    public void ExtractLengthPrefixedValue_ParsesCorrectly()
    {
        // Test the length-prefixed value extraction helper
        var result1 = KeyNotification.ExtractLengthPrefixedValue("6:field1"u8);
        Assert.Equal("field1", (string?)result1);

        var result2 = KeyNotification.ExtractLengthPrefixedValue("5:mykey"u8);
        Assert.Equal("mykey", (string?)result2);

        var result3 = KeyNotification.ExtractLengthPrefixedValue("11:hello world"u8);
        Assert.Equal("hello world", (string?)result3);

        // Test invalid formats
        var result4 = KeyNotification.ExtractLengthPrefixedValue("invalid"u8);
        Assert.True(result4.IsNull);

        var result5 = KeyNotification.ExtractLengthPrefixedValue("10:short"u8); // Length mismatch
        Assert.True(result5.IsNull);
    }

    [Fact]
    public void SubKeySpace_GetSubKey_ReturnsCorrectValue()
    {
        // Test that GetSubKey returns the expected value for SubKeySpace
        var channel = RedisChannel.Literal("__subkeyspace@4__:mykey");
        RedisValue value = "hset|6:field1";

        Assert.True(KeyNotification.TryParse(channel, value, out var notification));
        Assert.True(notification.IsSubKeySpace, "IsSubKeySpace should be true");

        var subKey = notification.GetSubKey();
        Assert.False(subKey.IsNull, $"SubKey should not be null. Value: {value}");
        Assert.Equal("field1", (string?)subKey);
    }

    [Fact]
    public void ChannelSuffix_SubKeyEvent_ReturnsCorrectValue()
    {
        // Test that ChannelSuffix returns the expected value for SubKeyEvent
        var channel = RedisChannel.Literal("__subkeyevent@4__:hset");
        RedisValue value = "5:mykey|6:field1";

        Assert.True(KeyNotification.TryParse(channel, value, out var notification));

        // Verify the correct Is* property is true
        Assert.False(notification.IsKeySpace, "IsKeySpace should be false");
        Assert.False(notification.IsKeyEvent, "IsKeyEvent should be false");
        Assert.False(notification.IsSubKeySpace, "IsSubKeySpace should be false");
        Assert.True(notification.IsSubKeyEvent, "IsSubKeyEvent should be true");
        Assert.False(notification.IsSubKeySpaceItem, "IsSubKeySpaceItem should be false");
        Assert.False(notification.IsSubKeySpaceEvent, "IsSubKeySpaceEvent should be false");

        var suffix = notification.ChannelSuffix;
        var expected = "hset"u8;

        Assert.Equal(expected.Length, suffix.Length);
        Assert.True(suffix.SequenceEqual(expected), "ChannelSuffix should equal 'hset'");
    }

    [Fact]
    public void SubKeySpace_HExpire_ParsesCorrectly()
    {
        // __subkeyspace@0__:hash with payload hexpire|5:field
        var channel = RedisChannel.Literal("__subkeyspace@0__:hash");
        RedisValue value = "hexpire|5:field";

        Assert.True(KeyNotification.TryParse(channel, value, out var notification));

        Assert.True(notification.IsSubKeySpace);
        Assert.Equal(0, notification.Database);
        Assert.Equal(KeyNotificationType.HExpire, notification.Type);
        Assert.True(notification.IsType("hexpire"u8));
        Assert.Equal("hash", (string?)notification.GetKey());
        Assert.Equal("field", (string?)notification.GetSubKey());
    }

    [Fact]
    public void NonSubKeyNotifications_ReturnNullSubKey()
    {
        // Regular keyspace notification
        var channel = RedisChannel.Literal("__keyspace@4__:mykey");
        RedisValue value = "set";

        Assert.True(KeyNotification.TryParse(channel, value, out var notification));
        Assert.True(notification.IsKeySpace);
        Assert.True(notification.GetSubKey().IsNull);

        // Regular keyevent notification
        channel = RedisChannel.Literal("__keyevent@4__:del");
        value = "mykey";

        Assert.True(KeyNotification.TryParse(channel, value, out notification));
        Assert.True(notification.IsKeyEvent);
        Assert.True(notification.GetSubKey().IsNull);
    }

    [Fact]
    public void KeyPrefix_KeySpace_MatchingPrefix_ParsesAndStrips()
    {
        // __keyspace@1__:foo:bar with payload "set"
        // Key prefix is "foo:"
        var channel = RedisChannel.Literal("__keyspace@1__:foo:bar");
        RedisValue value = "set";
        ReadOnlySpan<byte> keyPrefix = "foo:"u8;

        Assert.True(KeyNotification.TryParse(keyPrefix, in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);
        Assert.Equal(1, notification.Database);
        Assert.Equal(KeyNotificationType.Set, notification.Type);

        // The key should NOT include the prefix
        Assert.Equal("bar", (string?)notification.GetKey());
        Assert.Equal(3, notification.GetKeyByteCount());
        Assert.Equal(3, notification.GetKeyCharCount());
    }

    [Fact]
    public void KeyPrefix_KeySpace_NonMatchingPrefix_ReturnsFalse()
    {
        // __keyspace@1__:other:bar with payload "set"
        // Key prefix is "foo:"
        var channel = RedisChannel.Literal("__keyspace@1__:other:bar");
        RedisValue value = "set";
        ReadOnlySpan<byte> keyPrefix = "foo:"u8;

        // Should return false because the key doesn't start with "foo:"
        Assert.False(KeyNotification.TryParse(keyPrefix, in channel, in value, out var notification));
    }

    [Fact]
    public void KeyPrefix_KeyEvent_MatchingPrefix_ParsesAndStrips()
    {
        // __keyevent@1__:set with payload "foo:bar"
        // Key prefix is "foo:"
        var channel = RedisChannel.Literal("__keyevent@1__:set");
        RedisValue value = "foo:bar";
        ReadOnlySpan<byte> keyPrefix = "foo:"u8;

        Assert.True(KeyNotification.TryParse(keyPrefix, in channel, in value, out var notification));

        Assert.True(notification.IsKeyEvent);
        Assert.Equal(1, notification.Database);
        Assert.Equal(KeyNotificationType.Set, notification.Type);

        // The key should NOT include the prefix
        Assert.Equal("bar", (string?)notification.GetKey());
        Assert.Equal(3, notification.GetKeyByteCount());
        Assert.Equal(3, notification.GetKeyCharCount());
    }

    [Fact]
    public void KeyPrefix_KeyEvent_NonMatchingPrefix_ReturnsFalse()
    {
        // __keyevent@1__:set with payload "other:bar"
        // Key prefix is "foo:"
        var channel = RedisChannel.Literal("__keyevent@1__:set");
        RedisValue value = "other:bar";
        ReadOnlySpan<byte> keyPrefix = "foo:"u8;

        // Should return false because the key doesn't start with "foo:"
        Assert.False(KeyNotification.TryParse(keyPrefix, in channel, in value, out var notification));
    }

    [Fact]
    public void KeyPrefix_KeySpace_EmptyPrefix_ParsesWithoutStripping()
    {
        // __keyspace@1__:mykey with payload "set"
        // Empty prefix
        var channel = RedisChannel.Literal("__keyspace@1__:mykey");
        RedisValue value = "set";
        ReadOnlySpan<byte> keyPrefix = ""u8;

        Assert.True(KeyNotification.TryParse(keyPrefix, in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);

        // The key should be unchanged
        Assert.Equal("mykey", (string?)notification.GetKey());
        Assert.Equal(5, notification.GetKeyByteCount());
    }

    [Fact]
    public void KeyPrefix_KeySpace_PrefixLongerThanKey_ReturnsFalse()
    {
        // __keyspace@1__:foo with payload "set"
        // Key prefix is "foo:bar" which is longer than the actual key
        var channel = RedisChannel.Literal("__keyspace@1__:foo");
        RedisValue value = "set";
        ReadOnlySpan<byte> keyPrefix = "foo:bar"u8;

        // Should return false because prefix is longer than the key
        Assert.False(KeyNotification.TryParse(keyPrefix, in channel, in value, out var notification));
    }

    [Fact]
    public void KeyPrefix_KeySpace_ExactMatch_ReturnsEmptyKey()
    {
        // __keyspace@1__:foo with payload "set"
        // Key prefix is exactly "foo"
        var channel = RedisChannel.Literal("__keyspace@1__:foo");
        RedisValue value = "set";
        ReadOnlySpan<byte> keyPrefix = "foo"u8;

        Assert.True(KeyNotification.TryParse(keyPrefix, in channel, in value, out var notification));

        Assert.True(notification.IsKeySpace);

        // The key should be empty after stripping the prefix
        Assert.Equal("", (string?)notification.GetKey());
        Assert.Equal(0, notification.GetKeyByteCount());
        Assert.Equal(0, notification.GetKeyCharCount());
    }

    [Fact]
    public void KeyPrefix_MultiTenantScenario_IsolatesCorrectly()
    {
        // Simulate a multi-tenant scenario with client prefixes
        ReadOnlySpan<byte> client1Prefix = "client1234:"u8;
        ReadOnlySpan<byte> client5678Prefix = "client5678:"u8;

        // Client 1's notification
        var channel1 = RedisChannel.Literal("__keyspace@0__:client1234:order/123");
        RedisValue value1 = "set";

        // Client 2's notification (different client)
        var channel2 = RedisChannel.Literal("__keyspace@0__:client5678:order/456");
        RedisValue value2 = "set";

        // Client 1 should only see their own notifications
        Assert.True(KeyNotification.TryParse(client1Prefix, in channel1, in value1, out var notification1));
        Assert.Equal("order/123", (string?)notification1.GetKey());

        Assert.False(KeyNotification.TryParse(client1Prefix, in channel2, in value2, out _));

        // Client 2 should only see their own notifications
        Assert.True(KeyNotification.TryParse(client5678Prefix, in channel2, in value2, out var notification2));
        Assert.Equal("order/456", (string?)notification2.GetKey());

        Assert.False(KeyNotification.TryParse(client5678Prefix, in channel1, in value1, out _));
    }
}
