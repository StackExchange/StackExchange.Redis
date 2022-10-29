using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net;
using System.Text;
using Moq;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests;

[CollectionDefinition(nameof(MoqDependentCollection), DisableParallelization = true)]
public class MoqDependentCollection { }

[Collection(nameof(MoqDependentCollection))]
public sealed class KeyPrefixedDatabaseTests
{
    private readonly Mock<IDatabase> mock;
    private readonly IDatabase prefixed;

    public KeyPrefixedDatabaseTests()
    {
        mock = new Mock<IDatabase>();
        prefixed = new KeyPrefixedDatabase(mock.Object, Encoding.UTF8.GetBytes("prefix:"));
    }

    [Fact]
    public void CreateBatch()
    {
        object asyncState = new();
        IBatch innerBatch = new Mock<IBatch>().Object;
        mock.Setup(_ => _.CreateBatch(asyncState)).Returns(innerBatch);
        IBatch wrappedBatch = prefixed.CreateBatch(asyncState);
        mock.Verify(_ => _.CreateBatch(asyncState));
        Assert.IsType<KeyPrefixedBatch>(wrappedBatch);
        Assert.Same(innerBatch, ((KeyPrefixedBatch)wrappedBatch).Inner);
    }

    [Fact]
    public void CreateTransaction()
    {
        object asyncState = new();
        ITransaction innerTransaction = new Mock<ITransaction>().Object;
        mock.Setup(_ => _.CreateTransaction(asyncState)).Returns(innerTransaction);
        ITransaction wrappedTransaction = prefixed.CreateTransaction(asyncState);
        mock.Verify(_ => _.CreateTransaction(asyncState));
        Assert.IsType<KeyPrefixedTransaction>(wrappedTransaction);
        Assert.Same(innerTransaction, ((KeyPrefixedTransaction)wrappedTransaction).Inner);
    }

