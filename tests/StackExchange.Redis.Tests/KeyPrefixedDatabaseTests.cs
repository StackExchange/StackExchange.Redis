﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Text;
using NSubstitute;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests;

[CollectionDefinition(nameof(SubstituteDependentCollection), DisableParallelization = true)]
public class SubstituteDependentCollection { }

[Collection(nameof(SubstituteDependentCollection))]
public sealed class KeyPrefixedDatabaseTests
{
    private readonly IDatabase mock;
    private readonly IDatabase prefixed;

    internal static RedisKey[] IsKeys(params RedisKey[] expected) => IsRaw(expected);
    internal static RedisValue[] IsValues(params RedisValue[] expected) => IsRaw(expected);
    private static T[] IsRaw<T>(T[] expected)
    {
        Expression<Predicate<T[]>> lambda = actual => actual.Length == expected.Length && expected.SequenceEqual(actual);
        return Arg.Is(lambda);
    }

    public KeyPrefixedDatabaseTests()
    {
        mock = Substitute.For<IDatabase>();
        prefixed = new KeyPrefixedDatabase(mock, Encoding.UTF8.GetBytes("prefix:"));
    }

    [Fact]
    public void CreateBatch()
    {
        object asyncState = new();
        IBatch innerBatch = Substitute.For<IBatch>();
        mock.CreateBatch(asyncState).Returns(innerBatch);
        IBatch wrappedBatch = prefixed.CreateBatch(asyncState);
        mock.Received().CreateBatch(asyncState);
        Assert.IsType<KeyPrefixedBatch>(wrappedBatch);
        Assert.Same(innerBatch, ((KeyPrefixedBatch)wrappedBatch).Inner);
    }

    [Fact]
    public void CreateTransaction()
    {
        object asyncState = new();
        ITransaction innerTransaction = Substitute.For<ITransaction>();
        mock.CreateTransaction(asyncState).Returns(innerTransaction);
        ITransaction wrappedTransaction = prefixed.CreateTransaction(asyncState);
        mock.Received().CreateTransaction(asyncState);
        Assert.IsType<KeyPrefixedTransaction>(wrappedTransaction);
        Assert.Same(innerTransaction, ((KeyPrefixedTransaction)wrappedTransaction).Inner);
    }

