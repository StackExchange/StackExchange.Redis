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
    [Collection(nameof(MoqDependentCollection))]
    public sealed class KeyPrefixedTests
    {
        private readonly Mock<IDatabaseAsync> mock;
        private readonly KeyPrefixed<IDatabaseAsync> prefixed;

        public KeyPrefixedTests()
        {
            mock = new Mock<IDatabaseAsync>();
            prefixed = new KeyPrefixed<IDatabaseAsync>(mock.Object, Encoding.UTF8.GetBytes("prefix:"));
        }

        [Fact]
        public async Task DebugObjectAsync()
        {
            await prefixed.DebugObjectAsync("key", CommandFlags.None);
            mock.Verify(_ => _.DebugObjectAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HashDecrementAsync_1()
        {
            prefixed.HashDecrementAsync("key", "hashField", 123, CommandFlags.None);
            mock.Verify(_ => _.HashDecrementAsync("prefix:key", "hashField", 123, CommandFlags.None));
        }

        [Fact]
        public void HashDecrementAsync_2()
        {
            prefixed.HashDecrementAsync("key", "hashField", 1.23, CommandFlags.None);
            mock.Verify(_ => _.HashDecrementAsync("prefix:key", "hashField", 1.23, CommandFlags.None));
        }

        [Fact]
        public void HashDeleteAsync_1()
        {
            prefixed.HashDeleteAsync("key", "hashField", CommandFlags.None);
            mock.Verify(_ => _.HashDeleteAsync("prefix:key", "hashField", CommandFlags.None));
        }

        [Fact]
        public void HashDeleteAsync_2()
        {
            RedisValue[] hashFields = Array.Empty<RedisValue>();
            prefixed.HashDeleteAsync("key", hashFields, CommandFlags.None);
            mock.Verify(_ => _.HashDeleteAsync("prefix:key", hashFields, CommandFlags.None));
        }

        [Fact]
        public void HashExistsAsync()
        {
            prefixed.HashExistsAsync("key", "hashField", CommandFlags.None);
            mock.Verify(_ => _.HashExistsAsync("prefix:key", "hashField", CommandFlags.None));
        }

        [Fact]
        public void HashGetAllAsync()
        {
            prefixed.HashGetAllAsync("key", CommandFlags.None);
            mock.Verify(_ => _.HashGetAllAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HashGetAsync_1()
        {
            prefixed.HashGetAsync("key", "hashField", CommandFlags.None);
            mock.Verify(_ => _.HashGetAsync("prefix:key", "hashField", CommandFlags.None));
        }

        [Fact]
        public void HashGetAsync_2()
        {
            RedisValue[] hashFields = Array.Empty<RedisValue>();
            prefixed.HashGetAsync("key", hashFields, CommandFlags.None);
            mock.Verify(_ => _.HashGetAsync("prefix:key", hashFields, CommandFlags.None));
        }

        [Fact]
        public void HashIncrementAsync_1()
        {
            prefixed.HashIncrementAsync("key", "hashField", 123, CommandFlags.None);
            mock.Verify(_ => _.HashIncrementAsync("prefix:key", "hashField", 123, CommandFlags.None));
        }

        [Fact]
        public void HashIncrementAsync_2()
        {
            prefixed.HashIncrementAsync("key", "hashField", 1.23, CommandFlags.None);
            mock.Verify(_ => _.HashIncrementAsync("prefix:key", "hashField", 1.23, CommandFlags.None));
        }

        [Fact]
        public void HashKeysAsync()
        {
            prefixed.HashKeysAsync("key", CommandFlags.None);
            mock.Verify(_ => _.HashKeysAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HashLengthAsync()
        {
            prefixed.HashLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.HashLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HashSetAsync_1()
        {
            HashEntry[] hashFields = Array.Empty<HashEntry>();
            prefixed.HashSetAsync("key", hashFields, CommandFlags.None);
            mock.Verify(_ => _.HashSetAsync("prefix:key", hashFields, CommandFlags.None));
        }

        [Fact]
        public void HashSetAsync_2()
        {
            prefixed.HashSetAsync("key", "hashField", "value", When.Exists, CommandFlags.None);
            mock.Verify(_ => _.HashSetAsync("prefix:key", "hashField", "value", When.Exists, CommandFlags.None));
        }

        [Fact]
        public void HashStringLengthAsync()
        {
            prefixed.HashStringLengthAsync("key","field", CommandFlags.None);
            mock.Verify(_ => _.HashStringLengthAsync("prefix:key", "field", CommandFlags.None));
        }

        [Fact]
        public void HashValuesAsync()
        {
            prefixed.HashValuesAsync("key", CommandFlags.None);
            mock.Verify(_ => _.HashValuesAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HyperLogLogAddAsync_1()
        {
            prefixed.HyperLogLogAddAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.HyperLogLogAddAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void HyperLogLogAddAsync_2()
        {
            var values = Array.Empty<RedisValue>();
            prefixed.HyperLogLogAddAsync("key", values, CommandFlags.None);
            mock.Verify(_ => _.HyperLogLogAddAsync("prefix:key", values, CommandFlags.None));
        }

        [Fact]
        public void HyperLogLogLengthAsync()
        {
            prefixed.HyperLogLogLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.HyperLogLogLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void HyperLogLogMergeAsync_1()
        {
            prefixed.HyperLogLogMergeAsync("destination", "first", "second", CommandFlags.None);
            mock.Verify(_ => _.HyperLogLogMergeAsync("prefix:destination", "prefix:first", "prefix:second", CommandFlags.None));
        }

        [Fact]
        public void HyperLogLogMergeAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            prefixed.HyperLogLogMergeAsync("destination", keys, CommandFlags.None);
            mock.Verify(_ => _.HyperLogLogMergeAsync("prefix:destination", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void IdentifyEndpointAsync()
        {
            prefixed.IdentifyEndpointAsync("key", CommandFlags.None);
            mock.Verify(_ => _.IdentifyEndpointAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void IsConnected()
        {
            prefixed.IsConnected("key", CommandFlags.None);
            mock.Verify(_ => _.IsConnected("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyCopyAsync()
        {
            prefixed.KeyCopyAsync("key", "destination", flags: CommandFlags.None);
            mock.Verify(_ => _.KeyCopyAsync("prefix:key", "prefix:destination", -1, false, CommandFlags.None));
        }

        [Fact]
        public void KeyDeleteAsync_1()
        {
            prefixed.KeyDeleteAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyDeleteAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyDeleteAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            prefixed.KeyDeleteAsync(keys, CommandFlags.None);
            mock.Verify(_ => _.KeyDeleteAsync(It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void KeyDumpAsync()
        {
            prefixed.KeyDumpAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyDumpAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyEncodingAsync()
        {
            prefixed.KeyEncodingAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyEncodingAsync("prefix:key", CommandFlags.None));
        }


        [Fact]
        public void KeyExistsAsync()
        {
            prefixed.KeyExistsAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyExistsAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyExpireAsync_1()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            prefixed.KeyExpireAsync("key", expiry, CommandFlags.None);
            mock.Verify(_ => _.KeyExpireAsync("prefix:key", expiry, CommandFlags.None));
        }

        [Fact]
        public void KeyExpireAsync_2()
        {
            DateTime expiry = DateTime.Now;
            prefixed.KeyExpireAsync("key", expiry, CommandFlags.None);
            mock.Verify(_ => _.KeyExpireAsync("prefix:key", expiry, CommandFlags.None));
        }

        [Fact]
        public void KeyExpireAsync_3()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            prefixed.KeyExpireAsync("key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
            mock.Verify(_ => _.KeyExpireAsync("prefix:key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None));
        }

        [Fact]
        public void KeyExpireAsync_4()
        {
            DateTime expiry = DateTime.Now;
            prefixed.KeyExpireAsync("key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
            mock.Verify(_ => _.KeyExpireAsync("prefix:key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None));
        }

        [Fact]
        public void KeyExpireTimeAsync()
        {
            prefixed.KeyExpireTimeAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyExpireTimeAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyFrequencyAsync()
        {
            prefixed.KeyFrequencyAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyFrequencyAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyMigrateAsync()
        {
            EndPoint toServer = new IPEndPoint(IPAddress.Loopback, 123);
            prefixed.KeyMigrateAsync("key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.None);
            mock.Verify(_ => _.KeyMigrateAsync("prefix:key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.None));
        }

        [Fact]
        public void KeyMoveAsync()
        {
            prefixed.KeyMoveAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.KeyMoveAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void KeyPersistAsync()
        {
            prefixed.KeyPersistAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyPersistAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public Task KeyRandomAsync()
        {
            return Assert.ThrowsAsync<NotSupportedException>(() => prefixed.KeyRandomAsync());
        }

        [Fact]
        public void KeyRefCountAsync()
        {
            prefixed.KeyRefCountAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyRefCountAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyRenameAsync()
        {
            prefixed.KeyRenameAsync("key", "newKey", When.Exists, CommandFlags.None);
            mock.Verify(_ => _.KeyRenameAsync("prefix:key", "prefix:newKey", When.Exists, CommandFlags.None));
        }

        [Fact]
        public void KeyRestoreAsync()
        {
            byte[] value = Array.Empty<byte>();
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            prefixed.KeyRestoreAsync("key", value, expiry, CommandFlags.None);
            mock.Verify(_ => _.KeyRestoreAsync("prefix:key", value, expiry, CommandFlags.None));
        }

        [Fact]
        public void KeyTimeToLiveAsync()
        {
            prefixed.KeyTimeToLiveAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyTimeToLiveAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyTypeAsync()
        {
            prefixed.KeyTypeAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyTypeAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void ListGetByIndexAsync()
        {
            prefixed.ListGetByIndexAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.ListGetByIndexAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void ListInsertAfterAsync()
        {
            prefixed.ListInsertAfterAsync("key", "pivot", "value", CommandFlags.None);
            mock.Verify(_ => _.ListInsertAfterAsync("prefix:key", "pivot", "value", CommandFlags.None));
        }

        [Fact]
        public void ListInsertBeforeAsync()
        {
            prefixed.ListInsertBeforeAsync("key", "pivot", "value", CommandFlags.None);
            mock.Verify(_ => _.ListInsertBeforeAsync("prefix:key", "pivot", "value", CommandFlags.None));
        }

        [Fact]
        public void ListLeftPopAsync()
        {
            prefixed.ListLeftPopAsync("key", CommandFlags.None);
            mock.Verify(_ => _.ListLeftPopAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void ListLeftPopAsync_1()
        {
            prefixed.ListLeftPopAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.ListLeftPopAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void ListLeftPushAsync_1()
        {
            prefixed.ListLeftPushAsync("key", "value", When.Exists, CommandFlags.None);
            mock.Verify(_ => _.ListLeftPushAsync("prefix:key", "value", When.Exists, CommandFlags.None));
        }

        [Fact]
        public void ListLeftPushAsync_2()
        {
            RedisValue[] values = Array.Empty<RedisValue>();
            prefixed.ListLeftPushAsync("key", values, CommandFlags.None);
            mock.Verify(_ => _.ListLeftPushAsync("prefix:key", values, CommandFlags.None));
        }

        [Fact]
        public void ListLeftPushAsync_3()
        {
            RedisValue[] values = new RedisValue[] { "value1", "value2" };
            prefixed.ListLeftPushAsync("key", values, When.Exists, CommandFlags.None);
            mock.Verify(_ => _.ListLeftPushAsync("prefix:key", values, When.Exists, CommandFlags.None));
        }

        [Fact]
        public void ListLengthAsync()
        {
            prefixed.ListLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.ListLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void ListMoveAsync()
        {
            prefixed.ListMoveAsync("key", "destination", ListSide.Left, ListSide.Right, CommandFlags.None);
            mock.Verify(_ => _.ListMoveAsync("prefix:key", "prefix:destination", ListSide.Left, ListSide.Right, CommandFlags.None));
        }

        [Fact]
        public void ListRangeAsync()
        {
            prefixed.ListRangeAsync("key", 123, 456, CommandFlags.None);
            mock.Verify(_ => _.ListRangeAsync("prefix:key", 123, 456, CommandFlags.None));
        }

        [Fact]
        public void ListRemoveAsync()
        {
            prefixed.ListRemoveAsync("key", "value", 123, CommandFlags.None);
            mock.Verify(_ => _.ListRemoveAsync("prefix:key", "value", 123, CommandFlags.None));
        }

        [Fact]
        public void ListRightPopAsync()
        {
            prefixed.ListRightPopAsync("key", CommandFlags.None);
            mock.Verify(_ => _.ListRightPopAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void ListRightPopAsync_1()
        {
            prefixed.ListRightPopAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.ListRightPopAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void ListRightPopLeftPushAsync()
        {
            prefixed.ListRightPopLeftPushAsync("source", "destination", CommandFlags.None);
            mock.Verify(_ => _.ListRightPopLeftPushAsync("prefix:source", "prefix:destination", CommandFlags.None));
        }

        [Fact]
        public void ListRightPushAsync_1()
        {
            prefixed.ListRightPushAsync("key", "value", When.Exists, CommandFlags.None);
            mock.Verify(_ => _.ListRightPushAsync("prefix:key", "value", When.Exists, CommandFlags.None));
        }

        [Fact]
        public void ListRightPushAsync_2()
        {
            RedisValue[] values = Array.Empty<RedisValue>();
            prefixed.ListRightPushAsync("key", values, CommandFlags.None);
            mock.Verify(_ => _.ListRightPushAsync("prefix:key", values, CommandFlags.None));
        }

        [Fact]
        public void ListRightPushAsync_3()
        {
            RedisValue[] values = new RedisValue[] { "value1", "value2" };
            prefixed.ListRightPushAsync("key", values, When.Exists, CommandFlags.None);
            mock.Verify(_ => _.ListRightPushAsync("prefix:key", values, When.Exists, CommandFlags.None));
        }

        [Fact]
        public void ListSetByIndexAsync()
        {
            prefixed.ListSetByIndexAsync("key", 123, "value", CommandFlags.None);
            mock.Verify(_ => _.ListSetByIndexAsync("prefix:key", 123, "value", CommandFlags.None));
        }

        [Fact]
        public void ListTrimAsync()
        {
            prefixed.ListTrimAsync("key", 123, 456, CommandFlags.None);
            mock.Verify(_ => _.ListTrimAsync("prefix:key", 123, 456, CommandFlags.None));
        }

        [Fact]
        public void LockExtendAsync()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            prefixed.LockExtendAsync("key", "value", expiry, CommandFlags.None);
            mock.Verify(_ => _.LockExtendAsync("prefix:key", "value", expiry, CommandFlags.None));
        }

        [Fact]
        public void LockQueryAsync()
        {
            prefixed.LockQueryAsync("key", CommandFlags.None);
            mock.Verify(_ => _.LockQueryAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void LockReleaseAsync()
        {
            prefixed.LockReleaseAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.LockReleaseAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void LockTakeAsync()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            prefixed.LockTakeAsync("key", "value", expiry, CommandFlags.None);
            mock.Verify(_ => _.LockTakeAsync("prefix:key", "value", expiry, CommandFlags.None));
        }

        [Fact]
        public void PublishAsync()
        {
            prefixed.PublishAsync("channel", "message", CommandFlags.None);
            mock.Verify(_ => _.PublishAsync("prefix:channel", "message", CommandFlags.None));
        }

        [Fact]
        public void ScriptEvaluateAsync_1()
        {
            byte[] hash = Array.Empty<byte>();
            RedisValue[] values = Array.Empty<RedisValue>();
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            prefixed.ScriptEvaluateAsync(hash, keys, values, CommandFlags.None);
            mock.Verify(_ => _.ScriptEvaluateAsync(hash, It.Is(valid), values, CommandFlags.None));
        }

        [Fact]
        public void ScriptEvaluateAsync_2()
        {
            RedisValue[] values = Array.Empty<RedisValue>();
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            prefixed.ScriptEvaluateAsync("script", keys, values, CommandFlags.None);
            mock.Verify(_ => _.ScriptEvaluateAsync("script", It.Is(valid), values, CommandFlags.None));
        }

        [Fact]
        public void SetAddAsync_1()
        {
            prefixed.SetAddAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.SetAddAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void SetAddAsync_2()
        {
            RedisValue[] values = Array.Empty<RedisValue>();
            prefixed.SetAddAsync("key", values, CommandFlags.None);
            mock.Verify(_ => _.SetAddAsync("prefix:key", values, CommandFlags.None));
        }

        [Fact]
        public void SetCombineAndStoreAsync_1()
        {
            prefixed.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", "first", "second", CommandFlags.None);
            mock.Verify(_ => _.SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.None));
        }

        [Fact]
        public void SetCombineAndStoreAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            prefixed.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", keys, CommandFlags.None);
            mock.Verify(_ => _.SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void SetCombineAsync_1()
        {
            prefixed.SetCombineAsync(SetOperation.Intersect, "first", "second", CommandFlags.None);
            mock.Verify(_ => _.SetCombineAsync(SetOperation.Intersect, "prefix:first", "prefix:second", CommandFlags.None));
        }

        [Fact]
        public void SetCombineAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            prefixed.SetCombineAsync(SetOperation.Intersect, keys, CommandFlags.None);
            mock.Verify(_ => _.SetCombineAsync(SetOperation.Intersect, It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void SetContainsAsync()
        {
            prefixed.SetContainsAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.SetContainsAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void SetContainsAsync_2()
        {
            RedisValue[] values = new RedisValue[] { "value1", "value2" };
            prefixed.SetContainsAsync("key", values, CommandFlags.None);
            mock.Verify(_ => _.SetContainsAsync("prefix:key", values, CommandFlags.None));
        }

        [Fact]
        public void SetIntersectionLengthAsync()
        {
            var keys = new RedisKey[] { "key1", "key2" };
            prefixed.SetIntersectionLengthAsync(keys);
            mock.Verify(_ => _.SetIntersectionLengthAsync(keys, 0, CommandFlags.None));
        }

        [Fact]
        public void SetLengthAsync()
        {
            prefixed.SetLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.SetLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void SetMembersAsync()
        {
            prefixed.SetMembersAsync("key", CommandFlags.None);
            mock.Verify(_ => _.SetMembersAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void SetMoveAsync()
        {
            prefixed.SetMoveAsync("source", "destination", "value", CommandFlags.None);
            mock.Verify(_ => _.SetMoveAsync("prefix:source", "prefix:destination", "value", CommandFlags.None));
        }

        [Fact]
        public void SetPopAsync_1()
        {
            prefixed.SetPopAsync("key", CommandFlags.None);
            mock.Verify(_ => _.SetPopAsync("prefix:key", CommandFlags.None));

            prefixed.SetPopAsync("key", 5, CommandFlags.None);
            mock.Verify(_ => _.SetPopAsync("prefix:key", 5, CommandFlags.None));
        }

        [Fact]
        public void SetPopAsync_2()
        {
            prefixed.SetPopAsync("key", 5, CommandFlags.None);
            mock.Verify(_ => _.SetPopAsync("prefix:key", 5, CommandFlags.None));
        }

        [Fact]
        public void SetRandomMemberAsync()
        {
            prefixed.SetRandomMemberAsync("key", CommandFlags.None);
            mock.Verify(_ => _.SetRandomMemberAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void SetRandomMembersAsync()
        {
            prefixed.SetRandomMembersAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.SetRandomMembersAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void SetRemoveAsync_1()
        {
            prefixed.SetRemoveAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.SetRemoveAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void SetRemoveAsync_2()
        {
            RedisValue[] values = Array.Empty<RedisValue>();
            prefixed.SetRemoveAsync("key", values, CommandFlags.None);
            mock.Verify(_ => _.SetRemoveAsync("prefix:key", values, CommandFlags.None));
        }

        [Fact]
        public void SortAndStoreAsync()
        {
            RedisValue[] get = new RedisValue[] { "a", "#" };
            Expression<Func<RedisValue[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "#";

            prefixed.SortAndStoreAsync("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.None);
            prefixed.SortAndStoreAsync("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.None);

            mock.Verify(_ => _.SortAndStoreAsync("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", It.Is(valid), CommandFlags.None));
            mock.Verify(_ => _.SortAndStoreAsync("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void SortAsync()
        {
            RedisValue[] get = new RedisValue[] { "a", "#" };
            Expression<Func<RedisValue[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "#";

            prefixed.SortAsync("key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.None);
            prefixed.SortAsync("key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.None);

            mock.Verify(_ => _.SortAsync("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", It.Is(valid), CommandFlags.None));
            mock.Verify(_ => _.SortAsync("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void SortedSetAddAsync_1()
        {
            prefixed.SortedSetAddAsync("key", "member", 1.23, When.Exists, CommandFlags.None);
            mock.Verify(_ => _.SortedSetAddAsync("prefix:key", "member", 1.23, When.Exists, CommandFlags.None));
        }

        [Fact]
        public void SortedSetAddAsync_2()
        {
            SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
            prefixed.SortedSetAddAsync("key", values, When.Exists, CommandFlags.None);
            mock.Verify(_ => _.SortedSetAddAsync("prefix:key", values, When.Exists, CommandFlags.None));
        }

        [Fact]
        public void SortedSetAddAsync_3()
        {
            SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
            prefixed.SortedSetAddAsync("key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
            mock.Verify(_ => _.SortedSetAddAsync("prefix:key", values, SortedSetWhen.GreaterThan, CommandFlags.None));
        }

        [Fact]
        public void SortedSetCombineAsync()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            prefixed.SortedSetCombineAsync(SetOperation.Intersect, keys);
            mock.Verify(_ => _.SortedSetCombineAsync(SetOperation.Intersect, keys, null, Aggregate.Sum, CommandFlags.None));
        }

        [Fact]
        public void SortedSetCombineWithScoresAsync()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            prefixed.SortedSetCombineWithScoresAsync(SetOperation.Intersect, keys);
            mock.Verify(_ => _.SortedSetCombineWithScoresAsync(SetOperation.Intersect, keys, null, Aggregate.Sum, CommandFlags.None));
        }

        [Fact]
        public void SortedSetCombineAndStoreAsync_1()
        {
            prefixed.SortedSetCombineAndStoreAsync(SetOperation.Intersect, "destination", "first", "second", Aggregate.Max, CommandFlags.None);
            mock.Verify(_ => _.SortedSetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", Aggregate.Max, CommandFlags.None));
        }

        [Fact]
        public void SortedSetCombineAndStoreAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            prefixed.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", keys, CommandFlags.None);
            mock.Verify(_ => _.SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void SortedSetDecrementAsync()
        {
            prefixed.SortedSetDecrementAsync("key", "member", 1.23, CommandFlags.None);
            mock.Verify(_ => _.SortedSetDecrementAsync("prefix:key", "member", 1.23, CommandFlags.None));
        }

        [Fact]
        public void SortedSetIncrementAsync()
        {
            prefixed.SortedSetIncrementAsync("key", "member", 1.23, CommandFlags.None);
            mock.Verify(_ => _.SortedSetIncrementAsync("prefix:key", "member", 1.23, CommandFlags.None));
        }

        [Fact]
        public void SortedSetIntersectionLengthAsync()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            prefixed.SortedSetIntersectionLengthAsync(keys, 1, CommandFlags.None);
            mock.Verify(_ => _.SortedSetIntersectionLengthAsync(keys, 1, CommandFlags.None));
        }

        [Fact]
        public void SortedSetLengthAsync()
        {
            prefixed.SortedSetLengthAsync("key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
            mock.Verify(_ => _.SortedSetLengthAsync("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.None));
        }

        [Fact]
        public void SortedSetLengthByValueAsync()
        {
            prefixed.SortedSetLengthByValueAsync("key", "min", "max", Exclude.Start, CommandFlags.None);
            mock.Verify(_ => _.SortedSetLengthByValueAsync("prefix:key", "min", "max", Exclude.Start, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRandomMemberAsync()
        {
            prefixed.SortedSetRandomMemberAsync("key", CommandFlags.None);
            mock.Verify(_ => _.SortedSetRandomMemberAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void SortedSetRandomMembersAsync()
        {
            prefixed.SortedSetRandomMembersAsync("key", 2, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRandomMembersAsync("prefix:key", 2, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRandomMemberWithScoresAsync()
        {
            prefixed.SortedSetRandomMembersWithScoresAsync("key", 2, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRandomMembersWithScoresAsync("prefix:key", 2, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByRankAsync()
        {
            prefixed.SortedSetRangeByRankAsync("key", 123, 456, Order.Descending, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByRankAsync("prefix:key", 123, 456, Order.Descending, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByRankWithScoresAsync()
        {
            prefixed.SortedSetRangeByRankWithScoresAsync("key", 123, 456, Order.Descending, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByRankWithScoresAsync("prefix:key", 123, 456, Order.Descending, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByScoreAsync()
        {
            prefixed.SortedSetRangeByScoreAsync("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByScoreAsync("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByScoreWithScoresAsync()
        {
            prefixed.SortedSetRangeByScoreWithScoresAsync("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByScoreWithScoresAsync("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByValueAsync()
        {
            prefixed.SortedSetRangeByValueAsync("key", "min", "max", Exclude.Start, 123, 456, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByValueAsync("prefix:key", "min", "max", Exclude.Start, Order.Ascending, 123, 456, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRangeByValueDescAsync()
        {
            prefixed.SortedSetRangeByValueAsync("key", "min", "max", Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRangeByValueAsync("prefix:key", "min", "max", Exclude.Start, Order.Descending, 123, 456, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRankAsync()
        {
            prefixed.SortedSetRankAsync("key", "member", Order.Descending, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRankAsync("prefix:key", "member", Order.Descending, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRemoveAsync_1()
        {
            prefixed.SortedSetRemoveAsync("key", "member", CommandFlags.None);
            mock.Verify(_ => _.SortedSetRemoveAsync("prefix:key", "member", CommandFlags.None));
        }

        [Fact]
        public void SortedSetRemoveAsync_2()
        {
            RedisValue[] members = Array.Empty<RedisValue>();
            prefixed.SortedSetRemoveAsync("key", members, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRemoveAsync("prefix:key", members, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRemoveRangeByRankAsync()
        {
            prefixed.SortedSetRemoveRangeByRankAsync("key", 123, 456, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRemoveRangeByRankAsync("prefix:key", 123, 456, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRemoveRangeByScoreAsync()
        {
            prefixed.SortedSetRemoveRangeByScoreAsync("key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRemoveRangeByScoreAsync("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.None));
        }

        [Fact]
        public void SortedSetRemoveRangeByValueAsync()
        {
            prefixed.SortedSetRemoveRangeByValueAsync("key", "min", "max", Exclude.Start, CommandFlags.None);
            mock.Verify(_ => _.SortedSetRemoveRangeByValueAsync("prefix:key", "min", "max", Exclude.Start, CommandFlags.None));
        }

        [Fact]
        public void SortedSetScoreAsync()
        {
            prefixed.SortedSetScoreAsync("key", "member", CommandFlags.None);
            mock.Verify(_ => _.SortedSetScoreAsync("prefix:key", "member", CommandFlags.None));
        }

        [Fact]
        public void SortedSetScoreAsync_Multiple()
        {
            prefixed.SortedSetScoresAsync("key", new RedisValue[] { "member1", "member2" }, CommandFlags.None);
            mock.Verify(_ => _.SortedSetScoresAsync("prefix:key", new RedisValue[] { "member1", "member2" }, CommandFlags.None));
        }

        [Fact]
        public void SortedSetUpdateAsync()
        {
            SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
            prefixed.SortedSetUpdateAsync("key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
            mock.Verify(_ => _.SortedSetUpdateAsync("prefix:key", values, SortedSetWhen.GreaterThan, CommandFlags.None));
        }

        [Fact]
        public void StreamAcknowledgeAsync_1()
        {
            prefixed.StreamAcknowledgeAsync("key", "group", "0-0", CommandFlags.None);
            mock.Verify(_ => _.StreamAcknowledgeAsync("prefix:key", "group", "0-0", CommandFlags.None));
        }

        [Fact]
        public void StreamAcknowledgeAsync_2()
        {
            var messageIds = new RedisValue[] { "0-0", "0-1", "0-2" };
            prefixed.StreamAcknowledgeAsync("key", "group", messageIds, CommandFlags.None);
            mock.Verify(_ => _.StreamAcknowledgeAsync("prefix:key", "group", messageIds, CommandFlags.None));
        }

        [Fact]
        public void StreamAddAsync_1()
        {
            prefixed.StreamAddAsync("key", "field1", "value1", "*", 1000, true, CommandFlags.None);
            mock.Verify(_ => _.StreamAddAsync("prefix:key", "field1", "value1", "*", 1000, true, CommandFlags.None));
        }

        [Fact]
        public void StreamAddAsync_2()
        {
            var fields = Array.Empty<NameValueEntry>();
            prefixed.StreamAddAsync("key", fields, "*", 1000, true, CommandFlags.None);
            mock.Verify(_ => _.StreamAddAsync("prefix:key", fields, "*", 1000, true, CommandFlags.None));
        }

        [Fact]
        public void StreamAutoClaimAsync()
        {
            prefixed.StreamAutoClaimAsync("key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
            mock.Verify(_ => _.StreamAutoClaimAsync("prefix:key", "group", "consumer", 0, "0-0", 100, CommandFlags.None));
        }

        [Fact]
        public void StreamAutoClaimIdsOnlyAsync()
        {
            prefixed.StreamAutoClaimIdsOnlyAsync("key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
            mock.Verify(_ => _.StreamAutoClaimIdsOnlyAsync("prefix:key", "group", "consumer", 0, "0-0", 100, CommandFlags.None));
        }

        [Fact]
        public void StreamClaimMessagesAsync()
        {
            var messageIds = Array.Empty<RedisValue>();
            prefixed.StreamClaimAsync("key", "group", "consumer", 1000, messageIds, CommandFlags.None);
            mock.Verify(_ => _.StreamClaimAsync("prefix:key", "group", "consumer", 1000, messageIds, CommandFlags.None));
        }

        [Fact]
        public void StreamClaimMessagesReturningIdsAsync()
        {
            var messageIds = Array.Empty<RedisValue>();
            prefixed.StreamClaimIdsOnlyAsync("key", "group", "consumer", 1000, messageIds, CommandFlags.None);
            mock.Verify(_ => _.StreamClaimIdsOnlyAsync("prefix:key", "group", "consumer", 1000, messageIds, CommandFlags.None));
        }

        [Fact]
        public void StreamConsumerInfoGetAsync()
        {
            prefixed.StreamConsumerInfoAsync("key", "group", CommandFlags.None);
            mock.Verify(_ => _.StreamConsumerInfoAsync("prefix:key", "group", CommandFlags.None));
        }

        [Fact]
        public void StreamConsumerGroupSetPositionAsync()
        {
            prefixed.StreamConsumerGroupSetPositionAsync("key", "group", StreamPosition.Beginning, CommandFlags.None);
            mock.Verify(_ => _.StreamConsumerGroupSetPositionAsync("prefix:key", "group", StreamPosition.Beginning, CommandFlags.None));
        }

        [Fact]
        public void StreamCreateConsumerGroupAsync()
        {
            prefixed.StreamCreateConsumerGroupAsync("key", "group", "0-0", false, CommandFlags.None);
            mock.Verify(_ => _.StreamCreateConsumerGroupAsync("prefix:key", "group", "0-0", false, CommandFlags.None));
        }

        [Fact]
        public void StreamGroupInfoGetAsync()
        {
            prefixed.StreamGroupInfoAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StreamGroupInfoAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StreamInfoGetAsync()
        {
            prefixed.StreamInfoAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StreamInfoAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StreamLengthAsync()
        {
            prefixed.StreamLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StreamLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StreamMessagesDeleteAsync()
        {
            var messageIds = Array.Empty<RedisValue>();
            prefixed.StreamDeleteAsync("key", messageIds, CommandFlags.None);
            mock.Verify(_ => _.StreamDeleteAsync("prefix:key", messageIds, CommandFlags.None));
        }

        [Fact]
        public void StreamDeleteConsumerAsync()
        {
            prefixed.StreamDeleteConsumerAsync("key", "group", "consumer", CommandFlags.None);
            mock.Verify(_ => _.StreamDeleteConsumerAsync("prefix:key", "group", "consumer", CommandFlags.None));
        }

        [Fact]
        public void StreamDeleteConsumerGroupAsync()
        {
            prefixed.StreamDeleteConsumerGroupAsync("key", "group", CommandFlags.None);
            mock.Verify(_ => _.StreamDeleteConsumerGroupAsync("prefix:key", "group", CommandFlags.None));
        }

        [Fact]
        public void StreamPendingInfoGetAsync()
        {
            prefixed.StreamPendingAsync("key", "group", CommandFlags.None);
            mock.Verify(_ => _.StreamPendingAsync("prefix:key", "group", CommandFlags.None));
        }

        [Fact]
        public void StreamPendingMessageInfoGetAsync()
        {
            prefixed.StreamPendingMessagesAsync("key", "group", 10, RedisValue.Null, "-", "+", CommandFlags.None);
            mock.Verify(_ => _.StreamPendingMessagesAsync("prefix:key", "group", 10, RedisValue.Null, "-", "+", CommandFlags.None));
        }

        [Fact]
        public void StreamRangeAsync()
        {
            prefixed.StreamRangeAsync("key", "-", "+", null, Order.Ascending, CommandFlags.None);
            mock.Verify(_ => _.StreamRangeAsync("prefix:key", "-", "+", null, Order.Ascending, CommandFlags.None));
        }

        [Fact]
        public void StreamReadAsync_1()
        {
            var streamPositions = Array.Empty<StreamPosition>();
            prefixed.StreamReadAsync(streamPositions, null, CommandFlags.None);
            mock.Verify(_ => _.StreamReadAsync(streamPositions, null, CommandFlags.None));
        }

        [Fact]
        public void StreamReadAsync_2()
        {
            prefixed.StreamReadAsync("key", "0-0", null, CommandFlags.None);
            mock.Verify(_ => _.StreamReadAsync("prefix:key", "0-0", null, CommandFlags.None));
        }

        [Fact]
        public void StreamReadGroupAsync_1()
        {
            prefixed.StreamReadGroupAsync("key", "group", "consumer", StreamPosition.Beginning, 10, false, CommandFlags.None);
            mock.Verify(_ => _.StreamReadGroupAsync("prefix:key", "group", "consumer", StreamPosition.Beginning, 10, false, CommandFlags.None));
        }

        [Fact]
        public void StreamStreamReadGroupAsync_2()
        {
            var streamPositions = Array.Empty<StreamPosition>();
            prefixed.StreamReadGroupAsync(streamPositions, "group", "consumer", 10, false, CommandFlags.None);
            mock.Verify(_ => _.StreamReadGroupAsync(streamPositions, "group", "consumer", 10, false, CommandFlags.None));
        }

        [Fact]
        public void StreamTrimAsync()
        {
            prefixed.StreamTrimAsync("key", 1000, true, CommandFlags.None);
            mock.Verify(_ => _.StreamTrimAsync("prefix:key", 1000, true, CommandFlags.None));
        }

        [Fact]
        public void StringAppendAsync()
        {
            prefixed.StringAppendAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.StringAppendAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void StringBitCountAsync()
        {
            prefixed.StringBitCountAsync("key", 123, 456, CommandFlags.None);
            mock.Verify(_ => _.StringBitCountAsync("prefix:key", 123, 456, CommandFlags.None));
        }

        [Fact]
        public void StringBitCountAsync_2()
        {
            prefixed.StringBitCountAsync("key", 123, 456, StringIndexType.Byte, CommandFlags.None);
            mock.Verify(_ => _.StringBitCountAsync("prefix:key", 123, 456, StringIndexType.Byte, CommandFlags.None));
        }

        [Fact]
        public void StringBitOperationAsync_1()
        {
            prefixed.StringBitOperationAsync(Bitwise.Xor, "destination", "first", "second", CommandFlags.None);
            mock.Verify(_ => _.StringBitOperationAsync(Bitwise.Xor, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.None));
        }

        [Fact]
        public void StringBitOperationAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            prefixed.StringBitOperationAsync(Bitwise.Xor, "destination", keys, CommandFlags.None);
            mock.Verify(_ => _.StringBitOperationAsync(Bitwise.Xor, "prefix:destination", It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void StringBitPositionAsync()
        {
            prefixed.StringBitPositionAsync("key", true, 123, 456, CommandFlags.None);
            mock.Verify(_ => _.StringBitPositionAsync("prefix:key", true, 123, 456, CommandFlags.None));
        }

        [Fact]
        public void StringBitPositionAsync_2()
        {
            prefixed.StringBitPositionAsync("key", true, 123, 456, StringIndexType.Byte, CommandFlags.None);
            mock.Verify(_ => _.StringBitPositionAsync("prefix:key", true, 123, 456, StringIndexType.Byte, CommandFlags.None));
        }

        [Fact]
        public void StringDecrementAsync_1()
        {
            prefixed.StringDecrementAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.StringDecrementAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void StringDecrementAsync_2()
        {
            prefixed.StringDecrementAsync("key", 1.23, CommandFlags.None);
            mock.Verify(_ => _.StringDecrementAsync("prefix:key", 1.23, CommandFlags.None));
        }

        [Fact]
        public void StringGetAsync_1()
        {
            prefixed.StringGetAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StringGetAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StringGetAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            prefixed.StringGetAsync(keys, CommandFlags.None);
            mock.Verify(_ => _.StringGetAsync(It.Is(valid), CommandFlags.None));
        }

        [Fact]
        public void StringGetBitAsync()
        {
            prefixed.StringGetBitAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.StringGetBitAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void StringGetRangeAsync()
        {
            prefixed.StringGetRangeAsync("key", 123, 456, CommandFlags.None);
            mock.Verify(_ => _.StringGetRangeAsync("prefix:key", 123, 456, CommandFlags.None));
        }

        [Fact]
        public void StringGetSetAsync()
        {
            prefixed.StringGetSetAsync("key", "value", CommandFlags.None);
            mock.Verify(_ => _.StringGetSetAsync("prefix:key", "value", CommandFlags.None));
        }

        [Fact]
        public void StringGetDeleteAsync()
        {
            prefixed.StringGetDeleteAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StringGetDeleteAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StringGetWithExpiryAsync()
        {
            prefixed.StringGetWithExpiryAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StringGetWithExpiryAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StringIncrementAsync_1()
        {
            prefixed.StringIncrementAsync("key", 123, CommandFlags.None);
            mock.Verify(_ => _.StringIncrementAsync("prefix:key", 123, CommandFlags.None));
        }

        [Fact]
        public void StringIncrementAsync_2()
        {
            prefixed.StringIncrementAsync("key", 1.23, CommandFlags.None);
            mock.Verify(_ => _.StringIncrementAsync("prefix:key", 1.23, CommandFlags.None));
        }

        [Fact]
        public void StringLengthAsync()
        {
            prefixed.StringLengthAsync("key", CommandFlags.None);
            mock.Verify(_ => _.StringLengthAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void StringSetAsync_1()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            prefixed.StringSetAsync("key", "value", expiry, When.Exists, CommandFlags.None);
            mock.Verify(_ => _.StringSetAsync("prefix:key", "value", expiry, When.Exists, CommandFlags.None));
        }

        [Fact]
        public void StringSetAsync_2()
        {
            TimeSpan? expiry = null;
            prefixed.StringSetAsync("key", "value", expiry, true, When.Exists, CommandFlags.None);
            mock.Verify(_ => _.StringSetAsync("prefix:key", "value", expiry, true, When.Exists, CommandFlags.None));
        }

        [Fact]
        public void StringSetAsync_3()
        {
            KeyValuePair<RedisKey, RedisValue>[] values = new KeyValuePair<RedisKey, RedisValue>[] { new KeyValuePair<RedisKey, RedisValue>("a", "x"), new KeyValuePair<RedisKey, RedisValue>("b", "y") };
            Expression<Func<KeyValuePair<RedisKey, RedisValue>[], bool>> valid = _ => _.Length == 2 && _[0].Key == "prefix:a" && _[0].Value == "x" && _[1].Key == "prefix:b" && _[1].Value == "y";
            prefixed.StringSetAsync(values, When.Exists, CommandFlags.None);
            mock.Verify(_ => _.StringSetAsync(It.Is(valid), When.Exists, CommandFlags.None));
        }

        [Fact]
        public void StringSetAsync_Compat()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            prefixed.StringSetAsync("key", "value", expiry, When.Exists);
            mock.Verify(_ => _.StringSetAsync("prefix:key", "value", expiry, When.Exists));
        }

        [Fact]
        public void StringSetBitAsync()
        {
            prefixed.StringSetBitAsync("key", 123, true, CommandFlags.None);
            mock.Verify(_ => _.StringSetBitAsync("prefix:key", 123, true, CommandFlags.None));
        }

        [Fact]
        public void StringSetRangeAsync()
        {
            prefixed.StringSetRangeAsync("key", 123, "value", CommandFlags.None);
            mock.Verify(_ => _.StringSetRangeAsync("prefix:key", 123, "value", CommandFlags.None));
        }

        [Fact]
        public void KeyTouchAsync_1()
        {
            prefixed.KeyTouchAsync("key", CommandFlags.None);
            mock.Verify(_ => _.KeyTouchAsync("prefix:key", CommandFlags.None));
        }

        [Fact]
        public void KeyTouchAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Func<RedisKey[], bool>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            prefixed.KeyTouchAsync(keys, CommandFlags.None);
            mock.Verify(_ => _.KeyTouchAsync(It.Is(valid), CommandFlags.None));
        }
    }
}
