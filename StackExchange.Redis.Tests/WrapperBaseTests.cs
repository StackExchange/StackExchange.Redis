#if FEATURE_MOQ
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net;
using System.Text;
using Moq;
using NUnit.Framework;
using StackExchange.Redis.KeyspaceIsolation;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public sealed class WrapperBaseTests
    {
        private Mock<IDatabaseAsync> mock;
        private WrapperBase<IDatabaseAsync> wrapper;

        [OneTimeSetUp]
        public void Initialize()
        {
            mock = new Mock<IDatabaseAsync>();
            wrapper = new WrapperBase<IDatabaseAsync>(mock.Object, Encoding.UTF8.GetBytes("prefix:"));
        }

        [Test]
        public void DebugObjectAsync()
        {
            wrapper.DebugObjectAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.DebugObjectAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void HashDecrementAsync_1()
        {
            wrapper.HashDecrementAsync("key", "hashField", 123, CommandFlags.HighPriority);
            mock.Verify(_ => _.HashDecrementAsync("prefix:key", "hashField", 123, CommandFlags.HighPriority));
        }

        [Test]
        public void HashDecrementAsync_2()
        {
            wrapper.HashDecrementAsync("key", "hashField", 1.23, CommandFlags.HighPriority);
            mock.Verify(_ => _.HashDecrementAsync("prefix:key", "hashField", 1.23, CommandFlags.HighPriority));
        }

        [Test]
        public void HashDeleteAsync_1()
        {
            wrapper.HashDeleteAsync("key", "hashField", CommandFlags.HighPriority);
            mock.Verify(_ => _.HashDeleteAsync("prefix:key", "hashField", CommandFlags.HighPriority));
        }

        [Test]
        public void HashDeleteAsync_2()
        {
            RedisValue[] hashFields = new RedisValue[0];
            wrapper.HashDeleteAsync("key", hashFields, CommandFlags.HighPriority);
            mock.Verify(_ => _.HashDeleteAsync("prefix:key", hashFields, CommandFlags.HighPriority));
        }

        [Test]
        public void HashExistsAsync()
        {
            wrapper.HashExistsAsync("key", "hashField", CommandFlags.HighPriority);
            mock.Verify(_ => _.HashExistsAsync("prefix:key", "hashField", CommandFlags.HighPriority));
        }

        [Test]
        public void HashGetAllAsync()
        {
            wrapper.HashGetAllAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.HashGetAllAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void HashGetAsync_1()
        {
            wrapper.HashGetAsync("key", "hashField", CommandFlags.HighPriority);
            mock.Verify(_ => _.HashGetAsync("prefix:key", "hashField", CommandFlags.HighPriority));
        }

        [Test]
        public void HashGetAsync_2()
        {
            RedisValue[] hashFields = new RedisValue[0];
            wrapper.HashGetAsync("key", hashFields, CommandFlags.HighPriority);
            mock.Verify(_ => _.HashGetAsync("prefix:key", hashFields, CommandFlags.HighPriority));
        }

        [Test]
        public void HashIncrementAsync_1()
        {
            wrapper.HashIncrementAsync("key", "hashField", 123, CommandFlags.HighPriority);
            mock.Verify(_ => _.HashIncrementAsync("prefix:key", "hashField", 123, CommandFlags.HighPriority));
        }

        [Test]
        public void HashIncrementAsync_2()
        {
            wrapper.HashIncrementAsync("key", "hashField", 1.23, CommandFlags.HighPriority);
            mock.Verify(_ => _.HashIncrementAsync("prefix:key", "hashField", 1.23, CommandFlags.HighPriority));
        }

        [Test]
        public void HashKeysAsync()
        {
            wrapper.HashKeysAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.HashKeysAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void HashLengthAsync()
        {
            wrapper.HashLengthAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.HashLengthAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void HashSetAsync_1()
        {
            HashEntry[] hashFields = new HashEntry[0];
            wrapper.HashSetAsync("key", hashFields, CommandFlags.HighPriority);
            mock.Verify(_ => _.HashSetAsync("prefix:key", hashFields, CommandFlags.HighPriority));
        }

        [Test]
        public void HashSetAsync_2()
        {
            wrapper.HashSetAsync("key", "hashField", "value", When.Exists, CommandFlags.HighPriority);
            mock.Verify(_ => _.HashSetAsync("prefix:key", "hashField", "value", When.Exists, CommandFlags.HighPriority));
        }

        [Test]
        public void HashValuesAsync()
        {
            wrapper.HashValuesAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.HashValuesAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void HyperLogLogAddAsync_1()
        {
            wrapper.HyperLogLogAddAsync("key", "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.HyperLogLogAddAsync("prefix:key", "value", CommandFlags.HighPriority));
        }

        [Test]
        public void HyperLogLogAddAsync_2()
        {
            RedisValue[] values = new RedisValue[0];
            wrapper.HyperLogLogAddAsync("key", values, CommandFlags.HighPriority);
            mock.Verify(_ => _.HyperLogLogAddAsync("prefix:key", values, CommandFlags.HighPriority));
        }

        [Test]
        public void HyperLogLogLengthAsync()
        {
            wrapper.HyperLogLogLengthAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.HyperLogLogLengthAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void HyperLogLogMergeAsync_1()
        {
            wrapper.HyperLogLogMergeAsync("destination", "first", "second", CommandFlags.HighPriority);
            mock.Verify(_ => _.HyperLogLogMergeAsync("prefix:destination", "prefix:first", "prefix:second", CommandFlags.HighPriority));
        }

        [Test]
        public void HyperLogLogMergeAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.HyperLogLogMergeAsync("destination", keys, CommandFlags.HighPriority);
            mock.Verify(_ => _.HyperLogLogMergeAsync("prefix:destination", It.Is(valid), CommandFlags.HighPriority));
        }

        [Test]
        public void IdentifyEndpointAsync()
        {
            wrapper.IdentifyEndpointAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.IdentifyEndpointAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void IsConnected()
        {
            wrapper.IsConnected("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.IsConnected("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void KeyDeleteAsync_1()
        {
            wrapper.KeyDeleteAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyDeleteAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void KeyDeleteAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.KeyDeleteAsync(keys, CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyDeleteAsync(It.Is(valid), CommandFlags.HighPriority));
        }

        [Test]
        public void KeyDumpAsync()
        {
            wrapper.KeyDumpAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyDumpAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void KeyExistsAsync()
        {
            wrapper.KeyExistsAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyExistsAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void KeyExpireAsync_1()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            wrapper.KeyExpireAsync("key", expiry, CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyExpireAsync("prefix:key", expiry, CommandFlags.HighPriority));
        }

        [Test]
        public void KeyExpireAsync_2()
        {
            DateTime expiry = DateTime.Now;
            wrapper.KeyExpireAsync("key", expiry, CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyExpireAsync("prefix:key", expiry, CommandFlags.HighPriority));
        }

        [Test]
        public void KeyMigrateAsync()
        {
            EndPoint toServer = new IPEndPoint(IPAddress.Loopback, 123);
            wrapper.KeyMigrateAsync("key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyMigrateAsync("prefix:key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.HighPriority));
        }

        [Test]
        public void KeyMoveAsync()
        {
            wrapper.KeyMoveAsync("key", 123, CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyMoveAsync("prefix:key", 123, CommandFlags.HighPriority));
        }

        [Test]
        public void KeyPersistAsync()
        {
            wrapper.KeyPersistAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyPersistAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void KeyRandomAsync()
        {
            Assert.Throws<NotSupportedException>(() => {
                wrapper.KeyRandomAsync();
            });
        }

        [Test]
        public void KeyRenameAsync()
        {
            wrapper.KeyRenameAsync("key", "newKey", When.Exists, CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyRenameAsync("prefix:key", "prefix:newKey", When.Exists, CommandFlags.HighPriority));
        }

        [Test]
        public void KeyRestoreAsync()
        {
            Byte[] value = new Byte[0];
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            wrapper.KeyRestoreAsync("key", value, expiry, CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyRestoreAsync("prefix:key", value, expiry, CommandFlags.HighPriority));
        }

        [Test]
        public void KeyTimeToLiveAsync()
        {
            wrapper.KeyTimeToLiveAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyTimeToLiveAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void KeyTypeAsync()
        {
            wrapper.KeyTypeAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.KeyTypeAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void ListGetByIndexAsync()
        {
            wrapper.ListGetByIndexAsync("key", 123, CommandFlags.HighPriority);
            mock.Verify(_ => _.ListGetByIndexAsync("prefix:key", 123, CommandFlags.HighPriority));
        }

        [Test]
        public void ListInsertAfterAsync()
        {
            wrapper.ListInsertAfterAsync("key", "pivot", "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.ListInsertAfterAsync("prefix:key", "pivot", "value", CommandFlags.HighPriority));
        }

        [Test]
        public void ListInsertBeforeAsync()
        {
            wrapper.ListInsertBeforeAsync("key", "pivot", "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.ListInsertBeforeAsync("prefix:key", "pivot", "value", CommandFlags.HighPriority));
        }

        [Test]
        public void ListLeftPopAsync()
        {
            wrapper.ListLeftPopAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.ListLeftPopAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void ListLeftPushAsync_1()
        {
            wrapper.ListLeftPushAsync("key", "value", When.Exists, CommandFlags.HighPriority);
            mock.Verify(_ => _.ListLeftPushAsync("prefix:key", "value", When.Exists, CommandFlags.HighPriority));
        }

        [Test]
        public void ListLeftPushAsync_2()
        {
            RedisValue[] values = new RedisValue[0];
            wrapper.ListLeftPushAsync("key", values, CommandFlags.HighPriority);
            mock.Verify(_ => _.ListLeftPushAsync("prefix:key", values, CommandFlags.HighPriority));
        }

        [Test]
        public void ListLengthAsync()
        {
            wrapper.ListLengthAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.ListLengthAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void ListRangeAsync()
        {
            wrapper.ListRangeAsync("key", 123, 456, CommandFlags.HighPriority);
            mock.Verify(_ => _.ListRangeAsync("prefix:key", 123, 456, CommandFlags.HighPriority));
        }

        [Test]
        public void ListRemoveAsync()
        {
            wrapper.ListRemoveAsync("key", "value", 123, CommandFlags.HighPriority);
            mock.Verify(_ => _.ListRemoveAsync("prefix:key", "value", 123, CommandFlags.HighPriority));
        }

        [Test]
        public void ListRightPopAsync()
        {
            wrapper.ListRightPopAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.ListRightPopAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void ListRightPopLeftPushAsync()
        {
            wrapper.ListRightPopLeftPushAsync("source", "destination", CommandFlags.HighPriority);
            mock.Verify(_ => _.ListRightPopLeftPushAsync("prefix:source", "prefix:destination", CommandFlags.HighPriority));
        }

        [Test]
        public void ListRightPushAsync_1()
        {
            wrapper.ListRightPushAsync("key", "value", When.Exists, CommandFlags.HighPriority);
            mock.Verify(_ => _.ListRightPushAsync("prefix:key", "value", When.Exists, CommandFlags.HighPriority));
        }

        [Test]
        public void ListRightPushAsync_2()
        {
            RedisValue[] values = new RedisValue[0];
            wrapper.ListRightPushAsync("key", values, CommandFlags.HighPriority);
            mock.Verify(_ => _.ListRightPushAsync("prefix:key", values, CommandFlags.HighPriority));
        }

        [Test]
        public void ListSetByIndexAsync()
        {
            wrapper.ListSetByIndexAsync("key", 123, "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.ListSetByIndexAsync("prefix:key", 123, "value", CommandFlags.HighPriority));
        }

        [Test]
        public void ListTrimAsync()
        {
            wrapper.ListTrimAsync("key", 123, 456, CommandFlags.HighPriority);
            mock.Verify(_ => _.ListTrimAsync("prefix:key", 123, 456, CommandFlags.HighPriority));
        }

        [Test]
        public void LockExtendAsync()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            wrapper.LockExtendAsync("key", "value", expiry, CommandFlags.HighPriority);
            mock.Verify(_ => _.LockExtendAsync("prefix:key", "value", expiry, CommandFlags.HighPriority));
        }

        [Test]
        public void LockQueryAsync()
        {
            wrapper.LockQueryAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.LockQueryAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void LockReleaseAsync()
        {
            wrapper.LockReleaseAsync("key", "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.LockReleaseAsync("prefix:key", "value", CommandFlags.HighPriority));
        }

        [Test]
        public void LockTakeAsync()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            wrapper.LockTakeAsync("key", "value", expiry, CommandFlags.HighPriority);
            mock.Verify(_ => _.LockTakeAsync("prefix:key", "value", expiry, CommandFlags.HighPriority));
        }

        [Test]
        public void PublishAsync()
        {
            wrapper.PublishAsync("channel", "message", CommandFlags.HighPriority);
            mock.Verify(_ => _.PublishAsync("prefix:channel", "message", CommandFlags.HighPriority));
        }

        [Test]
        public void ScriptEvaluateAsync_1()
        {
            byte[] hash = new byte[0];
            RedisValue[] values = new RedisValue[0];
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.ScriptEvaluateAsync(hash, keys, values, CommandFlags.HighPriority);
            mock.Verify(_ => _.ScriptEvaluateAsync(hash, It.Is(valid), values, CommandFlags.HighPriority));
        }

        [Test]
        public void ScriptEvaluateAsync_2()
        {
            RedisValue[] values = new RedisValue[0];
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.ScriptEvaluateAsync("script", keys, values, CommandFlags.HighPriority);
            mock.Verify(_ => _.ScriptEvaluateAsync("script", It.Is(valid), values, CommandFlags.HighPriority));
        }

        [Test]
        public void SetAddAsync_1()
        {
            wrapper.SetAddAsync("key", "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.SetAddAsync("prefix:key", "value", CommandFlags.HighPriority));
        }

        [Test]
        public void SetAddAsync_2()
        {
            RedisValue[] values = new RedisValue[0];
            wrapper.SetAddAsync("key", values, CommandFlags.HighPriority);
            mock.Verify(_ => _.SetAddAsync("prefix:key", values, CommandFlags.HighPriority));
        }

        [Test]
        public void SetCombineAndStoreAsync_1()
        {
            wrapper.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", "first", "second", CommandFlags.HighPriority);
            mock.Verify(_ => _.SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.HighPriority));
        }

        [Test]
        public void SetCombineAndStoreAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", keys, CommandFlags.HighPriority);
            mock.Verify(_ => _.SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", It.Is(valid), CommandFlags.HighPriority));
        }

        [Test]
        public void SetCombineAsync_1()
        {
            wrapper.SetCombineAsync(SetOperation.Intersect, "first", "second", CommandFlags.HighPriority);
            mock.Verify(_ => _.SetCombineAsync(SetOperation.Intersect, "prefix:first", "prefix:second", CommandFlags.HighPriority));
        }

        [Test]
        public void SetCombineAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.SetCombineAsync(SetOperation.Intersect, keys, CommandFlags.HighPriority);
            mock.Verify(_ => _.SetCombineAsync(SetOperation.Intersect, It.Is(valid), CommandFlags.HighPriority));
        }

        [Test]
        public void SetContainsAsync()
        {
            wrapper.SetContainsAsync("key", "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.SetContainsAsync("prefix:key", "value", CommandFlags.HighPriority));
        }

        [Test]
        public void SetLengthAsync()
        {
            wrapper.SetLengthAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.SetLengthAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void SetMembersAsync()
        {
            wrapper.SetMembersAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.SetMembersAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void SetMoveAsync()
        {
            wrapper.SetMoveAsync("source", "destination", "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.SetMoveAsync("prefix:source", "prefix:destination", "value", CommandFlags.HighPriority));
        }

        [Test]
        public void SetPopAsync()
        {
            wrapper.SetPopAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.SetPopAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void SetRandomMemberAsync()
        {
            wrapper.SetRandomMemberAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.SetRandomMemberAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void SetRandomMembersAsync()
        {
            wrapper.SetRandomMembersAsync("key", 123, CommandFlags.HighPriority);
            mock.Verify(_ => _.SetRandomMembersAsync("prefix:key", 123, CommandFlags.HighPriority));
        }

        [Test]
        public void SetRemoveAsync_1()
        {
            wrapper.SetRemoveAsync("key", "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.SetRemoveAsync("prefix:key", "value", CommandFlags.HighPriority));
        }

        [Test]
        public void SetRemoveAsync_2()
        {
            RedisValue[] values = new RedisValue[0];
            wrapper.SetRemoveAsync("key", values, CommandFlags.HighPriority);
            mock.Verify(_ => _.SetRemoveAsync("prefix:key", values, CommandFlags.HighPriority));
        }

        [Test]
        public void SortAndStoreAsync()
        {
            RedisValue[] get = new RedisValue[] { "a", "#" };
            Expression<Func<RedisValue[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "#";

            wrapper.SortAndStoreAsync("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.HighPriority);
            wrapper.SortAndStoreAsync("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.HighPriority);

            mock.Verify(_ => _.SortAndStoreAsync("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", It.Is(valid), CommandFlags.HighPriority));
            mock.Verify(_ => _.SortAndStoreAsync("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", It.Is(valid), CommandFlags.HighPriority));
        }

        [Test]
        public void SortAsync()
        {
            RedisValue[] get = new RedisValue[] { "a", "#" };
            Expression<Func<RedisValue[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "#";

            wrapper.SortAsync("key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.HighPriority);
            wrapper.SortAsync("key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.HighPriority);

            mock.Verify(_ => _.SortAsync("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", It.Is(valid), CommandFlags.HighPriority));
            mock.Verify(_ => _.SortAsync("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", It.Is(valid), CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetAddAsync_1()
        {
            wrapper.SortedSetAddAsync("key", "member", 1.23, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetAddAsync("prefix:key", "member", 1.23, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetAddAsync_2()
        {
            SortedSetEntry[] values = new SortedSetEntry[0];
            wrapper.SortedSetAddAsync("key", values, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetAddAsync("prefix:key", values, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetCombineAndStoreAsync_1()
        {
            wrapper.SortedSetCombineAndStoreAsync(SetOperation.Intersect, "destination", "first", "second", Aggregate.Max, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", Aggregate.Max, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetCombineAndStoreAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", keys, CommandFlags.HighPriority);
            mock.Verify(_ => _.SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", It.Is(valid), CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetDecrementAsync()
        {
            wrapper.SortedSetDecrementAsync("key", "member", 1.23, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetDecrementAsync("prefix:key", "member", 1.23, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetIncrementAsync()
        {
            wrapper.SortedSetIncrementAsync("key", "member", 1.23, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetIncrementAsync("prefix:key", "member", 1.23, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetLengthAsync()
        {
            wrapper.SortedSetLengthAsync("key", 1.23, 1.23, Exclude.Start, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetLengthAsync("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetLengthByValueAsync()
        {
            wrapper.SortedSetLengthByValueAsync("key", "min", "max", Exclude.Start, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetLengthByValueAsync("prefix:key", "min", "max", Exclude.Start, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetRangeByRankAsync()
        {
            wrapper.SortedSetRangeByRankAsync("key", 123, 456, Order.Descending, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetRangeByRankAsync("prefix:key", 123, 456, Order.Descending, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetRangeByRankWithScoresAsync()
        {
            wrapper.SortedSetRangeByRankWithScoresAsync("key", 123, 456, Order.Descending, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetRangeByRankWithScoresAsync("prefix:key", 123, 456, Order.Descending, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetRangeByScoreAsync()
        {
            wrapper.SortedSetRangeByScoreAsync("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetRangeByScoreAsync("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetRangeByScoreWithScoresAsync()
        {
            wrapper.SortedSetRangeByScoreWithScoresAsync("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetRangeByScoreWithScoresAsync("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetRangeByValueAsync()
        {
            wrapper.SortedSetRangeByValueAsync("key", "min", "max", Exclude.Start, 123, 456, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetRangeByValueAsync("prefix:key", "min", "max", Exclude.Start, 123, 456, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetRankAsync()
        {
            wrapper.SortedSetRankAsync("key", "member", Order.Descending, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetRankAsync("prefix:key", "member", Order.Descending, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetRemoveAsync_1()
        {
            wrapper.SortedSetRemoveAsync("key", "member", CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetRemoveAsync("prefix:key", "member", CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetRemoveAsync_2()
        {
            RedisValue[] members = new RedisValue[0];
            wrapper.SortedSetRemoveAsync("key", members, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetRemoveAsync("prefix:key", members, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetRemoveRangeByRankAsync()
        {
            wrapper.SortedSetRemoveRangeByRankAsync("key", 123, 456, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetRemoveRangeByRankAsync("prefix:key", 123, 456, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetRemoveRangeByScoreAsync()
        {
            wrapper.SortedSetRemoveRangeByScoreAsync("key", 1.23, 1.23, Exclude.Start, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetRemoveRangeByScoreAsync("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetRemoveRangeByValueAsync()
        {
            wrapper.SortedSetRemoveRangeByValueAsync("key", "min", "max", Exclude.Start, CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetRemoveRangeByValueAsync("prefix:key", "min", "max", Exclude.Start, CommandFlags.HighPriority));
        }

        [Test]
        public void SortedSetScoreAsync()
        {
            wrapper.SortedSetScoreAsync("key", "member", CommandFlags.HighPriority);
            mock.Verify(_ => _.SortedSetScoreAsync("prefix:key", "member", CommandFlags.HighPriority));
        }

        [Test]
        public void StringAppendAsync()
        {
            wrapper.StringAppendAsync("key", "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.StringAppendAsync("prefix:key", "value", CommandFlags.HighPriority));
        }

        [Test]
        public void StringBitCountAsync()
        {
            wrapper.StringBitCountAsync("key", 123, 456, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringBitCountAsync("prefix:key", 123, 456, CommandFlags.HighPriority));
        }

        [Test]
        public void StringBitOperationAsync_1()
        {
            wrapper.StringBitOperationAsync(Bitwise.Xor, "destination", "first", "second", CommandFlags.HighPriority);
            mock.Verify(_ => _.StringBitOperationAsync(Bitwise.Xor, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.HighPriority));
        }

        [Test]
        public void StringBitOperationAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.StringBitOperationAsync(Bitwise.Xor, "destination", keys, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringBitOperationAsync(Bitwise.Xor, "prefix:destination", It.Is(valid), CommandFlags.HighPriority));
        }

        [Test]
        public void StringBitPositionAsync()
        {
            wrapper.StringBitPositionAsync("key", true, 123, 456, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringBitPositionAsync("prefix:key", true, 123, 456, CommandFlags.HighPriority));
        }

        [Test]
        public void StringDecrementAsync_1()
        {
            wrapper.StringDecrementAsync("key", 123, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringDecrementAsync("prefix:key", 123, CommandFlags.HighPriority));
        }

        [Test]
        public void StringDecrementAsync_2()
        {
            wrapper.StringDecrementAsync("key", 1.23, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringDecrementAsync("prefix:key", 1.23, CommandFlags.HighPriority));
        }

        [Test]
        public void StringGetAsync_1()
        {
            wrapper.StringGetAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.StringGetAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void StringGetAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.StringGetAsync(keys, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringGetAsync(It.Is(valid), CommandFlags.HighPriority));
        }

        [Test]
        public void StringGetBitAsync()
        {
            wrapper.StringGetBitAsync("key", 123, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringGetBitAsync("prefix:key", 123, CommandFlags.HighPriority));
        }

        [Test]
        public void StringGetRangeAsync()
        {
            wrapper.StringGetRangeAsync("key", 123, 456, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringGetRangeAsync("prefix:key", 123, 456, CommandFlags.HighPriority));
        }

        [Test]
        public void StringGetSetAsync()
        {
            wrapper.StringGetSetAsync("key", "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.StringGetSetAsync("prefix:key", "value", CommandFlags.HighPriority));
        }

        [Test]
        public void StringGetWithExpiryAsync()
        {
            wrapper.StringGetWithExpiryAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.StringGetWithExpiryAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void StringIncrementAsync_1()
        {
            wrapper.StringIncrementAsync("key", 123, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringIncrementAsync("prefix:key", 123, CommandFlags.HighPriority));
        }

        [Test]
        public void StringIncrementAsync_2()
        {
            wrapper.StringIncrementAsync("key", 1.23, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringIncrementAsync("prefix:key", 1.23, CommandFlags.HighPriority));
        }

        [Test]
        public void StringLengthAsync()
        {
            wrapper.StringLengthAsync("key", CommandFlags.HighPriority);
            mock.Verify(_ => _.StringLengthAsync("prefix:key", CommandFlags.HighPriority));
        }

        [Test]
        public void StringSetAsync_1()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            wrapper.StringSetAsync("key", "value", expiry, When.Exists, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringSetAsync("prefix:key", "value", expiry, When.Exists, CommandFlags.HighPriority));
        }

        [Test]
        public void StringSetAsync_2()
        {
            KeyValuePair<RedisKey, RedisValue>[] values = new KeyValuePair<RedisKey, RedisValue>[] { new KeyValuePair<RedisKey, RedisValue>("a", "x"), new KeyValuePair<RedisKey, RedisValue>("b", "y") };
            Expression<Func<KeyValuePair<RedisKey, RedisValue>[], bool>> valid = _ => _.Length == 2 && _[0].Key == "prefix:a" && _[0].Value == "x" && _[1].Key == "prefix:b" && _[1].Value == "y";
            wrapper.StringSetAsync(values, When.Exists, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringSetAsync(It.Is(valid), When.Exists, CommandFlags.HighPriority));
        }

        [Test]
        public void StringSetBitAsync()
        {
            wrapper.StringSetBitAsync("key", 123, true, CommandFlags.HighPriority);
            mock.Verify(_ => _.StringSetBitAsync("prefix:key", 123, true, CommandFlags.HighPriority));
        }

        [Test]
        public void StringSetRangeAsync()
        {
            wrapper.StringSetRangeAsync("key", 123, "value", CommandFlags.HighPriority);
            mock.Verify(_ => _.StringSetRangeAsync("prefix:key", 123, "value", CommandFlags.HighPriority));
        }
    }
}
#endif
