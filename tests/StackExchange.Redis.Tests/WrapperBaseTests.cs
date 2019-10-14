using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net;
using System.Text;
using Moq;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests
{
    public sealed class WrapperBaseTests
    {
        private readonly Mock<IDatabaseAsync> mock;
        private readonly WrapperBase<IDatabaseAsync> wrapper;

        public WrapperBaseTests()
        {
            mock = new Mock<IDatabaseAsync>();
            wrapper = new WrapperBase<IDatabaseAsync>(mock.Object, Encoding.UTF8.GetBytes("prefix:"));
        }

#pragma warning disable RCS1047 // Non-asynchronous method name should not end with 'Async'.

        [Fact]
        public void DebugObjectAsync()
        {
            wrapper.DebugObjectAsync("key", CommandFlags.None);
            mock.Verify(_ => _.DebugObjectAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HashDecrementAsync_1()
        {
            wrapper.HashDecrementAsync("key", "hashField", 123, CommandFlags.None);
            mock.Verify(_ => _.HashDecrementAsync("prefix:key", "hashField", 123, CommandFlags.None));
        }

        [Fact]
        public void HashDecrementAsync_2()
        {
            wrapper.HashDecrementAsync("key", "hashField", 1.23, CommandFlags.None);
            mock.Verify(_ => _.HashDecrementAsync("prefix:key", "hashField", 1.23, CommandFlags.None));
        }

        [Fact]
        public void HashDeleteAsync_1()
        {
            wrapper.HashDeleteAsync("key", "hashField", CommandFlags.None);
            mock.Verify(_ => _.HashDeleteAsync("prefix:key", "hashField", CommandFlags.None));
        }

        [Fact]
        public void HashDeleteAsync_2()
        {
            RedisValue[] hashFields = new RedisValue[0];
            wrapper.HashDeleteAsync("key", hashFields, CommandFlags.None);
            mock.Verify(_ => _.HashDeleteAsync("prefix:key", hashFields, CommandFlags.None));
        }

        [Fact]
        public void HashExistsAsync()
        {
            wrapper.HashExistsAsync("key", "hashField", CommandFlags.None);
            mock.Verify(_ => _.HashExistsAsync("prefix:key", "hashField", CommandFlags.None));
        }

        [Fact]
        public void HashGetAllAsync()
        {
            wrapper.HashGetAllAsync("key", CommandFlags.None);
            mock.Verify(_ => _.HashGetAllAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HashGetAsync_1()
        {
            wrapper.HashGetAsync("key", "hashField", CommandFlags.None);
            mock.Verify(_ => _.HashGetAsync("prefix:key", "hashField", CommandFlags.None));
        }

        [Fact]
        public void HashGetAsync_2()
        {
            RedisValue[] hashFields = new RedisValue[0];
            wrapper.HashGetAsync("key", hashFields, CommandFlags.None);
            mock.Verify(_ => _.HashGetAsync("prefix:key", hashFields, CommandFlags.None));
        }

        [Fact]
        public void HashIncrementAsync_1()
        {
            wrapper.HashIncrementAsync("key", "hashField", 123, CommandFlags.None);
            mock.Verify(_ => _.HashIncrementAsync("prefix:key", "hashField", 123, CommandFlags.None));
        }

        [Fact]
        public void HashIncrementAsync_2()
        {
            wrapper.HashIncrementAsync("key", "hashField", 1.23, CommandFlags.None);
            mock.Verify(_ => _.HashIncrementAsync("prefix:key", "hashField", 1.23, CommandFlags.None));
        }

        [Fact]
        public void HashKeysAsync()
        {
            wrapper.HashKeysAsync("key", CommandFlags.None);
            mock.Verify(_ => _.HashKeysAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HashLengthAsync()
        {
            wrapper.HashLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.HashLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HashSetAsync_1()
        {
            HashEntry[] hashFields = new HashEntry[0];
            wrapper.HashSetAsync("key", hashFields, CommandFlags.None);
            mock.Verify(_ => _.HashSetAsync("prefix:key", hashFields, CommandFlags.None));
        }

        [Fact]
        public void HashSetAsync_2()
        {
            wrapper.HashSetAsync("key", "hashField", "value", When.Exists, CommandFlags.None);
            mock.Verify(_ => _.HashSetAsync("prefix:key", "hashField", "value", When.Exists, CommandFlags.None));
        }
        
        [Fact]
        public void HashStringLengthAsync()
        {
            wrapper.HashStringLengthAsync("key","field", CommandFlags.None);
            mock.Verify(_ => _.HashStringLengthAsync("prefix:key", "field", CommandFlags.None));
        }
        
        [Fact]
        public void HashValuesAsync()
        {
            wrapper.HashValuesAsync("key", CommandFlags.None);
            mock.Verify(_ => _.HashValuesAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HyperLogLogAddAsync_1()
        {
            wrapper.HyperLogLogAddAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.HyperLogLogAddAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void HyperLogLogAddAsync_2()
        {
            var values = new RedisValue[0];
            wrapper.HyperLogLogAddAsync("key", values, CommandFlags.None);
            mock.Verify(_ => _.HyperLogLogAddAsync("prefix:key", values, CommandFlags.None));
        }

        [Fact]
        public void HyperLogLogLengthAsync()
        {
            wrapper.HyperLogLogLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.HyperLogLogLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HyperLogLogMergeAsync_1()
        {
            wrapper.HyperLogLogMergeAsync("destination", "first", "second", CommandFlags.None);
            mock.Verify(_ => _.HyperLogLogMergeAsync("prefix:destination", "prefix:first", "prefix:second", CommandFlags.None));
        }

        [Fact]
        public void HyperLogLogMergeAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.HyperLogLogMergeAsync("destination", keys, CommandFlags.None);
            mock.Verify(_ => _.HyperLogLogMergeAsync("prefix:destination", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void IdentifyEndpointAsync()
        {
            wrapper.IdentifyEndpointAsync("key", CommandFlags.None);
            mock.Verify(_ => _.IdentifyEndpointAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void IsConnected()
        {
            wrapper.IsConnected("key", CommandFlags.None);
            mock.Verify(_ => _.IsConnected("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyDeleteAsync_1()
        {
            wrapper.KeyDeleteAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyDeleteAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyDeleteAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.KeyDeleteAsync(keys, CommandFlags.None);
            mock.Verify(_ => _.KeyDeleteAsync(It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void KeyDumpAsync()
        {
            wrapper.KeyDumpAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyDumpAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyExistsAsync()
        {
            wrapper.KeyExistsAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyExistsAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyExpireAsync_1()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            wrapper.KeyExpireAsync("key", expiry, CommandFlags.None);
            mock.Verify(_ => _.KeyExpireAsync("prefix:key", expiry, CommandFlags.None));
        }

        [Fact]
        public void KeyExpireAsync_2()
        {
            DateTime expiry = DateTime.Now;
            wrapper.KeyExpireAsync("key", expiry, CommandFlags.None);
            mock.Verify(_ => _.KeyExpireAsync("prefix:key", expiry, CommandFlags.None));
        }

        [Fact]
        public void KeyMigrateAsync()
        {
            EndPoint toServer = new IPEndPoint(IPAddress.Loopback, 123);
            wrapper.KeyMigrateAsync("key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.None);
            mock.Verify(_ => _.KeyMigrateAsync("prefix:key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.None));
        }

        [Fact]
        public void KeyMoveAsync()
        {
            wrapper.KeyMoveAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.KeyMoveAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void KeyPersistAsync()
        {
            wrapper.KeyPersistAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyPersistAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public Task KeyRandomAsync()
        {
            return Assert.ThrowsAsync<NotSupportedException>(() => wrapper.KeyRandomAsync());
        }

        [Fact]
        public void KeyRenameAsync()
        {
            wrapper.KeyRenameAsync("key", "newKey", When.Exists, CommandFlags.None);
            mock.Verify(_ => _.KeyRenameAsync("prefix:key", "prefix:newKey", When.Exists, CommandFlags.None));
        }

        [Fact]
        public void KeyRestoreAsync()
        {
            byte[] value = new byte[0];
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            wrapper.KeyRestoreAsync("key", value, expiry, CommandFlags.None);
            mock.Verify(_ => _.KeyRestoreAsync("prefix:key", value, expiry, CommandFlags.None));
        }

        [Fact]
        public void KeyTimeToLiveAsync()
        {
            wrapper.KeyTimeToLiveAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyTimeToLiveAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyTypeAsync()
        {
            wrapper.KeyTypeAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyTypeAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void ListGetByIndexAsync()
        {
            wrapper.ListGetByIndexAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.ListGetByIndexAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void ListInsertAfterAsync()
        {
            wrapper.ListInsertAfterAsync("key", "pivot", "value", CommandFlags.None);
            mock.Verify(_ => _.ListInsertAfterAsync("prefix:key", "pivot", "value", CommandFlags.None));
        }

        [Fact]
        public void ListInsertBeforeAsync()
        {
            wrapper.ListInsertBeforeAsync("key", "pivot", "value", CommandFlags.None);
            mock.Verify(_ => _.ListInsertBeforeAsync("prefix:key", "pivot", "value", CommandFlags.None));
        }

        [Fact]
        public void ListLeftPopAsync()
        {
            wrapper.ListLeftPopAsync("key", CommandFlags.None);
            mock.Verify(_ => _.ListLeftPopAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void ListLeftPushAsync_1()
        {
            wrapper.ListLeftPushAsync("key", "value", When.Exists, CommandFlags.None);
            mock.Verify(_ => _.ListLeftPushAsync("prefix:key", "value", When.Exists, CommandFlags.None));
        }

        [Fact]
        public void ListLeftPushAsync_2()
        {
            RedisValue[] values = new RedisValue[0];
            wrapper.ListLeftPushAsync("key", values, CommandFlags.None);
            mock.Verify(_ => _.ListLeftPushAsync("prefix:key", values, CommandFlags.None));
        }

        [Fact]
        public void ListLengthAsync()
        {
            wrapper.ListLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.ListLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void ListRangeAsync()
        {
            wrapper.ListRangeAsync("key", 123, 456, CommandFlags.None);
            mock.Verify(_ => _.ListRangeAsync("prefix:key", 123, 456, CommandFlags.None));
        }

        [Fact]
        public void ListRemoveAsync()
        {
            wrapper.ListRemoveAsync("key", "value", 123, CommandFlags.None);
            mock.Verify(_ => _.ListRemoveAsync("prefix:key", "value", 123, CommandFlags.None));
        }

        [Fact]
        public void ListRightPopAsync()
        {
            wrapper.ListRightPopAsync("key", CommandFlags.None);
            mock.Verify(_ => _.ListRightPopAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void ListRightPopLeftPushAsync()
        {
            wrapper.ListRightPopLeftPushAsync("source", "destination", CommandFlags.None);
            mock.Verify(_ => _.ListRightPopLeftPushAsync("prefix:source", "prefix:destination", CommandFlags.None));
        }

        [Fact]
        public void ListRightPushAsync_1()
        {
            wrapper.ListRightPushAsync("key", "value", When.Exists, CommandFlags.None);
            mock.Verify(_ => _.ListRightPushAsync("prefix:key", "value", When.Exists, CommandFlags.None));
        }

        [Fact]
        public void ListRightPushAsync_2()
        {
            RedisValue[] values = new RedisValue[0];
            wrapper.ListRightPushAsync("key", values, CommandFlags.None);
            mock.Verify(_ => _.ListRightPushAsync("prefix:key", values, CommandFlags.None));
        }

        [Fact]
        public void ListSetByIndexAsync()
        {
            wrapper.ListSetByIndexAsync("key", 123, "value", CommandFlags.None);
            mock.Verify(_ => _.ListSetByIndexAsync("prefix:key", 123, "value", CommandFlags.None));
        }

        [Fact]
        public void ListTrimAsync()
        {
            wrapper.ListTrimAsync("key", 123, 456, CommandFlags.None);
            mock.Verify(_ => _.ListTrimAsync("prefix:key", 123, 456, CommandFlags.None));
        }

        [Fact]
        public void LockExtendAsync()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            wrapper.LockExtendAsync("key", "value", expiry, CommandFlags.None);
            mock.Verify(_ => _.LockExtendAsync("prefix:key", "value", expiry, CommandFlags.None));
        }

        [Fact]
        public void LockQueryAsync()
        {
            wrapper.LockQueryAsync("key", CommandFlags.None);
            mock.Verify(_ => _.LockQueryAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void LockReleaseAsync()
        {
            wrapper.LockReleaseAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.LockReleaseAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void LockTakeAsync()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            wrapper.LockTakeAsync("key", "value", expiry, CommandFlags.None);
            mock.Verify(_ => _.LockTakeAsync("prefix:key", "value", expiry, CommandFlags.None));
        }

        [Fact]
        public void PublishAsync()
        {
            wrapper.PublishAsync("channel", "message", CommandFlags.None);
            mock.Verify(_ => _.PublishAsync("prefix:channel", "message", CommandFlags.None));
        }

        [Fact]
        public void ScriptEvaluateAsync_1()
        {
            byte[] hash = new byte[0];
            RedisValue[] values = new RedisValue[0];
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.ScriptEvaluateAsync(hash, keys, values, CommandFlags.None);
            mock.Verify(_ => _.ScriptEvaluateAsync(hash, It.Is(valid), values, CommandFlags.None));
        }

        [Fact]
        public void ScriptEvaluateAsync_2()
        {
            RedisValue[] values = new RedisValue[0];
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.ScriptEvaluateAsync("script", keys, values, CommandFlags.None);
            mock.Verify(_ => _.ScriptEvaluateAsync("script", It.Is(valid), values, CommandFlags.None));
        }

        [Fact]
        public void SetAddAsync_1()
        {
            wrapper.SetAddAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.SetAddAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void SetAddAsync_2()
        {
            RedisValue[] values = new RedisValue[0];
            wrapper.SetAddAsync("key", values, CommandFlags.None);
            mock.Verify(_ => _.SetAddAsync("prefix:key", values, CommandFlags.None));
        }

        [Fact]
        public void SetCombineAndStoreAsync_1()
        {
            wrapper.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", "first", "second", CommandFlags.None);
            mock.Verify(_ => _.SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.None));
        }

        [Fact]
        public void SetCombineAndStoreAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", keys, CommandFlags.None);
            mock.Verify(_ => _.SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void SetCombineAsync_1()
        {
            wrapper.SetCombineAsync(SetOperation.Intersect, "first", "second", CommandFlags.None);
            mock.Verify(_ => _.SetCombineAsync(SetOperation.Intersect, "prefix:first", "prefix:second", CommandFlags.None));
        }

        [Fact]
        public void SetCombineAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.SetCombineAsync(SetOperation.Intersect, keys, CommandFlags.None);
            mock.Verify(_ => _.SetCombineAsync(SetOperation.Intersect, It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void SetContainsAsync()
        {
            wrapper.SetContainsAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.SetContainsAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void SetLengthAsync()
        {
            wrapper.SetLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.SetLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void SetMembersAsync()
        {
            wrapper.SetMembersAsync("key", CommandFlags.None);
            mock.Verify(_ => _.SetMembersAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void SetMoveAsync()
        {
            wrapper.SetMoveAsync("source", "destination", "value", CommandFlags.None);
            mock.Verify(_ => _.SetMoveAsync("prefix:source", "prefix:destination", "value", CommandFlags.None));
        }

        [Fact]
        public void SetPopAsync_1()
        {
            wrapper.SetPopAsync("key", CommandFlags.None);
            mock.Verify(_ => _.SetPopAsync("prefix:key", CommandFlags.None));

            wrapper.SetPopAsync("key", 5, CommandFlags.None);
            mock.Verify(_ => _.SetPopAsync("prefix:key", 5, CommandFlags.None));
        }

        [Fact]
        public void SetPopAsync_2()
        {
            wrapper.SetPopAsync("key", 5, CommandFlags.None);
            mock.Verify(_ => _.SetPopAsync("prefix:key", 5, CommandFlags.None));
        }

        [Fact]
        public void SetRandomMemberAsync()
        {
            wrapper.SetRandomMemberAsync("key", CommandFlags.None);
            mock.Verify(_ => _.SetRandomMemberAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void SetRandomMembersAsync()
        {
            wrapper.SetRandomMembersAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.SetRandomMembersAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void SetRemoveAsync_1()
        {
            wrapper.SetRemoveAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.SetRemoveAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void SetRemoveAsync_2()
        {
            RedisValue[] values = new RedisValue[0];
            wrapper.SetRemoveAsync("key", values, CommandFlags.None);
            mock.Verify(_ => _.SetRemoveAsync("prefix:key", values, CommandFlags.None));
        }

        [Fact]
        public void SortAndStoreAsync()
        {
            RedisValue[] get = new RedisValue[] { "a", "#" };
            Expression<Func<RedisValue[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "#";

            wrapper.SortAndStoreAsync("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.None);
            wrapper.SortAndStoreAsync("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.None);

            mock.Verify(_ => _.SortAndStoreAsync("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", It.Is(valid), CommandFlags.None));
            mock.Verify(_ => _.SortAndStoreAsync("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void SortAsync()
        {
            RedisValue[] get = new RedisValue[] { "a", "#" };
            Expression<Func<RedisValue[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "#";

            wrapper.SortAsync("key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.None);
            wrapper.SortAsync("key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.None);

            mock.Verify(_ => _.SortAsync("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", It.Is(valid), CommandFlags.None));
            mock.Verify(_ => _.SortAsync("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void SortedSetAddAsync_1()
        {
            wrapper.SortedSetAddAsync("key", "member", 1.23, When.Exists, CommandFlags.None);
            mock.Verify(_ => _.SortedSetAddAsync("prefix:key", "member", 1.23, When.Exists, CommandFlags.None));
        }

        [Fact]
        public void SortedSetAddAsync_2()
        {
            SortedSetEntry[] values = new SortedSetEntry[0];
            wrapper.SortedSetAddAsync("key", values, When.Exists, CommandFlags.None);
            mock.Verify(_ => _.SortedSetAddAsync("prefix:key", values, When.Exists, CommandFlags.None));
        }

        [Fact]
        public void SortedSetCombineAndStoreAsync_1()
        {
            wrapper.SortedSetCombineAndStoreAsync(SetOperation.Intersect, "destination", "first", "second", Aggregate.Max, CommandFlags.None);
            mock.Verify(_ => _.SortedSetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", Aggregate.Max, CommandFlags.None));
        }

        [Fact]
        public void SortedSetCombineAndStoreAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", keys, CommandFlags.None);
            mock.Verify(_ => _.SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void SortedSetDecrementAsync()
        {
            wrapper.SortedSetDecrementAsync("key", "member", 1.23, CommandFlags.None);
            mock.Verify(_ => _.SortedSetDecrementAsync("prefix:key", "member", 1.23, CommandFlags.None));
        }

        [Fact]
        public void SortedSetIncrementAsync()
        {
            wrapper.SortedSetIncrementAsync("key", "member", 1.23, CommandFlags.None);
            mock.Verify(_ => _.SortedSetIncrementAsync("prefix:key", "member", 1.23, CommandFlags.None));
        }

        [Fact]
        public void SortedSetLengthAsync()
        {
            wrapper.SortedSetLengthAsync("key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
            mock.Verify(_ => _.SortedSetLengthAsync("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.None));
        }

        [Fact]
        public void SortedSetLengthByValueAsync()
        {
            wrapper.SortedSetLengthByValueAsync("key", "min", "max", Exclude.Start, CommandFlags.None);
            mock.Verify(_ => _.SortedSetLengthByValueAsync("prefix:key", "min", "max", Exclude.Start, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByRankAsync()
        {
            wrapper.SortedSetRangeByRankAsync("key", 123, 456, Order.Descending, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByRankAsync("prefix:key", 123, 456, Order.Descending, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByRankWithScoresAsync()
        {
            wrapper.SortedSetRangeByRankWithScoresAsync("key", 123, 456, Order.Descending, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByRankWithScoresAsync("prefix:key", 123, 456, Order.Descending, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByScoreAsync()
        {
            wrapper.SortedSetRangeByScoreAsync("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByScoreAsync("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByScoreWithScoresAsync()
        {
            wrapper.SortedSetRangeByScoreWithScoresAsync("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByScoreWithScoresAsync("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByValueAsync()
        {
            wrapper.SortedSetRangeByValueAsync("key", "min", "max", Exclude.Start, 123, 456, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByValueAsync("prefix:key", "min", "max", Exclude.Start, Order.Ascending, 123, 456, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByValueDescAsync()
        {
            wrapper.SortedSetRangeByValueAsync("key", "min", "max", Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByValueAsync("prefix:key", "min", "max", Exclude.Start, Order.Descending, 123, 456, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRankAsync()
        {
            wrapper.SortedSetRankAsync("key", "member", Order.Descending, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRankAsync("prefix:key", "member", Order.Descending, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRemoveAsync_1()
        {
            wrapper.SortedSetRemoveAsync("key", "member", CommandFlags.None);
            mock.Verify(_ => _.SortedSetRemoveAsync("prefix:key", "member", CommandFlags.None));
        }

        [Fact]
        public void SortedSetRemoveAsync_2()
        {
            RedisValue[] members = new RedisValue[0];
            wrapper.SortedSetRemoveAsync("key", members, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRemoveAsync("prefix:key", members, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRemoveRangeByRankAsync()
        {
            wrapper.SortedSetRemoveRangeByRankAsync("key", 123, 456, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRemoveRangeByRankAsync("prefix:key", 123, 456, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRemoveRangeByScoreAsync()
        {
            wrapper.SortedSetRemoveRangeByScoreAsync("key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRemoveRangeByScoreAsync("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRemoveRangeByValueAsync()
        {
            wrapper.SortedSetRemoveRangeByValueAsync("key", "min", "max", Exclude.Start, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRemoveRangeByValueAsync("prefix:key", "min", "max", Exclude.Start, CommandFlags.None));
        }

        [Fact]
        public void SortedSetScoreAsync()
        {
            wrapper.SortedSetScoreAsync("key", "member", CommandFlags.None);
            mock.Verify(_ => _.SortedSetScoreAsync("prefix:key", "member", CommandFlags.None));
        }

        [Fact]
        public void StreamAcknowledgeAsync_1()
        {
            wrapper.StreamAcknowledgeAsync("key", "group", "0-0", CommandFlags.None);
            mock.Verify(_ => _.StreamAcknowledgeAsync("prefix:key", "group", "0-0", CommandFlags.None));
        }

        [Fact]
        public void StreamAcknowledgeAsync_2()
        {
            var messageIds = new RedisValue[] { "0-0", "0-1", "0-2" };
            wrapper.StreamAcknowledgeAsync("key", "group", messageIds, CommandFlags.None);
            mock.Verify(_ => _.StreamAcknowledgeAsync("prefix:key", "group", messageIds, CommandFlags.None));
        }

        [Fact]
        public void StreamAddAsync_1()
        {
            wrapper.StreamAddAsync("key", "field1", "value1", "*", 1000, true, CommandFlags.None);
            mock.Verify(_ => _.StreamAddAsync("prefix:key", "field1", "value1", "*", 1000, true, CommandFlags.None));
        }

        [Fact]
        public void StreamAddAsync_2()
        {
            var fields = new NameValueEntry[0];
            wrapper.StreamAddAsync("key", fields, "*", 1000, true, CommandFlags.None);
            mock.Verify(_ => _.StreamAddAsync("prefix:key", fields, "*", 1000, true, CommandFlags.None));
        }

        [Fact]
        public void StreamClaimMessagesAsync()
        {
            var messageIds = new RedisValue[0];
            wrapper.StreamClaimAsync("key", "group", "consumer", 1000, messageIds, CommandFlags.None);
            mock.Verify(_ => _.StreamClaimAsync("prefix:key", "group", "consumer", 1000, messageIds, CommandFlags.None));
        }

        [Fact]
        public void StreamClaimMessagesReturningIdsAsync()
        {
            var messageIds = new RedisValue[0];
            wrapper.StreamClaimIdsOnlyAsync("key", "group", "consumer", 1000, messageIds, CommandFlags.None);
            mock.Verify(_ => _.StreamClaimIdsOnlyAsync("prefix:key", "group", "consumer", 1000, messageIds, CommandFlags.None));
        }

        [Fact]
        public void StreamConsumerInfoGetAsync()
        {
            wrapper.StreamConsumerInfoAsync("key", "group", CommandFlags.None);
            mock.Verify(_ => _.StreamConsumerInfoAsync("prefix:key", "group", CommandFlags.None));
        }

        [Fact]
        public void StreamConsumerGroupSetPositionAsync()
        {
            wrapper.StreamConsumerGroupSetPositionAsync("key", "group", StreamPosition.Beginning, CommandFlags.None);
            mock.Verify(_ => _.StreamConsumerGroupSetPositionAsync("prefix:key", "group", StreamPosition.Beginning, CommandFlags.None));
        }

        [Fact]
        public void StreamCreateConsumerGroupAsync()
        {
            wrapper.StreamCreateConsumerGroupAsync("key", "group", "0-0", CommandFlags.None);
            mock.Verify(_ => _.StreamCreateConsumerGroupAsync("prefix:key", "group", "0-0", CommandFlags.None));
        }

        [Fact]
        public void StreamGroupInfoGetAsync()
        {
            wrapper.StreamGroupInfoAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StreamGroupInfoAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StreamInfoGetAsync()
        {
            wrapper.StreamInfoAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StreamInfoAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StreamLengthAsync()
        {
            wrapper.StreamLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StreamLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StreamMessagesDeleteAsync()
        {
            var messageIds = new RedisValue[0] { };
            wrapper.StreamDeleteAsync("key", messageIds, CommandFlags.None);
            mock.Verify(_ => _.StreamDeleteAsync("prefix:key", messageIds, CommandFlags.None));
        }

        [Fact]
        public void StreamDeleteConsumerAsync()
        {
            wrapper.StreamDeleteConsumerAsync("key", "group", "consumer", CommandFlags.None);
            mock.Verify(_ => _.StreamDeleteConsumerAsync("prefix:key", "group", "consumer", CommandFlags.None));
        }

        [Fact]
        public void StreamDeleteConsumerGroupAsync()
        {
            wrapper.StreamDeleteConsumerGroupAsync("key", "group", CommandFlags.None);
            mock.Verify(_ => _.StreamDeleteConsumerGroupAsync("prefix:key", "group", CommandFlags.None));
        }

        [Fact]
        public void StreamPendingInfoGetAsync()
        {
            wrapper.StreamPendingAsync("key", "group", CommandFlags.None);
            mock.Verify(_ => _.StreamPendingAsync("prefix:key", "group", CommandFlags.None));
        }

        [Fact]
        public void StreamPendingMessageInfoGetAsync()
        {
            wrapper.StreamPendingMessagesAsync("key", "group", 10, RedisValue.Null, "-", "+", CommandFlags.None);
            mock.Verify(_ => _.StreamPendingMessagesAsync("prefix:key", "group", 10, RedisValue.Null, "-", "+", CommandFlags.None));
        }

        [Fact]
        public void StreamRangeAsync()
        {
            wrapper.StreamRangeAsync("key", "-", "+", null, Order.Ascending, CommandFlags.None);
            mock.Verify(_ => _.StreamRangeAsync("prefix:key", "-", "+", null, Order.Ascending, CommandFlags.None));
        }

        [Fact]
        public void StreamReadAsync_1()
        {
            var streamPositions = new StreamPosition[0] { };
            wrapper.StreamReadAsync(streamPositions, null, CommandFlags.None);
            mock.Verify(_ => _.StreamReadAsync(streamPositions, null, CommandFlags.None));
        }

        [Fact]
        public void StreamReadAsync_2()
        {
            wrapper.StreamReadAsync("key", "0-0", null, CommandFlags.None);
            mock.Verify(_ => _.StreamReadAsync("prefix:key", "0-0", null, CommandFlags.None));
        }

        [Fact]
        public void StreamReadGroupAsync_1()
        {
            wrapper.StreamReadGroupAsync("key", "group", "consumer", StreamPosition.Beginning, 10, CommandFlags.None);
            mock.Verify(_ => _.StreamReadGroupAsync("prefix:key", "group", "consumer", StreamPosition.Beginning, 10, CommandFlags.None));
        }

        [Fact]
        public void StreamStreamReadGroupAsync_2()
        {
            var streamPositions = new StreamPosition[0] { };
            wrapper.StreamReadGroupAsync(streamPositions, "group", "consumer", 10, CommandFlags.None);
            mock.Verify(_ => _.StreamReadGroupAsync(streamPositions, "group", "consumer", 10, CommandFlags.None));
        }

        [Fact]
        public void StreamTrimAsync()
        {
            wrapper.StreamTrimAsync("key", 1000, true, CommandFlags.None);
            mock.Verify(_ => _.StreamTrimAsync("prefix:key", 1000, true, CommandFlags.None));
        }

        [Fact]
        public void StringAppendAsync()
        {
            wrapper.StringAppendAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.StringAppendAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void StringBitCountAsync()
        {
            wrapper.StringBitCountAsync("key", 123, 456, CommandFlags.None);
            mock.Verify(_ => _.StringBitCountAsync("prefix:key", 123, 456, CommandFlags.None));
        }

        [Fact]
        public void StringBitOperationAsync_1()
        {
            wrapper.StringBitOperationAsync(Bitwise.Xor, "destination", "first", "second", CommandFlags.None);
            mock.Verify(_ => _.StringBitOperationAsync(Bitwise.Xor, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.None));
        }

        [Fact]
        public void StringBitOperationAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.StringBitOperationAsync(Bitwise.Xor, "destination", keys, CommandFlags.None);
            mock.Verify(_ => _.StringBitOperationAsync(Bitwise.Xor, "prefix:destination", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void StringBitPositionAsync()
        {
            wrapper.StringBitPositionAsync("key", true, 123, 456, CommandFlags.None);
            mock.Verify(_ => _.StringBitPositionAsync("prefix:key", true, 123, 456, CommandFlags.None));
        }

        [Fact]
        public void StringDecrementAsync_1()
        {
            wrapper.StringDecrementAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.StringDecrementAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void StringDecrementAsync_2()
        {
            wrapper.StringDecrementAsync("key", 1.23, CommandFlags.None);
            mock.Verify(_ => _.StringDecrementAsync("prefix:key", 1.23, CommandFlags.None));
        }

        [Fact]
        public void StringGetAsync_1()
        {
            wrapper.StringGetAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StringGetAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StringGetAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            wrapper.StringGetAsync(keys, CommandFlags.None);
            mock.Verify(_ => _.StringGetAsync(It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void StringGetBitAsync()
        {
            wrapper.StringGetBitAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.StringGetBitAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void StringGetRangeAsync()
        {
            wrapper.StringGetRangeAsync("key", 123, 456, CommandFlags.None);
            mock.Verify(_ => _.StringGetRangeAsync("prefix:key", 123, 456, CommandFlags.None));
        }

        [Fact]
        public void StringGetSetAsync()
        {
            wrapper.StringGetSetAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.StringGetSetAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void StringGetWithExpiryAsync()
        {
            wrapper.StringGetWithExpiryAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StringGetWithExpiryAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StringIncrementAsync_1()
        {
            wrapper.StringIncrementAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.StringIncrementAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void StringIncrementAsync_2()
        {
            wrapper.StringIncrementAsync("key", 1.23, CommandFlags.None);
            mock.Verify(_ => _.StringIncrementAsync("prefix:key", 1.23, CommandFlags.None));
        }

        [Fact]
        public void StringLengthAsync()
        {
            wrapper.StringLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StringLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StringSetAsync_1()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            wrapper.StringSetAsync("key", "value", expiry, When.Exists, CommandFlags.None);
            mock.Verify(_ => _.StringSetAsync("prefix:key", "value", expiry, When.Exists, CommandFlags.None));
        }

        [Fact]
        public void StringSetAsync_2()
        {
            KeyValuePair<RedisKey, RedisValue>[] values = new KeyValuePair<RedisKey, RedisValue>[] { new KeyValuePair<RedisKey, RedisValue>("a", "x"), new KeyValuePair<RedisKey, RedisValue>("b", "y") };
            Expression<Func<KeyValuePair<RedisKey, RedisValue>[], bool>> valid = _ => _.Length == 2 && _[0].Key == "prefix:a" && _[0].Value == "x" && _[1].Key == "prefix:b" && _[1].Value == "y";
            wrapper.StringSetAsync(values, When.Exists, CommandFlags.None);
            mock.Verify(_ => _.StringSetAsync(It.Is(valid), When.Exists, CommandFlags.None));
        }

        [Fact]
        public void StringSetBitAsync()
        {
            wrapper.StringSetBitAsync("key", 123, true, CommandFlags.None);
            mock.Verify(_ => _.StringSetBitAsync("prefix:key", 123, true, CommandFlags.None));
        }

        [Fact]
        public void StringSetRangeAsync()
        {
            wrapper.StringSetRangeAsync("key", 123, "value", CommandFlags.None);
            mock.Verify(_ => _.StringSetRangeAsync("prefix:key", 123, "value", CommandFlags.None));
        }
#pragma warning restore RCS1047 // Non-asynchronous method name should not end with 'Async'.
    }
}