    [Fact]
    public void DebugObject()
    {
        prefixed.DebugObject("key", CommandFlags.None);
        mock.Verify(_ => _.DebugObject("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void Get_Database()
    {
        mock.SetupGet(_ => _.Database).Returns(123);
        Assert.Equal(123, prefixed.Database);
    }

    [Fact]
    public void HashDecrement_1()
    {
        prefixed.HashDecrement("key", "hashField", 123, CommandFlags.None);
        mock.Verify(_ => _.HashDecrement("prefix:key", "hashField", 123, CommandFlags.None));
    }

    [Fact]
    public void HashDecrement_2()
    {
        prefixed.HashDecrement("key", "hashField", 1.23, CommandFlags.None);
        mock.Verify(_ => _.HashDecrement("prefix:key", "hashField", 1.23, CommandFlags.None));
    }

    [Fact]
    public void HashDelete_1()
    {
        prefixed.HashDelete("key", "hashField", CommandFlags.None);
        mock.Verify(_ => _.HashDelete("prefix:key", "hashField", CommandFlags.None));
    }

    [Fact]
    public void HashDelete_2()
    {
        RedisValue[] hashFields = Array.Empty<RedisValue>();
        prefixed.HashDelete("key", hashFields, CommandFlags.None);
        mock.Verify(_ => _.HashDelete("prefix:key", hashFields, CommandFlags.None));
    }

    [Fact]
    public void HashExists()
    {
        prefixed.HashExists("key", "hashField", CommandFlags.None);
        mock.Verify(_ => _.HashExists("prefix:key", "hashField", CommandFlags.None));
    }

    [Fact]
    public void HashGet_1()
    {
        prefixed.HashGet("key", "hashField", CommandFlags.None);
        mock.Verify(_ => _.HashGet("prefix:key", "hashField", CommandFlags.None));
    }

    [Fact]
    public void HashGet_2()
    {
        RedisValue[] hashFields = Array.Empty<RedisValue>();
        prefixed.HashGet("key", hashFields, CommandFlags.None);
        mock.Verify(_ => _.HashGet("prefix:key", hashFields, CommandFlags.None));
    }

    [Fact]
    public void HashGetAll()
    {
        prefixed.HashGetAll("key", CommandFlags.None);
        mock.Verify(_ => _.HashGetAll("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void HashIncrement_1()
    {
        prefixed.HashIncrement("key", "hashField", 123, CommandFlags.None);
        mock.Verify(_ => _.HashIncrement("prefix:key", "hashField", 123, CommandFlags.None));
    }

    [Fact]
    public void HashIncrement_2()
    {
        prefixed.HashIncrement("key", "hashField", 1.23, CommandFlags.None);
        mock.Verify(_ => _.HashIncrement("prefix:key", "hashField", 1.23, CommandFlags.None));
    }

    [Fact]
    public void HashKeys()
    {
        prefixed.HashKeys("key", CommandFlags.None);
        mock.Verify(_ => _.HashKeys("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void HashLength()
    {
        prefixed.HashLength("key", CommandFlags.None);
        mock.Verify(_ => _.HashLength("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void HashScan()
    {
        prefixed.HashScan("key", "pattern", 123, flags: CommandFlags.None);
        mock.Verify(_ => _.HashScan("prefix:key", "pattern", 123, CommandFlags.None));
    }

    [Fact]
    public void HashScan_Full()
    {
        prefixed.HashScan("key", "pattern", 123, 42, 64, flags: CommandFlags.None);
        mock.Verify(_ => _.HashScan("prefix:key", "pattern", 123, 42, 64, CommandFlags.None));
    }

    [Fact]
    public void HashSet_1()
    {
        HashEntry[] hashFields = Array.Empty<HashEntry>();
        prefixed.HashSet("key", hashFields, CommandFlags.None);
        mock.Verify(_ => _.HashSet("prefix:key", hashFields, CommandFlags.None));
    }

    [Fact]
    public void HashSet_2()
    {
        prefixed.HashSet("key", "hashField", "value", When.Exists, CommandFlags.None);
        mock.Verify(_ => _.HashSet("prefix:key", "hashField", "value", When.Exists, CommandFlags.None));
    }

    [Fact]
    public void HashStringLength()
    {
        prefixed.HashStringLength("key", "field", CommandFlags.None);
        mock.Verify(_ => _.HashStringLength("prefix:key", "field", CommandFlags.None));
    }

    [Fact]
    public void HashValues()
    {
        prefixed.HashValues("key", CommandFlags.None);
        mock.Verify(_ => _.HashValues("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void HyperLogLogAdd_1()
    {
        prefixed.HyperLogLogAdd("key", "value", CommandFlags.None);
        mock.Verify(_ => _.HyperLogLogAdd("prefix:key", "value", CommandFlags.None));
    }

    [Fact]
    public void HyperLogLogAdd_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        prefixed.HyperLogLogAdd("key", values, CommandFlags.None);
        mock.Verify(_ => _.HyperLogLogAdd("prefix:key", values, CommandFlags.None));
    }

    [Fact]
    public void HyperLogLogLength()
    {
        prefixed.HyperLogLogLength("key", CommandFlags.None);
        mock.Verify(_ => _.HyperLogLogLength("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void HyperLogLogMerge_1()
    {
        prefixed.HyperLogLogMerge("destination", "first", "second", CommandFlags.None);
        mock.Verify(_ => _.HyperLogLogMerge("prefix:destination", "prefix:first", "prefix:second", CommandFlags.None));
    }

    [Fact]
    public void HyperLogLogMerge_2()
    {
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
        prefixed.HyperLogLogMerge("destination", keys, CommandFlags.None);
        mock.Verify(_ => _.HyperLogLogMerge("prefix:destination", It.Is(valid), CommandFlags.None));
    }

    [Fact]
    public void IdentifyEndpoint()
    {
        prefixed.IdentifyEndpoint("key", CommandFlags.None);
        mock.Verify(_ => _.IdentifyEndpoint("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void KeyCopy()
    {
        prefixed.KeyCopy("key", "destination", flags: CommandFlags.None);
        mock.Verify(_ => _.KeyCopy("prefix:key", "prefix:destination", -1, false, CommandFlags.None));
    }

    [Fact]
    public void KeyDelete_1()
    {
        prefixed.KeyDelete("key", CommandFlags.None);
        mock.Verify(_ => _.KeyDelete("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void KeyDelete_2()
    {
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
        prefixed.KeyDelete(keys, CommandFlags.None);
        mock.Verify(_ => _.KeyDelete(It.Is(valid), CommandFlags.None));
    }

    [Fact]
    public void KeyDump()
    {
        prefixed.KeyDump("key", CommandFlags.None);
        mock.Verify(_ => _.KeyDump("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void KeyEncoding()
    {
        prefixed.KeyEncoding("key", CommandFlags.None);
        mock.Verify(_ => _.KeyEncoding("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void KeyExists()
    {
        prefixed.KeyExists("key", CommandFlags.None);
        mock.Verify(_ => _.KeyExists("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void KeyExpire_1()
    {
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.KeyExpire("key", expiry, CommandFlags.None);
        mock.Verify(_ => _.KeyExpire("prefix:key", expiry, CommandFlags.None));
    }

    [Fact]
    public void KeyExpire_2()
    {
        DateTime expiry = DateTime.Now;
        prefixed.KeyExpire("key", expiry, CommandFlags.None);
        mock.Verify(_ => _.KeyExpire("prefix:key", expiry, CommandFlags.None));
    }

    [Fact]
    public void KeyExpire_3()
    {
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.KeyExpire("key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
        mock.Verify(_ => _.KeyExpire("prefix:key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None));
    }

    [Fact]
    public void KeyExpire_4()
    {
        DateTime expiry = DateTime.Now;
        prefixed.KeyExpire("key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
        mock.Verify(_ => _.KeyExpire("prefix:key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None));
    }

    [Fact]
    public void KeyExpireTime()
    {
        prefixed.KeyExpireTime("key", CommandFlags.None);
        mock.Verify(_ => _.KeyExpireTime("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void KeyFrequency()
    {
        prefixed.KeyFrequency("key", CommandFlags.None);
        mock.Verify(_ => _.KeyFrequency("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void KeyMigrate()
    {
        EndPoint toServer = new IPEndPoint(IPAddress.Loopback, 123);
        prefixed.KeyMigrate("key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.None);
        mock.Verify(_ => _.KeyMigrate("prefix:key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.None));
    }

    [Fact]
    public void KeyMove()
    {
        prefixed.KeyMove("key", 123, CommandFlags.None);
        mock.Verify(_ => _.KeyMove("prefix:key", 123, CommandFlags.None));
    }

    [Fact]
    public void KeyPersist()
    {
        prefixed.KeyPersist("key", CommandFlags.None);
        mock.Verify(_ => _.KeyPersist("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void KeyRandom()
    {
        Assert.Throws<NotSupportedException>(() => prefixed.KeyRandom());
    }

    [Fact]
    public void KeyRefCount()
    {
        prefixed.KeyRefCount("key", CommandFlags.None);
        mock.Verify(_ => _.KeyRefCount("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void KeyRename()
    {
        prefixed.KeyRename("key", "newKey", When.Exists, CommandFlags.None);
        mock.Verify(_ => _.KeyRename("prefix:key", "prefix:newKey", When.Exists, CommandFlags.None));
    }

    [Fact]
    public void KeyRestore()
    {
        byte[] value = Array.Empty<byte>();
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.KeyRestore("key", value, expiry, CommandFlags.None);
        mock.Verify(_ => _.KeyRestore("prefix:key", value, expiry, CommandFlags.None));
    }

    [Fact]
    public void KeyTimeToLive()
    {
        prefixed.KeyTimeToLive("key", CommandFlags.None);
        mock.Verify(_ => _.KeyTimeToLive("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void KeyType()
    {
        prefixed.KeyType("key", CommandFlags.None);
        mock.Verify(_ => _.KeyType("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void ListGetByIndex()
    {
        prefixed.ListGetByIndex("key", 123, CommandFlags.None);
        mock.Verify(_ => _.ListGetByIndex("prefix:key", 123, CommandFlags.None));
    }

    [Fact]
    public void ListInsertAfter()
    {
        prefixed.ListInsertAfter("key", "pivot", "value", CommandFlags.None);
        mock.Verify(_ => _.ListInsertAfter("prefix:key", "pivot", "value", CommandFlags.None));
    }

    [Fact]
    public void ListInsertBefore()
    {
        prefixed.ListInsertBefore("key", "pivot", "value", CommandFlags.None);
        mock.Verify(_ => _.ListInsertBefore("prefix:key", "pivot", "value", CommandFlags.None));
    }

    [Fact]
    public void ListLeftPop()
    {
        prefixed.ListLeftPop("key", CommandFlags.None);
        mock.Verify(_ => _.ListLeftPop("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void ListLeftPop_1()
    {
        prefixed.ListLeftPop("key", 123, CommandFlags.None);
        mock.Verify(_ => _.ListLeftPop("prefix:key", 123, CommandFlags.None));
    }

    [Fact]
    public void ListLeftPush_1()
    {
        prefixed.ListLeftPush("key", "value", When.Exists, CommandFlags.None);
        mock.Verify(_ => _.ListLeftPush("prefix:key", "value", When.Exists, CommandFlags.None));
    }

    [Fact]
    public void ListLeftPush_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        prefixed.ListLeftPush("key", values, CommandFlags.None);
        mock.Verify(_ => _.ListLeftPush("prefix:key", values, CommandFlags.None));
    }

    [Fact]
    public void ListLeftPush_3()
    {
        RedisValue[] values = new RedisValue[] { "value1", "value2" };
        prefixed.ListLeftPush("key", values, When.Exists, CommandFlags.None);
        mock.Verify(_ => _.ListLeftPush("prefix:key", values, When.Exists, CommandFlags.None));
    }

    [Fact]
    public void ListLength()
    {
        prefixed.ListLength("key", CommandFlags.None);
        mock.Verify(_ => _.ListLength("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void ListMove()
    {
        prefixed.ListMove("key", "destination", ListSide.Left, ListSide.Right, CommandFlags.None);
        mock.Verify(_ => _.ListMove("prefix:key", "prefix:destination", ListSide.Left, ListSide.Right, CommandFlags.None));
    }

    [Fact]
    public void ListRange()
    {
        prefixed.ListRange("key", 123, 456, CommandFlags.None);
        mock.Verify(_ => _.ListRange("prefix:key", 123, 456, CommandFlags.None));
    }

    [Fact]
    public void ListRemove()
    {
        prefixed.ListRemove("key", "value", 123, CommandFlags.None);
        mock.Verify(_ => _.ListRemove("prefix:key", "value", 123, CommandFlags.None));
    }

    [Fact]
    public void ListRightPop()
    {
        prefixed.ListRightPop("key", CommandFlags.None);
        mock.Verify(_ => _.ListRightPop("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void ListRightPop_1()
    {
        prefixed.ListRightPop("key", 123, CommandFlags.None);
        mock.Verify(_ => _.ListRightPop("prefix:key", 123, CommandFlags.None));
    }

    [Fact]
    public void ListRightPopLeftPush()
    {
        prefixed.ListRightPopLeftPush("source", "destination", CommandFlags.None);
        mock.Verify(_ => _.ListRightPopLeftPush("prefix:source", "prefix:destination", CommandFlags.None));
    }

    [Fact]
    public void ListRightPush_1()
    {
        prefixed.ListRightPush("key", "value", When.Exists, CommandFlags.None);
        mock.Verify(_ => _.ListRightPush("prefix:key", "value", When.Exists, CommandFlags.None));
    }

    [Fact]
    public void ListRightPush_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        prefixed.ListRightPush("key", values, CommandFlags.None);
        mock.Verify(_ => _.ListRightPush("prefix:key", values, CommandFlags.None));
    }

    [Fact]
    public void ListRightPush_3()
    {
        RedisValue[] values = new RedisValue[] { "value1", "value2" };
        prefixed.ListRightPush("key", values, When.Exists, CommandFlags.None);
        mock.Verify(_ => _.ListRightPush("prefix:key", values, When.Exists, CommandFlags.None));
    }

    [Fact]
    public void ListSetByIndex()
    {
        prefixed.ListSetByIndex("key", 123, "value", CommandFlags.None);
        mock.Verify(_ => _.ListSetByIndex("prefix:key", 123, "value", CommandFlags.None));
    }

    [Fact]
    public void ListTrim()
    {
        prefixed.ListTrim("key", 123, 456, CommandFlags.None);
        mock.Verify(_ => _.ListTrim("prefix:key", 123, 456, CommandFlags.None));
    }

    [Fact]
    public void LockExtend()
    {
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.LockExtend("key", "value", expiry, CommandFlags.None);
        mock.Verify(_ => _.LockExtend("prefix:key", "value", expiry, CommandFlags.None));
    }

    [Fact]
    public void LockQuery()
    {
        prefixed.LockQuery("key", CommandFlags.None);
        mock.Verify(_ => _.LockQuery("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void LockRelease()
    {
        prefixed.LockRelease("key", "value", CommandFlags.None);
        mock.Verify(_ => _.LockRelease("prefix:key", "value", CommandFlags.None));
    }

    [Fact]
    public void LockTake()
    {
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.LockTake("key", "value", expiry, CommandFlags.None);
        mock.Verify(_ => _.LockTake("prefix:key", "value", expiry, CommandFlags.None));
    }

    [Fact]
    public void Publish()
    {
        prefixed.Publish("channel", "message", CommandFlags.None);
        mock.Verify(_ => _.Publish("prefix:channel", "message", CommandFlags.None));
    }

    [Fact]
    public void ScriptEvaluate_1()
    {
        byte[] hash = Array.Empty<byte>();
        RedisValue[] values = Array.Empty<RedisValue>();
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
        prefixed.ScriptEvaluate(hash, keys, values, CommandFlags.None);
        mock.Verify(_ => _.ScriptEvaluate(hash, It.Is(valid), values, CommandFlags.None));
    }

    [Fact]
    public void ScriptEvaluate_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
        prefixed.ScriptEvaluate("script", keys, values, CommandFlags.None);
        mock.Verify(_ => _.ScriptEvaluate("script", It.Is(valid), values, CommandFlags.None));
    }

    [Fact]
    public void SetAdd_1()
    {
        prefixed.SetAdd("key", "value", CommandFlags.None);
        mock.Verify(_ => _.SetAdd("prefix:key", "value", CommandFlags.None));
    }

    [Fact]
    public void SetAdd_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        prefixed.SetAdd("key", values, CommandFlags.None);
        mock.Verify(_ => _.SetAdd("prefix:key", values, CommandFlags.None));
    }

    [Fact]
    public void SetCombine_1()
    {
        prefixed.SetCombine(SetOperation.Intersect, "first", "second", CommandFlags.None);
        mock.Verify(_ => _.SetCombine(SetOperation.Intersect, "prefix:first", "prefix:second", CommandFlags.None));
    }

    [Fact]
    public void SetCombine_2()
    {
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
        prefixed.SetCombine(SetOperation.Intersect, keys, CommandFlags.None);
        mock.Verify(_ => _.SetCombine(SetOperation.Intersect, It.Is(valid), CommandFlags.None));
    }

    [Fact]
    public void SetCombineAndStore_1()
    {
        prefixed.SetCombineAndStore(SetOperation.Intersect, "destination", "first", "second", CommandFlags.None);
        mock.Verify(_ => _.SetCombineAndStore(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.None));
    }

    [Fact]
    public void SetCombineAndStore_2()
    {
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
        prefixed.SetCombineAndStore(SetOperation.Intersect, "destination", keys, CommandFlags.None);
        mock.Verify(_ => _.SetCombineAndStore(SetOperation.Intersect, "prefix:destination", It.Is(valid), CommandFlags.None));
    }

    [Fact]
    public void SetContains()
    {
        prefixed.SetContains("key", "value", CommandFlags.None);
        mock.Verify(_ => _.SetContains("prefix:key", "value", CommandFlags.None));
    }

    [Fact]
    public void SetContains_2()
    {
        RedisValue[] values = new RedisValue[] { "value1", "value2" };
        prefixed.SetContains("key", values, CommandFlags.None);
        mock.Verify(_ => _.SetContains("prefix:key", values, CommandFlags.None));
    }

    [Fact]
    public void SetIntersectionLength()
    {
        var keys = new RedisKey[] { "key1", "key2" };
        prefixed.SetIntersectionLength(keys);
        mock.Verify(_ => _.SetIntersectionLength(keys, 0, CommandFlags.None));
    }

    [Fact]
    public void SetLength()
    {
        prefixed.SetLength("key", CommandFlags.None);
        mock.Verify(_ => _.SetLength("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void SetMembers()
    {
        prefixed.SetMembers("key", CommandFlags.None);
        mock.Verify(_ => _.SetMembers("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void SetMove()
    {
        prefixed.SetMove("source", "destination", "value", CommandFlags.None);
        mock.Verify(_ => _.SetMove("prefix:source", "prefix:destination", "value", CommandFlags.None));
    }

    [Fact]
    public void SetPop_1()
    {
        prefixed.SetPop("key", CommandFlags.None);
        mock.Verify(_ => _.SetPop("prefix:key", CommandFlags.None));

        prefixed.SetPop("key", 5, CommandFlags.None);
        mock.Verify(_ => _.SetPop("prefix:key", 5, CommandFlags.None));
    }

    [Fact]
    public void SetPop_2()
    {
        prefixed.SetPop("key", 5, CommandFlags.None);
        mock.Verify(_ => _.SetPop("prefix:key", 5, CommandFlags.None));
    }

    [Fact]
    public void SetRandomMember()
    {
        prefixed.SetRandomMember("key", CommandFlags.None);
        mock.Verify(_ => _.SetRandomMember("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void SetRandomMembers()
    {
        prefixed.SetRandomMembers("key", 123, CommandFlags.None);
        mock.Verify(_ => _.SetRandomMembers("prefix:key", 123, CommandFlags.None));
    }

    [Fact]
    public void SetRemove_1()
    {
        prefixed.SetRemove("key", "value", CommandFlags.None);
        mock.Verify(_ => _.SetRemove("prefix:key", "value", CommandFlags.None));
    }

    [Fact]
    public void SetRemove_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        prefixed.SetRemove("key", values, CommandFlags.None);
        mock.Verify(_ => _.SetRemove("prefix:key", values, CommandFlags.None));
    }

    [Fact]
    public void SetScan()
    {
        prefixed.SetScan("key", "pattern", 123, flags: CommandFlags.None);
        mock.Verify(_ => _.SetScan("prefix:key", "pattern", 123, CommandFlags.None));
    }

    [Fact]
    public void SetScan_Full()
    {
        prefixed.SetScan("key", "pattern", 123, 42, 64, flags: CommandFlags.None);
        mock.Verify(_ => _.SetScan("prefix:key", "pattern", 123, 42, 64, CommandFlags.None));
    }

    [Fact]
    public void Sort()
    {
        RedisValue[] get = new RedisValue[] { "a", "#" };
        Expression<Func<RedisValue[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "#";

        prefixed.Sort("key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.None);
        prefixed.Sort("key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.None);

        mock.Verify(_ => _.Sort("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", It.Is(valid), CommandFlags.None));
        mock.Verify(_ => _.Sort("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", It.Is(valid), CommandFlags.None));
    }

    [Fact]
    public void SortAndStore()
    {
        RedisValue[] get = new RedisValue[] { "a", "#" };
        Expression<Func<RedisValue[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "#";

        prefixed.SortAndStore("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.None);
        prefixed.SortAndStore("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.None);

        mock.Verify(_ => _.SortAndStore("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", It.Is(valid), CommandFlags.None));
        mock.Verify(_ => _.SortAndStore("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", It.Is(valid), CommandFlags.None));
    }

    [Fact]
    public void SortedSetAdd_1()
    {
        prefixed.SortedSetAdd("key", "member", 1.23, When.Exists, CommandFlags.None);
        mock.Verify(_ => _.SortedSetAdd("prefix:key", "member", 1.23, When.Exists, CommandFlags.None));
    }

    [Fact]
    public void SortedSetAdd_2()
    {
        SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
        prefixed.SortedSetAdd("key", values, When.Exists, CommandFlags.None);
        mock.Verify(_ => _.SortedSetAdd("prefix:key", values, When.Exists, CommandFlags.None));
    }

    [Fact]
    public void SortedSetAdd_3()
    {
        SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
        prefixed.SortedSetAdd("key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
        mock.Verify(_ => _.SortedSetAdd("prefix:key", values, SortedSetWhen.GreaterThan, CommandFlags.None));
    }

    [Fact]
    public void SortedSetCombine()
    {
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        prefixed.SortedSetCombine(SetOperation.Intersect, keys);
        mock.Verify(_ => _.SortedSetCombine(SetOperation.Intersect, keys, null, Aggregate.Sum, CommandFlags.None));
    }

    [Fact]
    public void SortedSetCombineWithScores()
    {
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        prefixed.SortedSetCombineWithScores(SetOperation.Intersect, keys);
        mock.Verify(_ => _.SortedSetCombineWithScores(SetOperation.Intersect, keys, null, Aggregate.Sum, CommandFlags.None));
    }

    [Fact]
    public void SortedSetCombineAndStore_1()
    {
        prefixed.SortedSetCombineAndStore(SetOperation.Intersect, "destination", "first", "second", Aggregate.Max, CommandFlags.None);
        mock.Verify(_ => _.SortedSetCombineAndStore(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", Aggregate.Max, CommandFlags.None));
    }

    [Fact]
    public void SortedSetCombineAndStore_2()
    {
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
        prefixed.SetCombineAndStore(SetOperation.Intersect, "destination", keys, CommandFlags.None);
        mock.Verify(_ => _.SetCombineAndStore(SetOperation.Intersect, "prefix:destination", It.Is(valid), CommandFlags.None));
    }

    [Fact]
    public void SortedSetDecrement()
    {
        prefixed.SortedSetDecrement("key", "member", 1.23, CommandFlags.None);
        mock.Verify(_ => _.SortedSetDecrement("prefix:key", "member", 1.23, CommandFlags.None));
    }

    [Fact]
    public void SortedSetIncrement()
    {
        prefixed.SortedSetIncrement("key", "member", 1.23, CommandFlags.None);
        mock.Verify(_ => _.SortedSetIncrement("prefix:key", "member", 1.23, CommandFlags.None));
    }

    [Fact]
    public void SortedSetIntersectionLength()
    {
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        prefixed.SortedSetIntersectionLength(keys, 1, CommandFlags.None);
        mock.Verify(_ => _.SortedSetIntersectionLength(keys, 1, CommandFlags.None));
    }

    [Fact]
    public void SortedSetLength()
    {
        prefixed.SortedSetLength("key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
        mock.Verify(_ => _.SortedSetLength("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRandomMember()
    {
        prefixed.SortedSetRandomMember("key", CommandFlags.None);
        mock.Verify(_ => _.SortedSetRandomMember("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void SortedSetRandomMembers()
    {
        prefixed.SortedSetRandomMembers("key", 2, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRandomMembers("prefix:key", 2, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRandomMembersWithScores()
    {
        prefixed.SortedSetRandomMembersWithScores("key", 2, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRandomMembersWithScores("prefix:key", 2, CommandFlags.None));
    }

    [Fact]
    public void SortedSetLengthByValue()
    {
        prefixed.SortedSetLengthByValue("key", "min", "max", Exclude.Start, CommandFlags.None);
        mock.Verify(_ => _.SortedSetLengthByValue("prefix:key", "min", "max", Exclude.Start, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRangeByRank()
    {
        prefixed.SortedSetRangeByRank("key", 123, 456, Order.Descending, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRangeByRank("prefix:key", 123, 456, Order.Descending, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRangeByRankWithScores()
    {
        prefixed.SortedSetRangeByRankWithScores("key", 123, 456, Order.Descending, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRangeByRankWithScores("prefix:key", 123, 456, Order.Descending, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRangeByScore()
    {
        prefixed.SortedSetRangeByScore("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRangeByScore("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRangeByScoreWithScores()
    {
        prefixed.SortedSetRangeByScoreWithScores("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRangeByScoreWithScores("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRangeByValue()
    {
        prefixed.SortedSetRangeByValue("key", "min", "max", Exclude.Start, 123, 456, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRangeByValue("prefix:key", "min", "max", Exclude.Start, Order.Ascending, 123, 456, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRangeByValueDesc()
    {
        prefixed.SortedSetRangeByValue("key", "min", "max", Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRangeByValue("prefix:key", "min", "max", Exclude.Start, Order.Descending, 123, 456, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRank()
    {
        prefixed.SortedSetRank("key", "member", Order.Descending, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRank("prefix:key", "member", Order.Descending, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRemove_1()
    {
        prefixed.SortedSetRemove("key", "member", CommandFlags.None);
        mock.Verify(_ => _.SortedSetRemove("prefix:key", "member", CommandFlags.None));
    }

    [Fact]
    public void SortedSetRemove_2()
    {
        RedisValue[] members = Array.Empty<RedisValue>();
        prefixed.SortedSetRemove("key", members, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRemove("prefix:key", members, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRemoveRangeByRank()
    {
        prefixed.SortedSetRemoveRangeByRank("key", 123, 456, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRemoveRangeByRank("prefix:key", 123, 456, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRemoveRangeByScore()
    {
        prefixed.SortedSetRemoveRangeByScore("key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRemoveRangeByScore("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.None));
    }

    [Fact]
    public void SortedSetRemoveRangeByValue()
    {
        prefixed.SortedSetRemoveRangeByValue("key", "min", "max", Exclude.Start, CommandFlags.None);
        mock.Verify(_ => _.SortedSetRemoveRangeByValue("prefix:key", "min", "max", Exclude.Start, CommandFlags.None));
    }

    [Fact]
    public void SortedSetScan()
    {
        prefixed.SortedSetScan("key", "pattern", 123, flags: CommandFlags.None);
        mock.Verify(_ => _.SortedSetScan("prefix:key", "pattern", 123, CommandFlags.None));
    }

    [Fact]
    public void SortedSetScan_Full()
    {
        prefixed.SortedSetScan("key", "pattern", 123, 42, 64, flags: CommandFlags.None);
        mock.Verify(_ => _.SortedSetScan("prefix:key", "pattern", 123, 42, 64, CommandFlags.None));
    }

    [Fact]
    public void SortedSetScore()
    {
        prefixed.SortedSetScore("key", "member", CommandFlags.None);
        mock.Verify(_ => _.SortedSetScore("prefix:key", "member", CommandFlags.None));
    }

    [Fact]
    public void SortedSetScore_Multiple()
    {
        prefixed.SortedSetScores("key", new RedisValue[] { "member1", "member2" }, CommandFlags.None);
        mock.Verify(_ => _.SortedSetScores("prefix:key", new RedisValue[] { "member1", "member2" }, CommandFlags.None));
    }

    [Fact]
    public void SortedSetUpdate()
    {
        SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
        prefixed.SortedSetUpdate("key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
        mock.Verify(_ => _.SortedSetUpdate("prefix:key", values, SortedSetWhen.GreaterThan, CommandFlags.None));
    }

    [Fact]
    public void StreamAcknowledge_1()
    {
        prefixed.StreamAcknowledge("key", "group", "0-0", CommandFlags.None);
        mock.Verify(_ => _.StreamAcknowledge("prefix:key", "group", "0-0", CommandFlags.None));
    }

    [Fact]
    public void StreamAcknowledge_2()
    {
        var messageIds = new RedisValue[] { "0-0", "0-1", "0-2" };
        prefixed.StreamAcknowledge("key", "group", messageIds, CommandFlags.None);
        mock.Verify(_ => _.StreamAcknowledge("prefix:key", "group", messageIds, CommandFlags.None));
    }

    [Fact]
    public void StreamAdd_1()
    {
        prefixed.StreamAdd("key", "field1", "value1", "*", 1000, true, CommandFlags.None);
        mock.Verify(_ => _.StreamAdd("prefix:key", "field1", "value1", "*", 1000, true, CommandFlags.None));
    }

    [Fact]
    public void StreamAdd_2()
    {
        var fields = Array.Empty<NameValueEntry>();
        prefixed.StreamAdd("key", fields, "*", 1000, true, CommandFlags.None);
        mock.Verify(_ => _.StreamAdd("prefix:key", fields, "*", 1000, true, CommandFlags.None));
    }

    [Fact]
    public void StreamAutoClaim()
    {
        prefixed.StreamAutoClaim("key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
        mock.Verify(_ => _.StreamAutoClaim("prefix:key", "group", "consumer", 0, "0-0", 100, CommandFlags.None));
    }

    [Fact]
    public void StreamAutoClaimIdsOnly()
    {
        prefixed.StreamAutoClaimIdsOnly("key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
        mock.Verify(_ => _.StreamAutoClaimIdsOnly("prefix:key", "group", "consumer", 0, "0-0", 100, CommandFlags.None));
    }

    [Fact]
    public void StreamClaimMessages()
    {
        var messageIds = Array.Empty<RedisValue>();
        prefixed.StreamClaim("key", "group", "consumer", 1000, messageIds, CommandFlags.None);
        mock.Verify(_ => _.StreamClaim("prefix:key", "group", "consumer", 1000, messageIds, CommandFlags.None));
    }

    [Fact]
    public void StreamClaimMessagesReturningIds()
    {
        var messageIds = Array.Empty<RedisValue>();
        prefixed.StreamClaimIdsOnly("key", "group", "consumer", 1000, messageIds, CommandFlags.None);
        mock.Verify(_ => _.StreamClaimIdsOnly("prefix:key", "group", "consumer", 1000, messageIds, CommandFlags.None));
    }

    [Fact]
    public void StreamConsumerGroupSetPosition()
    {
        prefixed.StreamConsumerGroupSetPosition("key", "group", StreamPosition.Beginning, CommandFlags.None);
        mock.Verify(_ => _.StreamConsumerGroupSetPosition("prefix:key", "group", StreamPosition.Beginning, CommandFlags.None));
    }

    [Fact]
    public void StreamConsumerInfoGet()
    {
        prefixed.StreamConsumerInfo("key", "group", CommandFlags.None);
        mock.Verify(_ => _.StreamConsumerInfo("prefix:key", "group", CommandFlags.None));
    }

    [Fact]
    public void StreamCreateConsumerGroup()
    {
        prefixed.StreamCreateConsumerGroup("key", "group", StreamPosition.Beginning, false, CommandFlags.None);
        mock.Verify(_ => _.StreamCreateConsumerGroup("prefix:key", "group", StreamPosition.Beginning, false, CommandFlags.None));
    }

    [Fact]
    public void StreamGroupInfoGet()
    {
        prefixed.StreamGroupInfo("key", CommandFlags.None);
        mock.Verify(_ => _.StreamGroupInfo("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void StreamInfoGet()
    {
        prefixed.StreamInfo("key", CommandFlags.None);
        mock.Verify(_ => _.StreamInfo("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void StreamLength()
    {
        prefixed.StreamLength("key", CommandFlags.None);
        mock.Verify(_ => _.StreamLength("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void StreamMessagesDelete()
    {
        var messageIds = Array.Empty<RedisValue>();
        prefixed.StreamDelete("key", messageIds, CommandFlags.None);
        mock.Verify(_ => _.StreamDelete("prefix:key", messageIds, CommandFlags.None));
    }

    [Fact]
    public void StreamDeleteConsumer()
    {
        prefixed.StreamDeleteConsumer("key", "group", "consumer", CommandFlags.None);
        mock.Verify(_ => _.StreamDeleteConsumer("prefix:key", "group", "consumer", CommandFlags.None));
    }

    [Fact]
    public void StreamDeleteConsumerGroup()
    {
        prefixed.StreamDeleteConsumerGroup("key", "group", CommandFlags.None);
        mock.Verify(_ => _.StreamDeleteConsumerGroup("prefix:key", "group", CommandFlags.None));
    }

    [Fact]
    public void StreamPendingInfoGet()
    {
        prefixed.StreamPending("key", "group", CommandFlags.None);
        mock.Verify(_ => _.StreamPending("prefix:key", "group", CommandFlags.None));
    }

    [Fact]
    public void StreamPendingMessageInfoGet()
    {
        prefixed.StreamPendingMessages("key", "group", 10, RedisValue.Null, "-", "+", CommandFlags.None);
        mock.Verify(_ => _.StreamPendingMessages("prefix:key", "group", 10, RedisValue.Null, "-", "+", CommandFlags.None));
    }

    [Fact]
    public void StreamRange()
    {
        prefixed.StreamRange("key", "-", "+", null, Order.Ascending, CommandFlags.None);
        mock.Verify(_ => _.StreamRange("prefix:key", "-", "+", null, Order.Ascending, CommandFlags.None));
    }

    [Fact]
    public void StreamRead_1()
    {
        var streamPositions = Array.Empty<StreamPosition>();
        prefixed.StreamRead(streamPositions, null, CommandFlags.None);
        mock.Verify(_ => _.StreamRead(streamPositions, null, CommandFlags.None));
    }

    [Fact]
    public void StreamRead_2()
    {
        prefixed.StreamRead("key", "0-0", null, CommandFlags.None);
        mock.Verify(_ => _.StreamRead("prefix:key", "0-0", null, CommandFlags.None));
    }

    [Fact]
    public void StreamStreamReadGroup_1()
    {
        prefixed.StreamReadGroup("key", "group", "consumer", "0-0", 10, false, CommandFlags.None);
        mock.Verify(_ => _.StreamReadGroup("prefix:key", "group", "consumer", "0-0", 10, false, CommandFlags.None));
    }

    [Fact]
    public void StreamStreamReadGroup_2()
    {
        var streamPositions = Array.Empty<StreamPosition>();
        prefixed.StreamReadGroup(streamPositions, "group", "consumer", 10, false, CommandFlags.None);
        mock.Verify(_ => _.StreamReadGroup(streamPositions, "group", "consumer", 10, false, CommandFlags.None));
    }

    [Fact]
    public void StreamTrim()
    {
        prefixed.StreamTrim("key", 1000, true, CommandFlags.None);
        mock.Verify(_ => _.StreamTrim("prefix:key", 1000, true, CommandFlags.None));
    }

    [Fact]
    public void StringAppend()
    {
        prefixed.StringAppend("key", "value", CommandFlags.None);
        mock.Verify(_ => _.StringAppend("prefix:key", "value", CommandFlags.None));
    }

    [Fact]
    public void StringBitCount()
    {
        prefixed.StringBitCount("key", 123, 456, CommandFlags.None);
        mock.Verify(_ => _.StringBitCount("prefix:key", 123, 456, CommandFlags.None));
    }

    [Fact]
    public void StringBitCount_2()
    {
        prefixed.StringBitCount("key", 123, 456, StringIndexType.Byte, CommandFlags.None);
        mock.Verify(_ => _.StringBitCount("prefix:key", 123, 456, StringIndexType.Byte, CommandFlags.None));
    }

    [Fact]
    public void StringBitOperation_1()
    {
        prefixed.StringBitOperation(Bitwise.Xor, "destination", "first", "second", CommandFlags.None);
        mock.Verify(_ => _.StringBitOperation(Bitwise.Xor, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.None));
    }

    [Fact]
    public void StringBitOperation_2()
    {
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
        prefixed.StringBitOperation(Bitwise.Xor, "destination", keys, CommandFlags.None);
        mock.Verify(_ => _.StringBitOperation(Bitwise.Xor, "prefix:destination", It.Is(valid), CommandFlags.None));
    }

    [Fact]
    public void StringBitPosition()
    {
        prefixed.StringBitPosition("key", true, 123, 456, CommandFlags.None);
        mock.Verify(_ => _.StringBitPosition("prefix:key", true, 123, 456, CommandFlags.None));
    }

    [Fact]
    public void StringBitPosition_2()
    {
        prefixed.StringBitPosition("key", true, 123, 456, StringIndexType.Byte, CommandFlags.None);
        mock.Verify(_ => _.StringBitPosition("prefix:key", true, 123, 456, StringIndexType.Byte, CommandFlags.None));
    }

    [Fact]
    public void StringDecrement_1()
    {
        prefixed.StringDecrement("key", 123, CommandFlags.None);
        mock.Verify(_ => _.StringDecrement("prefix:key", 123, CommandFlags.None));
    }

    [Fact]
    public void StringDecrement_2()
    {
        prefixed.StringDecrement("key", 1.23, CommandFlags.None);
        mock.Verify(_ => _.StringDecrement("prefix:key", 1.23, CommandFlags.None));
    }

    [Fact]
    public void StringGet_1()
    {
        prefixed.StringGet("key", CommandFlags.None);
        mock.Verify(_ => _.StringGet("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void StringGet_2()
    {
        RedisKey[] keys = new RedisKey[] { "a", "b" };
        Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
        prefixed.StringGet(keys, CommandFlags.None);
        mock.Verify(_ => _.StringGet(It.Is(valid), CommandFlags.None));
    }

    [Fact]
    public void StringGetBit()
    {
        prefixed.StringGetBit("key", 123, CommandFlags.None);
        mock.Verify(_ => _.StringGetBit("prefix:key", 123, CommandFlags.None));
    }

    [Fact]
    public void StringGetRange()
    {
        prefixed.StringGetRange("key", 123, 456, CommandFlags.None);
        mock.Verify(_ => _.StringGetRange("prefix:key", 123, 456, CommandFlags.None));
    }

    [Fact]
    public void StringGetSet()
    {
        prefixed.StringGetSet("key", "value", CommandFlags.None);
        mock.Verify(_ => _.StringGetSet("prefix:key", "value", CommandFlags.None));
    }

    [Fact]
    public void StringGetDelete()
    {
        prefixed.StringGetDelete("key", CommandFlags.None);
        mock.Verify(_ => _.StringGetDelete("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void StringGetWithExpiry()
    {
        prefixed.StringGetWithExpiry("key", CommandFlags.None);
        mock.Verify(_ => _.StringGetWithExpiry("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void StringIncrement_1()
    {
        prefixed.StringIncrement("key", 123, CommandFlags.None);
        mock.Verify(_ => _.StringIncrement("prefix:key", 123, CommandFlags.None));
    }

    [Fact]
    public void StringIncrement_2()
    {
        prefixed.StringIncrement("key", 1.23, CommandFlags.None);
        mock.Verify(_ => _.StringIncrement("prefix:key", 1.23, CommandFlags.None));
    }

    [Fact]
    public void StringLength()
    {
        prefixed.StringLength("key", CommandFlags.None);
        mock.Verify(_ => _.StringLength("prefix:key", CommandFlags.None));
    }

    [Fact]
    public void StringSet_1()
    {
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.StringSet("key", "value", expiry, When.Exists, CommandFlags.None);
        mock.Verify(_ => _.StringSet("prefix:key", "value", expiry, When.Exists, CommandFlags.None));
    }

    [Fact]
    public void StringSet_2()
    {
        TimeSpan? expiry = null;
        prefixed.StringSet("key", "value", expiry, true, When.Exists, CommandFlags.None);
        mock.Verify(_ => _.StringSet("prefix:key", "value", expiry, true, When.Exists, CommandFlags.None));
    }

    [Fact]
    public void StringSet_3()
    {
        KeyValuePair<RedisKey, RedisValue>[] values = new KeyValuePair<RedisKey, RedisValue>[] { new KeyValuePair<RedisKey, RedisValue>("a", "x"), new KeyValuePair<RedisKey, RedisValue>("b", "y") };
        Expression<Func<KeyValuePair<RedisKey, RedisValue>[], bool>> valid = _ => _.Length == 2 && _[0].Key == "prefix:a" && _[0].Value == "x" && _[1].Key == "prefix:b" && _[1].Value == "y";
        prefixed.StringSet(values, When.Exists, CommandFlags.None);
        mock.Verify(_ => _.StringSet(It.Is(valid), When.Exists, CommandFlags.None));
    }

    [Fact]
    public void StringSet_Compat()
    {
        TimeSpan? expiry = null;
        prefixed.StringSet("key", "value", expiry, When.Exists);
        mock.Verify(_ => _.StringSet("prefix:key", "value", expiry, When.Exists));
    }

    [Fact]
    public void StringSetBit()
    {
        prefixed.StringSetBit("key", 123, true, CommandFlags.None);
        mock.Verify(_ => _.StringSetBit("prefix:key", 123, true, CommandFlags.None));
    }

    [Fact]
    public void StringSetRange()
    {
        prefixed.StringSetRange("key", 123, "value", CommandFlags.None);
        mock.Verify(_ => _.StringSetRange("prefix:key", 123, "value", CommandFlags.None));
    }
}