    [Fact]
    public void DebugObject()
    {
        prefixed.DebugObject("key", CommandFlags.None);
        mock.Received().DebugObject("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void Get_Database()
    {
        mock.Database.Returns(123);
        Assert.Equal(123, prefixed.Database);
    }

    [Fact]
    public void HashDecrement_1()
    {
        prefixed.HashDecrement("key", "hashField", 123, CommandFlags.None);
        mock.Received().HashDecrement("prefix:key", "hashField", 123, CommandFlags.None);
    }

    [Fact]
    public void HashDecrement_2()
    {
        prefixed.HashDecrement("key", "hashField", 1.23, CommandFlags.None);
        mock.Received().HashDecrement("prefix:key", "hashField", 1.23, CommandFlags.None);
    }

    [Fact]
    public void HashDelete_1()
    {
        prefixed.HashDelete("key", "hashField", CommandFlags.None);
        mock.Received().HashDelete("prefix:key", "hashField", CommandFlags.None);
    }

    [Fact]
    public void HashDelete_2()
    {
        RedisValue[] hashFields = Array.Empty<RedisValue>();
        prefixed.HashDelete("key", hashFields, CommandFlags.None);
        mock.Received().HashDelete("prefix:key", hashFields, CommandFlags.None);
    }

    [Fact]
    public void HashExists()
    {
        prefixed.HashExists("key", "hashField", CommandFlags.None);
        mock.Received().HashExists("prefix:key", "hashField", CommandFlags.None);
    }

    [Fact]
    public void HashGet_1()
    {
        prefixed.HashGet("key", "hashField", CommandFlags.None);
        mock.Received().HashGet("prefix:key", "hashField", CommandFlags.None);
    }

    [Fact]
    public void HashGet_2()
    {
        RedisValue[] hashFields = Array.Empty<RedisValue>();
        prefixed.HashGet("key", hashFields, CommandFlags.None);
        mock.Received().HashGet("prefix:key", hashFields, CommandFlags.None);
    }

    [Fact]
    public void HashGetAll()
    {
        prefixed.HashGetAll("key", CommandFlags.None);
        mock.Received().HashGetAll("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void HashIncrement_1()
    {
        prefixed.HashIncrement("key", "hashField", 123, CommandFlags.None);
        mock.Received().HashIncrement("prefix:key", "hashField", 123, CommandFlags.None);
    }

    [Fact]
    public void HashIncrement_2()
    {
        prefixed.HashIncrement("key", "hashField", 1.23, CommandFlags.None);
        mock.Received().HashIncrement("prefix:key", "hashField", 1.23, CommandFlags.None);
    }

    [Fact]
    public void HashKeys()
    {
        prefixed.HashKeys("key", CommandFlags.None);
        mock.Received().HashKeys("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void HashLength()
    {
        prefixed.HashLength("key", CommandFlags.None);
        mock.Received().HashLength("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void HashScan()
    {
        prefixed.HashScan("key", "pattern", 123, flags: CommandFlags.None);
        mock.Received().HashScan("prefix:key", "pattern", 123, CommandFlags.None);
    }

    [Fact]
    public void HashScan_Full()
    {
        prefixed.HashScan("key", "pattern", 123, 42, 64, flags: CommandFlags.None);
        mock.Received().HashScan("prefix:key", "pattern", 123, 42, 64, CommandFlags.None);
    }

    [Fact]
    public void HashScanNoValues()
    {
        prefixed.HashScanNoValues("key", "pattern", 123, flags: CommandFlags.None);
        mock.Received().HashScanNoValues("prefix:key", "pattern", 123, flags: CommandFlags.None);
    }

    [Fact]
    public void HashScanNoValues_Full()
    {
        prefixed.HashScanNoValues("key", "pattern", 123, 42, 64, flags: CommandFlags.None);
        mock.Received().HashScanNoValues("prefix:key", "pattern", 123, 42, 64, CommandFlags.None);
    }

    [Fact]
    public void HashSet_1()
    {
        HashEntry[] hashFields = Array.Empty<HashEntry>();
        prefixed.HashSet("key", hashFields, CommandFlags.None);
        mock.Received().HashSet("prefix:key", hashFields, CommandFlags.None);
    }

    [Fact]
    public void HashSet_2()
    {
        prefixed.HashSet("key", "hashField", "value", When.Exists, CommandFlags.None);
        mock.Received().HashSet("prefix:key", "hashField", "value", When.Exists, CommandFlags.None);
    }

    [Fact]
    public void HashStringLength()
    {
        prefixed.HashStringLength("key", "field", CommandFlags.None);
        mock.Received().HashStringLength("prefix:key", "field", CommandFlags.None);
    }

    [Fact]
    public void HashValues()
    {
        prefixed.HashValues("key", CommandFlags.None);
        mock.Received().HashValues("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void HyperLogLogAdd_1()
    {
        prefixed.HyperLogLogAdd("key", "value", CommandFlags.None);
        mock.Received().HyperLogLogAdd("prefix:key", "value", CommandFlags.None);
    }

    [Fact]
    public void HyperLogLogAdd_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        prefixed.HyperLogLogAdd("key", values, CommandFlags.None);
        mock.Received().HyperLogLogAdd("prefix:key", values, CommandFlags.None);
    }

    [Fact]
    public void HyperLogLogLength()
    {
        prefixed.HyperLogLogLength("key", CommandFlags.None);
        mock.Received().HyperLogLogLength("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void HyperLogLogMerge_1()
    {
        prefixed.HyperLogLogMerge("destination", "first", "second", CommandFlags.None);
        mock.Received().HyperLogLogMerge("prefix:destination", "prefix:first", "prefix:second", CommandFlags.None);
    }

    [Fact]
    public void HyperLogLogMerge_2()
    {
        prefixed.HyperLogLogMerge("destination", ["a", "b"], CommandFlags.None);
        mock.Received().HyperLogLogMerge("prefix:destination", IsKeys(["prefix:a", "prefix:b"]), CommandFlags.None);
    }

    [Fact]
    public void IdentifyEndpoint()
    {
        prefixed.IdentifyEndpoint("key", CommandFlags.None);
        mock.Received().IdentifyEndpoint("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void KeyCopy()
    {
        prefixed.KeyCopy("key", "destination", flags: CommandFlags.None);
        mock.Received().KeyCopy("prefix:key", "prefix:destination", -1, false, CommandFlags.None);
    }

    [Fact]
    public void KeyDelete_1()
    {
        prefixed.KeyDelete("key", CommandFlags.None);
        mock.Received().KeyDelete("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void KeyDelete_2()
    {
        prefixed.KeyDelete(["a", "b"], CommandFlags.None);
        mock.Received().KeyDelete(IsKeys(["prefix:a", "prefix:b"]), CommandFlags.None);
    }

    [Fact]
    public void KeyDump()
    {
        prefixed.KeyDump("key", CommandFlags.None);
        mock.Received().KeyDump("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void KeyEncoding()
    {
        prefixed.KeyEncoding("key", CommandFlags.None);
        mock.Received().KeyEncoding("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void KeyExists()
    {
        prefixed.KeyExists("key", CommandFlags.None);
        mock.Received().KeyExists("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void KeyExpire_1()
    {
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.KeyExpire("key", expiry, CommandFlags.None);
        mock.Received().KeyExpire("prefix:key", expiry, CommandFlags.None);
    }

    [Fact]
    public void KeyExpire_2()
    {
        DateTime expiry = DateTime.Now;
        prefixed.KeyExpire("key", expiry, CommandFlags.None);
        mock.Received().KeyExpire("prefix:key", expiry, CommandFlags.None);
    }

    [Fact]
    public void KeyExpire_3()
    {
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.KeyExpire("key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
        mock.Received().KeyExpire("prefix:key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
    }

    [Fact]
    public void KeyExpire_4()
    {
        DateTime expiry = DateTime.Now;
        prefixed.KeyExpire("key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
        mock.Received().KeyExpire("prefix:key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
    }

    [Fact]
    public void KeyExpireTime()
    {
        prefixed.KeyExpireTime("key", CommandFlags.None);
        mock.Received().KeyExpireTime("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void KeyFrequency()
    {
        prefixed.KeyFrequency("key", CommandFlags.None);
        mock.Received().KeyFrequency("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void KeyMigrate()
    {
        EndPoint toServer = new IPEndPoint(IPAddress.Loopback, 123);
        prefixed.KeyMigrate("key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.None);
        mock.Received().KeyMigrate("prefix:key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.None);
    }

    [Fact]
    public void KeyMove()
    {
        prefixed.KeyMove("key", 123, CommandFlags.None);
        mock.Received().KeyMove("prefix:key", 123, CommandFlags.None);
    }

    [Fact]
    public void KeyPersist()
    {
        prefixed.KeyPersist("key", CommandFlags.None);
        mock.Received().KeyPersist("prefix:key", CommandFlags.None);
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
        mock.Received().KeyRefCount("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void KeyRename()
    {
        prefixed.KeyRename("key", "newKey", When.Exists, CommandFlags.None);
        mock.Received().KeyRename("prefix:key", "prefix:newKey", When.Exists, CommandFlags.None);
    }

    [Fact]
    public void KeyRestore()
    {
        byte[] value = Array.Empty<byte>();
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.KeyRestore("key", value, expiry, CommandFlags.None);
        mock.Received().KeyRestore("prefix:key", value, expiry, CommandFlags.None);
    }

    [Fact]
    public void KeyTimeToLive()
    {
        prefixed.KeyTimeToLive("key", CommandFlags.None);
        mock.Received().KeyTimeToLive("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void KeyType()
    {
        prefixed.KeyType("key", CommandFlags.None);
        mock.Received().KeyType("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void ListGetByIndex()
    {
        prefixed.ListGetByIndex("key", 123, CommandFlags.None);
        mock.Received().ListGetByIndex("prefix:key", 123, CommandFlags.None);
    }

    [Fact]
    public void ListInsertAfter()
    {
        prefixed.ListInsertAfter("key", "pivot", "value", CommandFlags.None);
        mock.Received().ListInsertAfter("prefix:key", "pivot", "value", CommandFlags.None);
    }

    [Fact]
    public void ListInsertBefore()
    {
        prefixed.ListInsertBefore("key", "pivot", "value", CommandFlags.None);
        mock.Received().ListInsertBefore("prefix:key", "pivot", "value", CommandFlags.None);
    }

    [Fact]
    public void ListLeftPop()
    {
        prefixed.ListLeftPop("key", CommandFlags.None);
        mock.Received().ListLeftPop("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void ListLeftPop_1()
    {
        prefixed.ListLeftPop("key", 123, CommandFlags.None);
        mock.Received().ListLeftPop("prefix:key", 123, CommandFlags.None);
    }

    [Fact]
    public void ListLeftPush_1()
    {
        prefixed.ListLeftPush("key", "value", When.Exists, CommandFlags.None);
        mock.Received().ListLeftPush("prefix:key", "value", When.Exists, CommandFlags.None);
    }

    [Fact]
    public void ListLeftPush_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        prefixed.ListLeftPush("key", values, CommandFlags.None);
        mock.Received().ListLeftPush("prefix:key", values, CommandFlags.None);
    }

    [Fact]
    public void ListLeftPush_3()
    {
        RedisValue[] values = ["value1", "value2"];
        prefixed.ListLeftPush("key", values, When.Exists, CommandFlags.None);
        mock.Received().ListLeftPush("prefix:key", values, When.Exists, CommandFlags.None);
    }

    [Fact]
    public void ListLength()
    {
        prefixed.ListLength("key", CommandFlags.None);
        mock.Received().ListLength("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void ListMove()
    {
        prefixed.ListMove("key", "destination", ListSide.Left, ListSide.Right, CommandFlags.None);
        mock.Received().ListMove("prefix:key", "prefix:destination", ListSide.Left, ListSide.Right, CommandFlags.None);
    }

    [Fact]
    public void ListRange()
    {
        prefixed.ListRange("key", 123, 456, CommandFlags.None);
        mock.Received().ListRange("prefix:key", 123, 456, CommandFlags.None);
    }

    [Fact]
    public void ListRemove()
    {
        prefixed.ListRemove("key", "value", 123, CommandFlags.None);
        mock.Received().ListRemove("prefix:key", "value", 123, CommandFlags.None);
    }

    [Fact]
    public void ListRightPop()
    {
        prefixed.ListRightPop("key", CommandFlags.None);
        mock.Received().ListRightPop("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void ListRightPop_1()
    {
        prefixed.ListRightPop("key", 123, CommandFlags.None);
        mock.Received().ListRightPop("prefix:key", 123, CommandFlags.None);
    }

    [Fact]
    public void ListRightPopLeftPush()
    {
        prefixed.ListRightPopLeftPush("source", "destination", CommandFlags.None);
        mock.Received().ListRightPopLeftPush("prefix:source", "prefix:destination", CommandFlags.None);
    }

    [Fact]
    public void ListRightPush_1()
    {
        prefixed.ListRightPush("key", "value", When.Exists, CommandFlags.None);
        mock.Received().ListRightPush("prefix:key", "value", When.Exists, CommandFlags.None);
    }

    [Fact]
    public void ListRightPush_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        prefixed.ListRightPush("key", values, CommandFlags.None);
        mock.Received().ListRightPush("prefix:key", values, CommandFlags.None);
    }

    [Fact]
    public void ListRightPush_3()
    {
        RedisValue[] values = ["value1", "value2"];
        prefixed.ListRightPush("key", values, When.Exists, CommandFlags.None);
        mock.Received().ListRightPush("prefix:key", values, When.Exists, CommandFlags.None);
    }

    [Fact]
    public void ListSetByIndex()
    {
        prefixed.ListSetByIndex("key", 123, "value", CommandFlags.None);
        mock.Received().ListSetByIndex("prefix:key", 123, "value", CommandFlags.None);
    }

    [Fact]
    public void ListTrim()
    {
        prefixed.ListTrim("key", 123, 456, CommandFlags.None);
        mock.Received().ListTrim("prefix:key", 123, 456, CommandFlags.None);
    }

    [Fact]
    public void LockExtend()
    {
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.LockExtend("key", "value", expiry, CommandFlags.None);
        mock.Received().LockExtend("prefix:key", "value", expiry, CommandFlags.None);
    }

    [Fact]
    public void LockQuery()
    {
        prefixed.LockQuery("key", CommandFlags.None);
        mock.Received().LockQuery("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void LockRelease()
    {
        prefixed.LockRelease("key", "value", CommandFlags.None);
        mock.Received().LockRelease("prefix:key", "value", CommandFlags.None);
    }

    [Fact]
    public void LockTake()
    {
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.LockTake("key", "value", expiry, CommandFlags.None);
        mock.Received().LockTake("prefix:key", "value", expiry, CommandFlags.None);
    }

    [Fact]
    public void Publish()
    {
#pragma warning disable CS0618
        prefixed.Publish("channel", "message", CommandFlags.None);
        mock.Received().Publish("prefix:channel", "message", CommandFlags.None);
#pragma warning restore CS0618
    }

    [Fact]
    public void ScriptEvaluate_1()
    {
        byte[] hash = Array.Empty<byte>();
        RedisValue[] values = Array.Empty<RedisValue>();
        RedisKey[] keys = ["a", "b"];
        prefixed.ScriptEvaluate(hash, keys, values, CommandFlags.None);
        mock.Received().ScriptEvaluate(hash, IsKeys(["prefix:a", "prefix:b"]), values, CommandFlags.None);
    }

    [Fact]
    public void ScriptEvaluate_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        RedisKey[] keys = ["a", "b"];
        prefixed.ScriptEvaluate(script: "script", keys: keys, values: values, flags: CommandFlags.None);
        mock.Received().ScriptEvaluate(script: "script", keys: IsKeys(["prefix:a", "prefix:b"]), values: values, flags: CommandFlags.None);
    }

    [Fact]
    public void SetAdd_1()
    {
        prefixed.SetAdd("key", "value", CommandFlags.None);
        mock.Received().SetAdd("prefix:key", "value", CommandFlags.None);
    }

    [Fact]
    public void SetAdd_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        prefixed.SetAdd("key", values, CommandFlags.None);
        mock.Received().SetAdd("prefix:key", values, CommandFlags.None);
    }

    [Fact]
    public void SetCombine_1()
    {
        prefixed.SetCombine(SetOperation.Intersect, "first", "second", CommandFlags.None);
        mock.Received().SetCombine(SetOperation.Intersect, "prefix:first", "prefix:second", CommandFlags.None);
    }

    [Fact]
    public void SetCombine_2()
    {
        RedisKey[] keys = ["a", "b"];
        prefixed.SetCombine(SetOperation.Intersect, keys, CommandFlags.None);
        mock.Received().SetCombine(SetOperation.Intersect, IsKeys(["prefix:a", "prefix:b"]), CommandFlags.None);
    }

    [Fact]
    public void SetCombineAndStore_1()
    {
        prefixed.SetCombineAndStore(SetOperation.Intersect, "destination", "first", "second", CommandFlags.None);
        mock.Received().SetCombineAndStore(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.None);
    }

    [Fact]
    public void SetCombineAndStore_2()
    {
        RedisKey[] keys = ["a", "b"];
        prefixed.SetCombineAndStore(SetOperation.Intersect, "destination", keys, CommandFlags.None);
        mock.Received().SetCombineAndStore(SetOperation.Intersect, "prefix:destination", IsKeys(["prefix:a", "prefix:b"]), CommandFlags.None);
    }

    [Fact]
    public void SetContains()
    {
        prefixed.SetContains("key", "value", CommandFlags.None);
        mock.Received().SetContains("prefix:key", "value", CommandFlags.None);
    }

    [Fact]
    public void SetContains_2()
    {
        RedisValue[] values = ["value1", "value2"];
        prefixed.SetContains("key", values, CommandFlags.None);
        mock.Received().SetContains("prefix:key", values, CommandFlags.None);
    }

    [Fact]
    public void SetIntersectionLength()
    {
        prefixed.SetIntersectionLength(["key1", "key2"]);
        mock.Received().SetIntersectionLength(IsKeys(["prefix:key1", "prefix:key2"]), 0, CommandFlags.None);
    }

    [Fact]
    public void SetLength()
    {
        prefixed.SetLength("key", CommandFlags.None);
        mock.Received().SetLength("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void SetMembers()
    {
        prefixed.SetMembers("key", CommandFlags.None);
        mock.Received().SetMembers("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void SetMove()
    {
        prefixed.SetMove("source", "destination", "value", CommandFlags.None);
        mock.Received().SetMove("prefix:source", "prefix:destination", "value", CommandFlags.None);
    }

    [Fact]
    public void SetPop_1()
    {
        prefixed.SetPop("key", CommandFlags.None);
        mock.Received().SetPop("prefix:key", CommandFlags.None);

        prefixed.SetPop("key", 5, CommandFlags.None);
        mock.Received().SetPop("prefix:key", 5, CommandFlags.None);
    }

    [Fact]
    public void SetPop_2()
    {
        prefixed.SetPop("key", 5, CommandFlags.None);
        mock.Received().SetPop("prefix:key", 5, CommandFlags.None);
    }

    [Fact]
    public void SetRandomMember()
    {
        prefixed.SetRandomMember("key", CommandFlags.None);
        mock.Received().SetRandomMember("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void SetRandomMembers()
    {
        prefixed.SetRandomMembers("key", 123, CommandFlags.None);
        mock.Received().SetRandomMembers("prefix:key", 123, CommandFlags.None);
    }

    [Fact]
    public void SetRemove_1()
    {
        prefixed.SetRemove("key", "value", CommandFlags.None);
        mock.Received().SetRemove("prefix:key", "value", CommandFlags.None);
    }

    [Fact]
    public void SetRemove_2()
    {
        RedisValue[] values = Array.Empty<RedisValue>();
        prefixed.SetRemove("key", values, CommandFlags.None);
        mock.Received().SetRemove("prefix:key", values, CommandFlags.None);
    }

    [Fact]
    public void SetScan()
    {
        prefixed.SetScan("key", "pattern", 123, flags: CommandFlags.None);
        mock.Received().SetScan("prefix:key", "pattern", 123, CommandFlags.None);
    }

    [Fact]
    public void SetScan_Full()
    {
        prefixed.SetScan("key", "pattern", 123, 42, 64, flags: CommandFlags.None);
        mock.Received().SetScan("prefix:key", "pattern", 123, 42, 64, CommandFlags.None);
    }

    [Fact]
    public void Sort()
    {
        RedisValue[] get = ["a", "#"];

        prefixed.Sort("key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.None);
        prefixed.Sort("key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.None);

        mock.Received().Sort("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", IsValues(["prefix:a", "#"]), CommandFlags.None);
        mock.Received().Sort("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", IsValues(["prefix:a", "#"]), CommandFlags.None);
    }

    [Fact]
    public void SortAndStore()
    {
        RedisValue[] get = ["a", "#"];

        prefixed.SortAndStore("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.None);
        prefixed.SortAndStore("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.None);

        mock.Received().SortAndStore("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", IsValues(["prefix:a", "#"]), CommandFlags.None);
        mock.Received().SortAndStore("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", IsValues(["prefix:a", "#"]), CommandFlags.None);
    }

    [Fact]
    public void SortedSetAdd_1()
    {
        prefixed.SortedSetAdd("key", "member", 1.23, When.Exists, CommandFlags.None);
        mock.Received().SortedSetAdd("prefix:key", "member", 1.23, When.Exists, CommandFlags.None);
    }

    [Fact]
    public void SortedSetAdd_2()
    {
        SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
        prefixed.SortedSetAdd("key", values, When.Exists, CommandFlags.None);
        mock.Received().SortedSetAdd("prefix:key", values, When.Exists, CommandFlags.None);
    }

    [Fact]
    public void SortedSetAdd_3()
    {
        SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
        prefixed.SortedSetAdd("key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
        mock.Received().SortedSetAdd("prefix:key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
    }

    [Fact]
    public void SortedSetCombine()
    {
        RedisKey[] keys = ["a", "b"];
        prefixed.SortedSetCombine(SetOperation.Intersect, ["a", "b"]);
        mock.Received().SortedSetCombine(SetOperation.Intersect, IsKeys(["prefix:a", "prefix:b"]), null, Aggregate.Sum, CommandFlags.None);
    }

    [Fact]
    public void SortedSetCombineWithScores()
    {
        prefixed.SortedSetCombineWithScores(SetOperation.Intersect, ["a", "b"]);
        mock.Received().SortedSetCombineWithScores(SetOperation.Intersect, IsKeys("prefix:a", "prefix:b"), null, Aggregate.Sum, CommandFlags.None);
    }

    [Fact]
    public void SortedSetCombineAndStore_1()
    {
        prefixed.SortedSetCombineAndStore(SetOperation.Intersect, "destination", "first", "second", Aggregate.Max, CommandFlags.None);
        mock.Received().SortedSetCombineAndStore(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", Aggregate.Max, CommandFlags.None);
    }

    [Fact]
    public void SortedSetCombineAndStore_2()
    {
        RedisKey[] keys = ["a", "b"];
        prefixed.SetCombineAndStore(SetOperation.Intersect, "destination", keys, CommandFlags.None);
        mock.Received().SetCombineAndStore(SetOperation.Intersect, "prefix:destination", IsKeys("prefix:a", "prefix:b"), CommandFlags.None);
    }

    [Fact]
    public void SortedSetDecrement()
    {
        prefixed.SortedSetDecrement("key", "member", 1.23, CommandFlags.None);
        mock.Received().SortedSetDecrement("prefix:key", "member", 1.23, CommandFlags.None);
    }

    [Fact]
    public void SortedSetIncrement()
    {
        prefixed.SortedSetIncrement("key", "member", 1.23, CommandFlags.None);
        mock.Received().SortedSetIncrement("prefix:key", "member", 1.23, CommandFlags.None);
    }

    [Fact]
    public void SortedSetIntersectionLength()
    {
        prefixed.SortedSetIntersectionLength(["a", "b"], 1, CommandFlags.None);
        mock.Received().SortedSetIntersectionLength(IsKeys("prefix:a", "prefix:b"), 1, CommandFlags.None);
    }

    [Fact]
    public void SortedSetLength()
    {
        prefixed.SortedSetLength("key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
        mock.Received().SortedSetLength("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRandomMember()
    {
        prefixed.SortedSetRandomMember("key", CommandFlags.None);
        mock.Received().SortedSetRandomMember("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void SortedSetRandomMembers()
    {
        prefixed.SortedSetRandomMembers("key", 2, CommandFlags.None);
        mock.Received().SortedSetRandomMembers("prefix:key", 2, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRandomMembersWithScores()
    {
        prefixed.SortedSetRandomMembersWithScores("key", 2, CommandFlags.None);
        mock.Received().SortedSetRandomMembersWithScores("prefix:key", 2, CommandFlags.None);
    }

    [Fact]
    public void SortedSetLengthByValue()
    {
        prefixed.SortedSetLengthByValue("key", "min", "max", Exclude.Start, CommandFlags.None);
        mock.Received().SortedSetLengthByValue("prefix:key", "min", "max", Exclude.Start, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRangeByRank()
    {
        prefixed.SortedSetRangeByRank("key", 123, 456, Order.Descending, CommandFlags.None);
        mock.Received().SortedSetRangeByRank("prefix:key", 123, 456, Order.Descending, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRangeByRankWithScores()
    {
        prefixed.SortedSetRangeByRankWithScores("key", 123, 456, Order.Descending, CommandFlags.None);
        mock.Received().SortedSetRangeByRankWithScores("prefix:key", 123, 456, Order.Descending, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRangeByScore()
    {
        prefixed.SortedSetRangeByScore("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
        mock.Received().SortedSetRangeByScore("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRangeByScoreWithScores()
    {
        prefixed.SortedSetRangeByScoreWithScores("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
        mock.Received().SortedSetRangeByScoreWithScores("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRangeByValue()
    {
        prefixed.SortedSetRangeByValue("key", "min", "max", Exclude.Start, 123, 456, CommandFlags.None);
        mock.Received().SortedSetRangeByValue("prefix:key", "min", "max", Exclude.Start, Order.Ascending, 123, 456, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRangeByValueDesc()
    {
        prefixed.SortedSetRangeByValue("key", "min", "max", Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
        mock.Received().SortedSetRangeByValue("prefix:key", "min", "max", Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRank()
    {
        prefixed.SortedSetRank("key", "member", Order.Descending, CommandFlags.None);
        mock.Received().SortedSetRank("prefix:key", "member", Order.Descending, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRemove_1()
    {
        prefixed.SortedSetRemove("key", "member", CommandFlags.None);
        mock.Received().SortedSetRemove("prefix:key", "member", CommandFlags.None);
    }

    [Fact]
    public void SortedSetRemove_2()
    {
        RedisValue[] members = Array.Empty<RedisValue>();
        prefixed.SortedSetRemove("key", members, CommandFlags.None);
        mock.Received().SortedSetRemove("prefix:key", members, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRemoveRangeByRank()
    {
        prefixed.SortedSetRemoveRangeByRank("key", 123, 456, CommandFlags.None);
        mock.Received().SortedSetRemoveRangeByRank("prefix:key", 123, 456, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRemoveRangeByScore()
    {
        prefixed.SortedSetRemoveRangeByScore("key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
        mock.Received().SortedSetRemoveRangeByScore("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
    }

    [Fact]
    public void SortedSetRemoveRangeByValue()
    {
        prefixed.SortedSetRemoveRangeByValue("key", "min", "max", Exclude.Start, CommandFlags.None);
        mock.Received().SortedSetRemoveRangeByValue("prefix:key", "min", "max", Exclude.Start, CommandFlags.None);
    }

    [Fact]
    public void SortedSetScan()
    {
        prefixed.SortedSetScan("key", "pattern", 123, flags: CommandFlags.None);
        mock.Received().SortedSetScan("prefix:key", "pattern", 123, CommandFlags.None);
    }

    [Fact]
    public void SortedSetScan_Full()
    {
        prefixed.SortedSetScan("key", "pattern", 123, 42, 64, flags: CommandFlags.None);
        mock.Received().SortedSetScan("prefix:key", "pattern", 123, 42, 64, CommandFlags.None);
    }

    [Fact]
    public void SortedSetScore()
    {
        prefixed.SortedSetScore("key", "member", CommandFlags.None);
        mock.Received().SortedSetScore("prefix:key", "member", CommandFlags.None);
    }

    [Fact]
    public void SortedSetScore_Multiple()
    {
        var values = new RedisValue[] { "member1", "member2" };
        prefixed.SortedSetScores("key", values, CommandFlags.None);
        mock.Received().SortedSetScores("prefix:key", values, CommandFlags.None);
    }

    [Fact]
    public void SortedSetUpdate()
    {
        SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
        prefixed.SortedSetUpdate("key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
        mock.Received().SortedSetUpdate("prefix:key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
    }

    [Fact]
    public void StreamAcknowledge_1()
    {
        prefixed.StreamAcknowledge("key", "group", "0-0", CommandFlags.None);
        mock.Received().StreamAcknowledge("prefix:key", "group", "0-0", CommandFlags.None);
    }

    [Fact]
    public void StreamAcknowledge_2()
    {
        var messageIds = new RedisValue[] { "0-0", "0-1", "0-2" };
        prefixed.StreamAcknowledge("key", "group", messageIds, CommandFlags.None);
        mock.Received().StreamAcknowledge("prefix:key", "group", messageIds, CommandFlags.None);
    }

    [Fact]
    public void StreamAdd_1()
    {
        prefixed.StreamAdd("key", "field1", "value1", "*", 1000, true, CommandFlags.None);
        mock.Received().StreamAdd("prefix:key", "field1", "value1", "*", 1000, true, CommandFlags.None);
    }

    [Fact]
    public void StreamAdd_2()
    {
        var fields = Array.Empty<NameValueEntry>();
        prefixed.StreamAdd("key", fields, "*", 1000, true, CommandFlags.None);
        mock.Received().StreamAdd("prefix:key", fields, "*", 1000, true, CommandFlags.None);
    }

    [Fact]
    public void StreamAutoClaim()
    {
        prefixed.StreamAutoClaim("key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
        mock.Received().StreamAutoClaim("prefix:key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
    }

    [Fact]
    public void StreamAutoClaimIdsOnly()
    {
        prefixed.StreamAutoClaimIdsOnly("key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
        mock.Received().StreamAutoClaimIdsOnly("prefix:key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
    }

    [Fact]
    public void StreamClaimMessages()
    {
        var messageIds = Array.Empty<RedisValue>();
        prefixed.StreamClaim("key", "group", "consumer", 1000, messageIds, CommandFlags.None);
        mock.Received().StreamClaim("prefix:key", "group", "consumer", 1000, messageIds, CommandFlags.None);
    }

    [Fact]
    public void StreamClaimMessagesReturningIds()
    {
        var messageIds = Array.Empty<RedisValue>();
        prefixed.StreamClaimIdsOnly("key", "group", "consumer", 1000, messageIds, CommandFlags.None);
        mock.Received().StreamClaimIdsOnly("prefix:key", "group", "consumer", 1000, messageIds, CommandFlags.None);
    }

    [Fact]
    public void StreamConsumerGroupSetPosition()
    {
        prefixed.StreamConsumerGroupSetPosition("key", "group", StreamPosition.Beginning, CommandFlags.None);
        mock.Received().StreamConsumerGroupSetPosition("prefix:key", "group", StreamPosition.Beginning, CommandFlags.None);
    }

    [Fact]
    public void StreamConsumerInfoGet()
    {
        prefixed.StreamConsumerInfo("key", "group", CommandFlags.None);
        mock.Received().StreamConsumerInfo("prefix:key", "group", CommandFlags.None);
    }

    [Fact]
    public void StreamCreateConsumerGroup()
    {
        prefixed.StreamCreateConsumerGroup("key", "group", StreamPosition.Beginning, false, CommandFlags.None);
        mock.Received().StreamCreateConsumerGroup("prefix:key", "group", StreamPosition.Beginning, false, CommandFlags.None);
    }

    [Fact]
    public void StreamGroupInfoGet()
    {
        prefixed.StreamGroupInfo("key", CommandFlags.None);
        mock.Received().StreamGroupInfo("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void StreamInfoGet()
    {
        prefixed.StreamInfo("key", CommandFlags.None);
        mock.Received().StreamInfo("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void StreamLength()
    {
        prefixed.StreamLength("key", CommandFlags.None);
        mock.Received().StreamLength("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void StreamMessagesDelete()
    {
        var messageIds = Array.Empty<RedisValue>();
        prefixed.StreamDelete("key", messageIds, CommandFlags.None);
        mock.Received().StreamDelete("prefix:key", messageIds, CommandFlags.None);
    }

    [Fact]
    public void StreamDeleteConsumer()
    {
        prefixed.StreamDeleteConsumer("key", "group", "consumer", CommandFlags.None);
        mock.Received().StreamDeleteConsumer("prefix:key", "group", "consumer", CommandFlags.None);
    }

    [Fact]
    public void StreamDeleteConsumerGroup()
    {
        prefixed.StreamDeleteConsumerGroup("key", "group", CommandFlags.None);
        mock.Received().StreamDeleteConsumerGroup("prefix:key", "group", CommandFlags.None);
    }

    [Fact]
    public void StreamPendingInfoGet()
    {
        prefixed.StreamPending("key", "group", CommandFlags.None);
        mock.Received().StreamPending("prefix:key", "group", CommandFlags.None);
    }

    [Fact]
    public void StreamPendingMessageInfoGet()
    {
        prefixed.StreamPendingMessages("key", "group", 10, RedisValue.Null, "-", "+", 1000, CommandFlags.None);
        mock.Received().StreamPendingMessages("prefix:key", "group", 10, RedisValue.Null, "-", "+", 1000, CommandFlags.None);
    }

    [Fact]
    public void StreamRange()
    {
        prefixed.StreamRange("key", "-", "+", null, Order.Ascending, CommandFlags.None);
        mock.Received().StreamRange("prefix:key", "-", "+", null, Order.Ascending, CommandFlags.None);
    }

    [Fact]
    public void StreamRead_1()
    {
        var streamPositions = Array.Empty<StreamPosition>();
        prefixed.StreamRead(streamPositions, null, CommandFlags.None);
        mock.Received().StreamRead(streamPositions, null, CommandFlags.None);
    }

    [Fact]
    public void StreamRead_2()
    {
        prefixed.StreamRead("key", "0-0", null, CommandFlags.None);
        mock.Received().StreamRead("prefix:key", "0-0", null, CommandFlags.None);
    }

    [Fact]
    public void StreamStreamReadGroup_1()
    {
        prefixed.StreamReadGroup("key", "group", "consumer", "0-0", 10, false, CommandFlags.None);
        mock.Received().StreamReadGroup("prefix:key", "group", "consumer", "0-0", 10, false, CommandFlags.None);
    }

    [Fact]
    public void StreamStreamReadGroup_2()
    {
        var streamPositions = Array.Empty<StreamPosition>();
        prefixed.StreamReadGroup(streamPositions, "group", "consumer", 10, false, CommandFlags.None);
        mock.Received().StreamReadGroup(streamPositions, "group", "consumer", 10, false, CommandFlags.None);
    }

    [Fact]
    public void StreamTrim()
    {
        prefixed.StreamTrim("key", 1000, true, CommandFlags.None);
        mock.Received().StreamTrim("prefix:key", 1000, true, CommandFlags.None);
    }

    [Fact]
    public void StreamTrimByMinId()
    {
        prefixed.StreamTrimByMinId("key", 1111111111);
        mock.Received().StreamTrimByMinId("prefix:key", 1111111111);
    }

    [Fact]
    public void StreamTrimByMinIdWithApproximate()
    {
        prefixed.StreamTrimByMinId("key", 1111111111, useApproximateMaxLength: true);
        mock.Received().StreamTrimByMinId("prefix:key", 1111111111, useApproximateMaxLength: true);
    }

    [Fact]
    public void StreamTrimByMinIdWithApproximateAndLimit()
    {
        prefixed.StreamTrimByMinId("key", 1111111111, useApproximateMaxLength: true, limit: 100);
        mock.Received().StreamTrimByMinId("prefix:key", 1111111111, useApproximateMaxLength: true, limit: 100);
    }

    [Fact]
    public void StringAppend()
    {
        prefixed.StringAppend("key", "value", CommandFlags.None);
        mock.Received().StringAppend("prefix:key", "value", CommandFlags.None);
    }

    [Fact]
    public void StringBitCount()
    {
        prefixed.StringBitCount("key", 123, 456, CommandFlags.None);
        mock.Received().StringBitCount("prefix:key", 123, 456, CommandFlags.None);
    }

    [Fact]
    public void StringBitCount_2()
    {
        prefixed.StringBitCount("key", 123, 456, StringIndexType.Byte, CommandFlags.None);
        mock.Received().StringBitCount("prefix:key", 123, 456, StringIndexType.Byte, CommandFlags.None);
    }

    [Fact]
    public void StringBitOperation_1()
    {
        prefixed.StringBitOperation(Bitwise.Xor, "destination", "first", "second", CommandFlags.None);
        mock.Received().StringBitOperation(Bitwise.Xor, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.None);
    }

    [Fact]
    public void StringBitOperation_2()
    {
        RedisKey[] keys = ["a", "b"];
        prefixed.StringBitOperation(Bitwise.Xor, "destination", keys, CommandFlags.None);
        mock.Received().StringBitOperation(Bitwise.Xor, "prefix:destination", IsKeys("prefix:a", "prefix:b"), CommandFlags.None);
    }

    [Fact]
    public void StringBitOperation_Diff()
    {
        RedisKey[] keys = ["x", "y1", "y2"];
        prefixed.StringBitOperation(Bitwise.Diff, "destination", keys, CommandFlags.None);
        mock.Received().StringBitOperation(Bitwise.Diff, "prefix:destination", IsKeys("prefix:x", "prefix:y1", "prefix:y2"), CommandFlags.None);
    }

    [Fact]
    public void StringBitOperation_Diff1()
    {
        RedisKey[] keys = ["x", "y1", "y2"];
        prefixed.StringBitOperation(Bitwise.Diff1, "destination", keys, CommandFlags.None);
        mock.Received().StringBitOperation(Bitwise.Diff1, "prefix:destination", IsKeys("prefix:x", "prefix:y1", "prefix:y2"), CommandFlags.None);
    }

    [Fact]
    public void StringBitOperation_AndOr()
    {
        RedisKey[] keys = ["x", "y1", "y2"];
        prefixed.StringBitOperation(Bitwise.AndOr, "destination", keys, CommandFlags.None);
        mock.Received().StringBitOperation(Bitwise.AndOr, "prefix:destination", IsKeys("prefix:x", "prefix:y1", "prefix:y2"), CommandFlags.None);
    }

    [Fact]
    public void StringBitOperation_One()
    {
        RedisKey[] keys = ["a", "b", "c"];
        prefixed.StringBitOperation(Bitwise.One, "destination", keys, CommandFlags.None);
        mock.Received().StringBitOperation(Bitwise.One, "prefix:destination", IsKeys("prefix:a", "prefix:b", "prefix:c"), CommandFlags.None);
    }

    [Fact]
    public void StringBitPosition()
    {
        prefixed.StringBitPosition("key", true, 123, 456, CommandFlags.None);
        mock.Received().StringBitPosition("prefix:key", true, 123, 456, CommandFlags.None);
    }

    [Fact]
    public void StringBitPosition_2()
    {
        prefixed.StringBitPosition("key", true, 123, 456, StringIndexType.Byte, CommandFlags.None);
        mock.Received().StringBitPosition("prefix:key", true, 123, 456, StringIndexType.Byte, CommandFlags.None);
    }

    [Fact]
    public void StringDecrement_1()
    {
        prefixed.StringDecrement("key", 123, CommandFlags.None);
        mock.Received().StringDecrement("prefix:key", 123, CommandFlags.None);
    }

    [Fact]
    public void StringDecrement_2()
    {
        prefixed.StringDecrement("key", 1.23, CommandFlags.None);
        mock.Received().StringDecrement("prefix:key", 1.23, CommandFlags.None);
    }

    [Fact]
    public void StringGet_1()
    {
        prefixed.StringGet("key", CommandFlags.None);
        mock.Received().StringGet("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void StringGet_2()
    {
        RedisKey[] keys = ["a", "b"];
        prefixed.StringGet(keys, CommandFlags.None);
        mock.Received().StringGet(IsKeys("prefix:a", "prefix:b"), CommandFlags.None);
    }

    [Fact]
    public void StringGetBit()
    {
        prefixed.StringGetBit("key", 123, CommandFlags.None);
        mock.Received().StringGetBit("prefix:key", 123, CommandFlags.None);
    }

    [Fact]
    public void StringGetRange()
    {
        prefixed.StringGetRange("key", 123, 456, CommandFlags.None);
        mock.Received().StringGetRange("prefix:key", 123, 456, CommandFlags.None);
    }

    [Fact]
    public void StringGetSet()
    {
        prefixed.StringGetSet("key", "value", CommandFlags.None);
        mock.Received().StringGetSet("prefix:key", "value", CommandFlags.None);
    }

    [Fact]
    public void StringGetDelete()
    {
        prefixed.StringGetDelete("key", CommandFlags.None);
        mock.Received().StringGetDelete("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void StringGetWithExpiry()
    {
        prefixed.StringGetWithExpiry("key", CommandFlags.None);
        mock.Received().StringGetWithExpiry("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void StringIncrement_1()
    {
        prefixed.StringIncrement("key", 123, CommandFlags.None);
        mock.Received().StringIncrement("prefix:key", 123, CommandFlags.None);
    }

    [Fact]
    public void StringIncrement_2()
    {
        prefixed.StringIncrement("key", 1.23, CommandFlags.None);
        mock.Received().StringIncrement("prefix:key", 1.23, CommandFlags.None);
    }

    [Fact]
    public void StringLength()
    {
        prefixed.StringLength("key", CommandFlags.None);
        mock.Received().StringLength("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void StringSet_1()
    {
        TimeSpan expiry = TimeSpan.FromSeconds(123);
        prefixed.StringSet("key", "value", expiry, When.Exists, CommandFlags.None);
        mock.Received().StringSet("prefix:key", "value", expiry, When.Exists, CommandFlags.None);
    }

    [Fact]
    public void StringSet_2()
    {
        TimeSpan? expiry = null;
        prefixed.StringSet("key", "value", expiry, true, When.Exists, CommandFlags.None);
        mock.Received().StringSet("prefix:key", "value", expiry, true, When.Exists, CommandFlags.None);
    }

    [Fact]
    public void StringSet_3()
    {
        KeyValuePair<RedisKey, RedisValue>[] values = [new KeyValuePair<RedisKey, RedisValue>("a", "x"), new KeyValuePair<RedisKey, RedisValue>("b", "y")];
        Expression<Predicate<KeyValuePair<RedisKey, RedisValue>[]>> valid = _ => _.Length == 2 && _[0].Key == "prefix:a" && _[0].Value == "x" && _[1].Key == "prefix:b" && _[1].Value == "y";
        prefixed.StringSet(values, When.Exists, CommandFlags.None);
        mock.Received().StringSet(Arg.Is(valid), When.Exists, CommandFlags.None);
    }

    [Fact]
    public void StringSet_Compat()
    {
        TimeSpan? expiry = null;
        prefixed.StringSet("key", "value", expiry, When.Exists);
        mock.Received().StringSet("prefix:key", "value", expiry, When.Exists);
    }

    [Fact]
    public void StringSetBit()
    {
        prefixed.StringSetBit("key", 123, true, CommandFlags.None);
        mock.Received().StringSetBit("prefix:key", 123, true, CommandFlags.None);
    }

    [Fact]
    public void StringSetRange()
    {
        prefixed.StringSetRange("key", 123, "value", CommandFlags.None);
        mock.Received().StringSetRange("prefix:key", 123, "value", CommandFlags.None);
    }

    [Fact]
    public void Execute_1()
    {
        prefixed.Execute("CUSTOM", "arg1", (RedisKey)"arg2");
        mock.Received().Execute("CUSTOM", Arg.Is<object[]>(args => args.Length == 2 && args[0].Equals("arg1") && args[1].Equals((RedisKey)"prefix:arg2")), CommandFlags.None);
    }

    [Fact]
    public void Execute_2()
    {
        var args = new List<object> { "arg1", (RedisKey)"arg2" };
        prefixed.Execute("CUSTOM", args, CommandFlags.None);
        mock.Received().Execute("CUSTOM", Arg.Is<ICollection<object>>(a => a.Count == 2 && a.ElementAt(0).Equals("arg1") && a.ElementAt(1).Equals((RedisKey)"prefix:arg2"))!, CommandFlags.None);
    }

    [Fact]
    public void GeoAdd_1()
    {
        prefixed.GeoAdd("key", 1.23, 4.56, "member", CommandFlags.None);
        mock.Received().GeoAdd("prefix:key", 1.23, 4.56, "member", CommandFlags.None);
    }

    [Fact]
    public void GeoAdd_2()
    {
        var geoEntry = new GeoEntry(1.23, 4.56, "member");
        prefixed.GeoAdd("key", geoEntry, CommandFlags.None);
        mock.Received().GeoAdd("prefix:key", geoEntry, CommandFlags.None);
    }

    [Fact]
    public void GeoAdd_3()
    {
        var geoEntries = new GeoEntry[] { new GeoEntry(1.23, 4.56, "member1") };
        prefixed.GeoAdd("key", geoEntries, CommandFlags.None);
        mock.Received().GeoAdd("prefix:key", geoEntries, CommandFlags.None);
    }

    [Fact]
    public void GeoRemove()
    {
        prefixed.GeoRemove("key", "member", CommandFlags.None);
        mock.Received().GeoRemove("prefix:key", "member", CommandFlags.None);
    }

    [Fact]
    public void GeoDistance()
    {
        prefixed.GeoDistance("key", "member1", "member2", GeoUnit.Meters, CommandFlags.None);
        mock.Received().GeoDistance("prefix:key", "member1", "member2", GeoUnit.Meters, CommandFlags.None);
    }

    [Fact]
    public void GeoHash_1()
    {
        prefixed.GeoHash("key", "member", CommandFlags.None);
        mock.Received().GeoHash("prefix:key", "member", CommandFlags.None);
    }

    [Fact]
    public void GeoHash_2()
    {
        var members = new RedisValue[] { "member1", "member2" };
        prefixed.GeoHash("key", members, CommandFlags.None);
        mock.Received().GeoHash("prefix:key", members, CommandFlags.None);
    }

    [Fact]
    public void GeoPosition_1()
    {
        prefixed.GeoPosition("key", "member", CommandFlags.None);
        mock.Received().GeoPosition("prefix:key", "member", CommandFlags.None);
    }

    [Fact]
    public void GeoPosition_2()
    {
        var members = new RedisValue[] { "member1", "member2" };
        prefixed.GeoPosition("key", members, CommandFlags.None);
        mock.Received().GeoPosition("prefix:key", members, CommandFlags.None);
    }

    [Fact]
    public void GeoRadius_1()
    {
        prefixed.GeoRadius("key", "member", 100, GeoUnit.Meters, 10, Order.Ascending, GeoRadiusOptions.Default, CommandFlags.None);
        mock.Received().GeoRadius("prefix:key", "member", 100, GeoUnit.Meters, 10, Order.Ascending, GeoRadiusOptions.Default, CommandFlags.None);
    }

    [Fact]
    public void GeoRadius_2()
    {
        prefixed.GeoRadius("key", 1.23, 4.56, 100, GeoUnit.Meters, 10, Order.Ascending, GeoRadiusOptions.Default, CommandFlags.None);
        mock.Received().GeoRadius("prefix:key", 1.23, 4.56, 100, GeoUnit.Meters, 10, Order.Ascending, GeoRadiusOptions.Default, CommandFlags.None);
    }

    [Fact]
    public void GeoSearch_1()
    {
        var shape = new GeoSearchCircle(100, GeoUnit.Meters);
        prefixed.GeoSearch("key", "member", shape, 10, true, Order.Ascending, GeoRadiusOptions.Default, CommandFlags.None);
        mock.Received().GeoSearch("prefix:key", "member", shape, 10, true, Order.Ascending, GeoRadiusOptions.Default, CommandFlags.None);
    }

    [Fact]
    public void GeoSearch_2()
    {
        var shape = new GeoSearchCircle(100, GeoUnit.Meters);
        prefixed.GeoSearch("key", 1.23, 4.56, shape, 10, true, Order.Ascending, GeoRadiusOptions.Default, CommandFlags.None);
        mock.Received().GeoSearch("prefix:key", 1.23, 4.56, shape, 10, true, Order.Ascending, GeoRadiusOptions.Default, CommandFlags.None);
    }

    [Fact]
    public void GeoSearchAndStore_1()
    {
        var shape = new GeoSearchCircle(100, GeoUnit.Meters);
        prefixed.GeoSearchAndStore("source", "destination", "member", shape, 10, true, Order.Ascending, false, CommandFlags.None);
        mock.Received().GeoSearchAndStore("prefix:source", "prefix:destination", "member", shape, 10, true, Order.Ascending, false, CommandFlags.None);
    }

    [Fact]
    public void GeoSearchAndStore_2()
    {
        var shape = new GeoSearchCircle(100, GeoUnit.Meters);
        prefixed.GeoSearchAndStore("source", "destination", 1.23, 4.56, shape, 10, true, Order.Ascending, false, CommandFlags.None);
        mock.Received().GeoSearchAndStore("prefix:source", "prefix:destination", 1.23, 4.56, shape, 10, true, Order.Ascending, false, CommandFlags.None);
    }

    [Fact]
    public void HashFieldExpire_1()
    {
        var hashFields = new RedisValue[] { "field1", "field2" };
        var expiry = TimeSpan.FromSeconds(60);
        prefixed.HashFieldExpire("key", hashFields, expiry, ExpireWhen.Always, CommandFlags.None);
        mock.Received().HashFieldExpire("prefix:key", hashFields, expiry, ExpireWhen.Always, CommandFlags.None);
    }

    [Fact]
    public void HashFieldExpire_2()
    {
        var hashFields = new RedisValue[] { "field1", "field2" };
        var expiry = DateTime.Now.AddMinutes(1);
        prefixed.HashFieldExpire("key", hashFields, expiry, ExpireWhen.Always, CommandFlags.None);
        mock.Received().HashFieldExpire("prefix:key", hashFields, expiry, ExpireWhen.Always, CommandFlags.None);
    }

    [Fact]
    public void HashFieldGetExpireDateTime()
    {
        var hashFields = new RedisValue[] { "field1", "field2" };
        prefixed.HashFieldGetExpireDateTime("key", hashFields, CommandFlags.None);
        mock.Received().HashFieldGetExpireDateTime("prefix:key", hashFields, CommandFlags.None);
    }

    [Fact]
    public void HashFieldPersist()
    {
        var hashFields = new RedisValue[] { "field1", "field2" };
        prefixed.HashFieldPersist("key", hashFields, CommandFlags.None);
        mock.Received().HashFieldPersist("prefix:key", hashFields, CommandFlags.None);
    }

    [Fact]
    public void HashFieldGetTimeToLive()
    {
        var hashFields = new RedisValue[] { "field1", "field2" };
        prefixed.HashFieldGetTimeToLive("key", hashFields, CommandFlags.None);
        mock.Received().HashFieldGetTimeToLive("prefix:key", hashFields, CommandFlags.None);
    }

    [Fact]
    public void HashGetLease()
    {
        prefixed.HashGetLease("key", "field", CommandFlags.None);
        mock.Received().HashGetLease("prefix:key", "field", CommandFlags.None);
    }

    [Fact]
    public void HashFieldGetAndDelete_1()
    {
        prefixed.HashFieldGetAndDelete("key", "field", CommandFlags.None);
        mock.Received().HashFieldGetAndDelete("prefix:key", "field", CommandFlags.None);
    }

    [Fact]
    public void HashFieldGetAndDelete_2()
    {
        var hashFields = new RedisValue[] { "field1", "field2" };
        prefixed.HashFieldGetAndDelete("key", hashFields, CommandFlags.None);
        mock.Received().HashFieldGetAndDelete("prefix:key", hashFields, CommandFlags.None);
    }

    [Fact]
    public void HashFieldGetLeaseAndDelete()
    {
        prefixed.HashFieldGetLeaseAndDelete("key", "field", CommandFlags.None);
        mock.Received().HashFieldGetLeaseAndDelete("prefix:key", "field", CommandFlags.None);
    }

    [Fact]
    public void HashFieldGetAndSetExpiry_1()
    {
        var expiry = TimeSpan.FromMinutes(5);
        prefixed.HashFieldGetAndSetExpiry("key", "field", expiry, false, CommandFlags.None);
        mock.Received().HashFieldGetAndSetExpiry("prefix:key", "field", expiry, false, CommandFlags.None);
    }

    [Fact]
    public void HashFieldGetAndSetExpiry_2()
    {
        var expiry = DateTime.Now.AddMinutes(5);
        prefixed.HashFieldGetAndSetExpiry("key", "field", expiry, CommandFlags.None);
        mock.Received().HashFieldGetAndSetExpiry("prefix:key", "field", expiry, CommandFlags.None);
    }

    [Fact]
    public void HashFieldGetLeaseAndSetExpiry_1()
    {
        var expiry = TimeSpan.FromMinutes(5);
        prefixed.HashFieldGetLeaseAndSetExpiry("key", "field", expiry, false, CommandFlags.None);
        mock.Received().HashFieldGetLeaseAndSetExpiry("prefix:key", "field", expiry, false, CommandFlags.None);
    }

    [Fact]
    public void HashFieldGetLeaseAndSetExpiry_2()
    {
        var expiry = DateTime.Now.AddMinutes(5);
        prefixed.HashFieldGetLeaseAndSetExpiry("key", "field", expiry, CommandFlags.None);
        mock.Received().HashFieldGetLeaseAndSetExpiry("prefix:key", "field", expiry, CommandFlags.None);
    }
    [Fact]
    public void StringGetLease()
    {
        prefixed.StringGetLease("key", CommandFlags.None);
        mock.Received().StringGetLease("prefix:key", CommandFlags.None);
    }

    [Fact]
    public void StringGetSetExpiry_1()
    {
        var expiry = TimeSpan.FromMinutes(5);
        prefixed.StringGetSetExpiry("key", expiry, CommandFlags.None);
        mock.Received().StringGetSetExpiry("prefix:key", expiry, CommandFlags.None);
    }

    [Fact]
    public void StringGetSetExpiry_2()
    {
        var expiry = DateTime.Now.AddMinutes(5);
        prefixed.StringGetSetExpiry("key", expiry, CommandFlags.None);
        mock.Received().StringGetSetExpiry("prefix:key", expiry, CommandFlags.None);
    }

    [Fact]
    public void StringSetAndGet_1()
    {
        var expiry = TimeSpan.FromMinutes(5);
        prefixed.StringSetAndGet("key", "value", expiry, When.Always, CommandFlags.None);
        mock.Received().StringSetAndGet("prefix:key", "value", expiry, When.Always, CommandFlags.None);
    }

    [Fact]
    public void StringSetAndGet_2()
    {
        var expiry = TimeSpan.FromMinutes(5);
        prefixed.StringSetAndGet("key", "value", expiry, false, When.Always, CommandFlags.None);
        mock.Received().StringSetAndGet("prefix:key", "value", expiry, false, When.Always, CommandFlags.None);
    }
    [Fact]
    public void StringLongestCommonSubsequence()
    {
        prefixed.StringLongestCommonSubsequence("key1", "key2", CommandFlags.None);
        mock.Received().StringLongestCommonSubsequence("prefix:key1", "prefix:key2", CommandFlags.None);
    }

    [Fact]
    public void StringLongestCommonSubsequenceLength()
    {
        prefixed.StringLongestCommonSubsequenceLength("key1", "key2", CommandFlags.None);
        mock.Received().StringLongestCommonSubsequenceLength("prefix:key1", "prefix:key2", CommandFlags.None);
    }

    [Fact]
    public void StringLongestCommonSubsequenceWithMatches()
    {
        prefixed.StringLongestCommonSubsequenceWithMatches("key1", "key2", 5, CommandFlags.None);
        mock.Received().StringLongestCommonSubsequenceWithMatches("prefix:key1", "prefix:key2", 5, CommandFlags.None);
    }
    [Fact]
    public void IsConnected()
    {
        prefixed.IsConnected("key", CommandFlags.None);
        mock.Received().IsConnected("prefix:key", CommandFlags.None);
    }
    [Fact]
    public void StreamAdd_WithTrimMode_1()
    {
        prefixed.StreamAdd("key", "field", "value", "*", 1000, false, 100, StreamTrimMode.KeepReferences, CommandFlags.None);
        mock.Received().StreamAdd("prefix:key", "field", "value", "*", 1000, false, 100, StreamTrimMode.KeepReferences, CommandFlags.None);
    }

    [Fact]
    public void StreamAdd_WithTrimMode_2()
    {
        var fields = new NameValueEntry[] { new NameValueEntry("field", "value") };
        prefixed.StreamAdd("key", fields, "*", 1000, false, 100, StreamTrimMode.KeepReferences, CommandFlags.None);
        mock.Received().StreamAdd("prefix:key", fields, "*", 1000, false, 100, StreamTrimMode.KeepReferences, CommandFlags.None);
    }

    [Fact]
    public void StreamTrim_WithMode()
    {
        prefixed.StreamTrim("key", 1000, false, 100, StreamTrimMode.KeepReferences, CommandFlags.None);
        mock.Received().StreamTrim("prefix:key", 1000, false, 100, StreamTrimMode.KeepReferences, CommandFlags.None);
    }

    [Fact]
    public void StreamTrimByMinId_WithMode()
    {
        prefixed.StreamTrimByMinId("key", "1111111111", false, 100, StreamTrimMode.KeepReferences, CommandFlags.None);
        mock.Received().StreamTrimByMinId("prefix:key", "1111111111", false, 100, StreamTrimMode.KeepReferences, CommandFlags.None);
    }

    [Fact]
    public void StreamReadGroup_WithNoAck_1()
    {
        prefixed.StreamReadGroup("key", "group", "consumer", "0-0", 10, true, CommandFlags.None);
        mock.Received().StreamReadGroup("prefix:key", "group", "consumer", "0-0", 10, true, CommandFlags.None);
    }

    [Fact]
    public void StreamReadGroup_WithNoAck_2()
    {
        var streamPositions = new StreamPosition[] { new StreamPosition("key", "0-0") };
        prefixed.StreamReadGroup(streamPositions, "group", "consumer", 10, true, CommandFlags.None);
        mock.Received().StreamReadGroup(streamPositions, "group", "consumer", 10, true, CommandFlags.None);
    }

    [Fact]
    public void StreamTrim_Simple()
    {
        prefixed.StreamTrim("key", 1000, true, CommandFlags.None);
        mock.Received().StreamTrim("prefix:key", 1000, true, CommandFlags.None);
    }

    [Fact]
    public void StreamReadGroup_Simple_1()
    {
        prefixed.StreamReadGroup("key", "group", "consumer", "0-0", 10, CommandFlags.None);
        mock.Received().StreamReadGroup("prefix:key", "group", "consumer", "0-0", 10, CommandFlags.None);
    }

    [Fact]
    public void StreamReadGroup_Simple_2()
    {
        var streamPositions = new StreamPosition[] { new StreamPosition("key", "0-0") };
        prefixed.StreamReadGroup(streamPositions, "group", "consumer", 10, CommandFlags.None);
        mock.Received().StreamReadGroup(streamPositions, "group", "consumer", 10, CommandFlags.None);
    }
}
