using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net;
using System.Text;
using NSubstitute;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests
{
    [Collection(nameof(SubstituteDependentCollection))]
    public sealed class KeyPrefixedTests
    {
        private readonly IDatabaseAsync mock;
        private readonly KeyPrefixed<IDatabaseAsync> prefixed;

        public KeyPrefixedTests()
        {
            mock = Substitute.For<IDatabaseAsync>();
            prefixed = new KeyPrefixed<IDatabaseAsync>(mock, Encoding.UTF8.GetBytes("prefix:"));
        }

        [Fact]
        public async Task DebugObjectAsync()
        {
            await prefixed.DebugObjectAsync("key", CommandFlags.None);
            await mock.Received().DebugObjectAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task HashDecrementAsync_1()
        {
            await prefixed.HashDecrementAsync("key", "hashField", 123, CommandFlags.None);
            await mock.Received().HashDecrementAsync("prefix:key", "hashField", 123, CommandFlags.None);
        }

        [Fact]
        public async Task HashDecrementAsync_2()
        {
            await prefixed.HashDecrementAsync("key", "hashField", 1.23, CommandFlags.None);
            await mock.Received().HashDecrementAsync("prefix:key", "hashField", 1.23, CommandFlags.None);
        }

        [Fact]
        public async Task HashDeleteAsync_1()
        {
            await prefixed.HashDeleteAsync("key", "hashField", CommandFlags.None);
            await mock.Received().HashDeleteAsync("prefix:key", "hashField", CommandFlags.None);
        }

        [Fact]
        public async Task HashDeleteAsync_2()
        {
            RedisValue[] hashFields = Array.Empty<RedisValue>();
            await prefixed.HashDeleteAsync("key", hashFields, CommandFlags.None);
            await mock.Received().HashDeleteAsync("prefix:key", hashFields, CommandFlags.None);
        }

        [Fact]
        public async Task HashExistsAsync()
        {
            await prefixed.HashExistsAsync("key", "hashField", CommandFlags.None);
            await mock.Received().HashExistsAsync("prefix:key", "hashField", CommandFlags.None);
        }

        [Fact]
        public async Task HashGetAllAsync()
        {
            await prefixed.HashGetAllAsync("key", CommandFlags.None);
            await mock.Received().HashGetAllAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task HashGetAsync_1()
        {
            await prefixed.HashGetAsync("key", "hashField", CommandFlags.None);
            await mock.Received().HashGetAsync("prefix:key", "hashField", CommandFlags.None);
        }

        [Fact]
        public async Task HashGetAsync_2()
        {
            RedisValue[] hashFields = Array.Empty<RedisValue>();
            await prefixed.HashGetAsync("key", hashFields, CommandFlags.None);
            await mock.Received().HashGetAsync("prefix:key", hashFields, CommandFlags.None);
        }

        [Fact]
        public async Task HashIncrementAsync_1()
        {
            await prefixed.HashIncrementAsync("key", "hashField", 123, CommandFlags.None);
            await mock.Received().HashIncrementAsync("prefix:key", "hashField", 123, CommandFlags.None);
        }

        [Fact]
        public async Task HashIncrementAsync_2()
        {
            await prefixed.HashIncrementAsync("key", "hashField", 1.23, CommandFlags.None);
            await mock.Received().HashIncrementAsync("prefix:key", "hashField", 1.23, CommandFlags.None);
        }

        [Fact]
        public async Task HashKeysAsync()
        {
            await prefixed.HashKeysAsync("key", CommandFlags.None);
            await mock.Received().HashKeysAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task HashLengthAsync()
        {
            await prefixed.HashLengthAsync("key", CommandFlags.None);
            await mock.Received().HashLengthAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task HashSetAsync_1()
        {
            HashEntry[] hashFields = Array.Empty<HashEntry>();
            await prefixed.HashSetAsync("key", hashFields, CommandFlags.None);
            await mock.Received().HashSetAsync("prefix:key", hashFields, CommandFlags.None);
        }

        [Fact]
        public async Task HashSetAsync_2()
        {
            await prefixed.HashSetAsync("key", "hashField", "value", When.Exists, CommandFlags.None);
            await mock.Received().HashSetAsync("prefix:key", "hashField", "value", When.Exists, CommandFlags.None);
        }

        [Fact]
        public async Task HashStringLengthAsync()
        {
            await prefixed.HashStringLengthAsync("key","field", CommandFlags.None);
            await mock.Received().HashStringLengthAsync("prefix:key", "field", CommandFlags.None);
        }

        [Fact]
        public async Task HashValuesAsync()
        {
            await prefixed.HashValuesAsync("key", CommandFlags.None);
            await mock.Received().HashValuesAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task HyperLogLogAddAsync_1()
        {
            await prefixed.HyperLogLogAddAsync("key", "value", CommandFlags.None);
            await mock.Received().HyperLogLogAddAsync("prefix:key", "value", CommandFlags.None);
        }

        [Fact]
        public async Task HyperLogLogAddAsync_2()
        {
            var values = Array.Empty<RedisValue>();
            await prefixed.HyperLogLogAddAsync("key", values, CommandFlags.None);
            await mock.Received().HyperLogLogAddAsync("prefix:key", values, CommandFlags.None);
        }

        [Fact]
        public async Task HyperLogLogLengthAsync()
        {
            await prefixed.HyperLogLogLengthAsync("key", CommandFlags.None);
            await mock.Received().HyperLogLogLengthAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task HyperLogLogMergeAsync_1()
        {
            await prefixed.HyperLogLogMergeAsync("destination", "first", "second", CommandFlags.None);
            await mock.Received().HyperLogLogMergeAsync("prefix:destination", "prefix:first", "prefix:second", CommandFlags.None);
        }

        [Fact]
        public async Task HyperLogLogMergeAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Predicate<RedisKey[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            await prefixed.HyperLogLogMergeAsync("destination", keys, CommandFlags.None);
            await mock.Received().HyperLogLogMergeAsync("prefix:destination", Arg.Is(valid), CommandFlags.None);
        }

        [Fact]
        public async Task IdentifyEndpointAsync()
        {
            await prefixed.IdentifyEndpointAsync("key", CommandFlags.None);
            await mock.Received().IdentifyEndpointAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public void IsConnected()
        {
            prefixed.IsConnected("key", CommandFlags.None);
            mock.Received().IsConnected("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task KeyCopyAsync()
        {
            await prefixed.KeyCopyAsync("key", "destination", flags: CommandFlags.None);
            await mock.Received().KeyCopyAsync("prefix:key", "prefix:destination", -1, false, CommandFlags.None);
        }

        [Fact]
        public async Task KeyDeleteAsync_1()
        {
            await prefixed.KeyDeleteAsync("key", CommandFlags.None);
            await mock.Received().KeyDeleteAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task KeyDeleteAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Predicate<RedisKey[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            await prefixed.KeyDeleteAsync(keys, CommandFlags.None);
            await mock.Received().KeyDeleteAsync(Arg.Is(valid), CommandFlags.None);
        }

        [Fact]
        public async Task KeyDumpAsync()
        {
            await prefixed.KeyDumpAsync("key", CommandFlags.None);
            await mock.Received().KeyDumpAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task KeyEncodingAsync()
        {
            await prefixed.KeyEncodingAsync("key", CommandFlags.None);
            await mock.Received().KeyEncodingAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task KeyExistsAsync()
        {
            await prefixed.KeyExistsAsync("key", CommandFlags.None);
            await mock.Received().KeyExistsAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task KeyExpireAsync_1()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            await prefixed.KeyExpireAsync("key", expiry, CommandFlags.None);
            await mock.Received().KeyExpireAsync("prefix:key", expiry, CommandFlags.None);
        }

        [Fact]
        public async Task KeyExpireAsync_2()
        {
            DateTime expiry = DateTime.Now;
            await prefixed.KeyExpireAsync("key", expiry, CommandFlags.None);
            await mock.Received().KeyExpireAsync("prefix:key", expiry, CommandFlags.None);
        }

        [Fact]
        public async Task KeyExpireAsync_3()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            await prefixed.KeyExpireAsync("key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
            await mock.Received().KeyExpireAsync("prefix:key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
        }

        [Fact]
        public async Task KeyExpireAsync_4()
        {
            DateTime expiry = DateTime.Now;
            await prefixed.KeyExpireAsync("key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
            await mock.Received().KeyExpireAsync("prefix:key", expiry, ExpireWhen.HasNoExpiry, CommandFlags.None);
        }

        [Fact]
        public async Task KeyExpireTimeAsync()
        {
            await prefixed.KeyExpireTimeAsync("key", CommandFlags.None);
            await mock.Received().KeyExpireTimeAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task KeyFrequencyAsync()
        {
            await prefixed.KeyFrequencyAsync("key", CommandFlags.None);
            await mock.Received().KeyFrequencyAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task KeyMigrateAsync()
        {
            EndPoint toServer = new IPEndPoint(IPAddress.Loopback, 123);
            await prefixed.KeyMigrateAsync("key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.None);
            await mock.Received().KeyMigrateAsync("prefix:key", toServer, 123, 456, MigrateOptions.Copy, CommandFlags.None);
        }

        [Fact]
        public async Task KeyMoveAsync()
        {
            await prefixed.KeyMoveAsync("key", 123, CommandFlags.None);
            await mock.Received().KeyMoveAsync("prefix:key", 123, CommandFlags.None);
        }

        [Fact]
        public async Task KeyPersistAsync()
        {
            await prefixed.KeyPersistAsync("key", CommandFlags.None);
            await mock.Received().KeyPersistAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public Task KeyRandomAsync()
        {
            return Assert.ThrowsAsync<NotSupportedException>(() => prefixed.KeyRandomAsync());
        }

        [Fact]
        public async Task KeyRefCountAsync()
        {
            await prefixed.KeyRefCountAsync("key", CommandFlags.None);
            await mock.Received().KeyRefCountAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task KeyRenameAsync()
        {
            await prefixed.KeyRenameAsync("key", "newKey", When.Exists, CommandFlags.None);
            await mock.Received().KeyRenameAsync("prefix:key", "prefix:newKey", When.Exists, CommandFlags.None);
        }

        [Fact]
        public async Task KeyRestoreAsync()
        {
            byte[] value = Array.Empty<byte>();
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            await prefixed.KeyRestoreAsync("key", value, expiry, CommandFlags.None);
            await mock.Received().KeyRestoreAsync("prefix:key", value, expiry, CommandFlags.None);
        }

        [Fact]
        public async Task KeyTimeToLiveAsync()
        {
            await prefixed.KeyTimeToLiveAsync("key", CommandFlags.None);
            await mock.Received().KeyTimeToLiveAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task KeyTypeAsync()
        {
            await prefixed.KeyTypeAsync("key", CommandFlags.None);
            await mock.Received().KeyTypeAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task ListGetByIndexAsync()
        {
            await prefixed.ListGetByIndexAsync("key", 123, CommandFlags.None);
            await mock.Received().ListGetByIndexAsync("prefix:key", 123, CommandFlags.None);
        }

        [Fact]
        public async Task ListInsertAfterAsync()
        {
            await prefixed.ListInsertAfterAsync("key", "pivot", "value", CommandFlags.None);
            await mock.Received().ListInsertAfterAsync("prefix:key", "pivot", "value", CommandFlags.None);
        }

        [Fact]
        public async Task ListInsertBeforeAsync()
        {
            await prefixed.ListInsertBeforeAsync("key", "pivot", "value", CommandFlags.None);
            await mock.Received().ListInsertBeforeAsync("prefix:key", "pivot", "value", CommandFlags.None);
        }

        [Fact]
        public async Task ListLeftPopAsync()
        {
            await prefixed.ListLeftPopAsync("key", CommandFlags.None);
            await mock.Received().ListLeftPopAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task ListLeftPopAsync_1()
        {
            await prefixed.ListLeftPopAsync("key", 123, CommandFlags.None);
            await mock.Received().ListLeftPopAsync("prefix:key", 123, CommandFlags.None);
        }

        [Fact]
        public async Task ListLeftPushAsync_1()
        {
            await prefixed.ListLeftPushAsync("key", "value", When.Exists, CommandFlags.None);
            await mock.Received().ListLeftPushAsync("prefix:key", "value", When.Exists, CommandFlags.None);
        }

        [Fact]
        public async Task ListLeftPushAsync_2()
        {
            RedisValue[] values = Array.Empty<RedisValue>();
            await prefixed.ListLeftPushAsync("key", values, CommandFlags.None);
            await mock.Received().ListLeftPushAsync("prefix:key", values, CommandFlags.None);
        }

        [Fact]
        public async Task ListLeftPushAsync_3()
        {
            RedisValue[] values = new RedisValue[] { "value1", "value2" };
            await prefixed.ListLeftPushAsync("key", values, When.Exists, CommandFlags.None);
            await mock.Received().ListLeftPushAsync("prefix:key", values, When.Exists, CommandFlags.None);
        }

        [Fact]
        public async Task ListLengthAsync()
        {
            await prefixed.ListLengthAsync("key", CommandFlags.None);
            await mock.Received().ListLengthAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task ListMoveAsync()
        {
            await prefixed.ListMoveAsync("key", "destination", ListSide.Left, ListSide.Right, CommandFlags.None);
            await mock.Received().ListMoveAsync("prefix:key", "prefix:destination", ListSide.Left, ListSide.Right, CommandFlags.None);
        }

        [Fact]
        public async Task ListRangeAsync()
        {
            await prefixed.ListRangeAsync("key", 123, 456, CommandFlags.None);
            await mock.Received().ListRangeAsync("prefix:key", 123, 456, CommandFlags.None);
        }

        [Fact]
        public async Task ListRemoveAsync()
        {
            await prefixed.ListRemoveAsync("key", "value", 123, CommandFlags.None);
            await mock.Received().ListRemoveAsync("prefix:key", "value", 123, CommandFlags.None);
        }

        [Fact]
        public async Task ListRightPopAsync()
        {
            await prefixed.ListRightPopAsync("key", CommandFlags.None);
            await mock.Received().ListRightPopAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task ListRightPopAsync_1()
        {
            await prefixed.ListRightPopAsync("key", 123, CommandFlags.None);
            await mock.Received().ListRightPopAsync("prefix:key", 123, CommandFlags.None);
        }

        [Fact]
        public async Task ListRightPopLeftPushAsync()
        {
            await prefixed.ListRightPopLeftPushAsync("source", "destination", CommandFlags.None);
            await mock.Received().ListRightPopLeftPushAsync("prefix:source", "prefix:destination", CommandFlags.None);
        }

        [Fact]
        public async Task ListRightPushAsync_1()
        {
            await prefixed.ListRightPushAsync("key", "value", When.Exists, CommandFlags.None);
            await mock.Received().ListRightPushAsync("prefix:key", "value", When.Exists, CommandFlags.None);
        }

        [Fact]
        public async Task ListRightPushAsync_2()
        {
            RedisValue[] values = Array.Empty<RedisValue>();
            await prefixed.ListRightPushAsync("key", values, CommandFlags.None);
            await mock.Received().ListRightPushAsync("prefix:key", values, CommandFlags.None);
        }

        [Fact]
        public async Task ListRightPushAsync_3()
        {
            RedisValue[] values = new RedisValue[] { "value1", "value2" };
            await prefixed.ListRightPushAsync("key", values, When.Exists, CommandFlags.None);
            await mock.Received().ListRightPushAsync("prefix:key", values, When.Exists, CommandFlags.None);
        }

        [Fact]
        public async Task ListSetByIndexAsync()
        {
            await prefixed.ListSetByIndexAsync("key", 123, "value", CommandFlags.None);
            await mock.Received().ListSetByIndexAsync("prefix:key", 123, "value", CommandFlags.None);
        }

        [Fact]
        public async Task ListTrimAsync()
        {
            await prefixed.ListTrimAsync("key", 123, 456, CommandFlags.None);
            await mock.Received().ListTrimAsync("prefix:key", 123, 456, CommandFlags.None);
        }

        [Fact]
        public async Task LockExtendAsync()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            await prefixed.LockExtendAsync("key", "value", expiry, CommandFlags.None);
            await mock.Received().LockExtendAsync("prefix:key", "value", expiry, CommandFlags.None);
        }

        [Fact]
        public async Task LockQueryAsync()
        {
            await prefixed.LockQueryAsync("key", CommandFlags.None);
            await mock.Received().LockQueryAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task LockReleaseAsync()
        {
            await prefixed.LockReleaseAsync("key", "value", CommandFlags.None);
            await mock.Received().LockReleaseAsync("prefix:key", "value", CommandFlags.None);
        }

        [Fact]
        public async Task LockTakeAsync()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            await prefixed.LockTakeAsync("key", "value", expiry, CommandFlags.None);
            await mock.Received().LockTakeAsync("prefix:key", "value", expiry, CommandFlags.None);
        }

        [Fact]
        public async Task PublishAsync()
        {
            await prefixed.PublishAsync(RedisChannel.Literal("channel"), "message", CommandFlags.None);
            await mock.Received().PublishAsync(RedisChannel.Literal("prefix:channel"), "message", CommandFlags.None);
        }

        [Fact]
        public async Task ScriptEvaluateAsync_1()
        {
            byte[] hash = Array.Empty<byte>();
            RedisValue[] values = Array.Empty<RedisValue>();
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Predicate<RedisKey[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            await prefixed.ScriptEvaluateAsync(hash, keys, values, CommandFlags.None);
            await mock.Received().ScriptEvaluateAsync(hash, Arg.Is(valid), values, CommandFlags.None);
        }

        [Fact]
        public async Task ScriptEvaluateAsync_2()
        {
            RedisValue[] values = Array.Empty<RedisValue>();
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Predicate<RedisKey[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            await prefixed.ScriptEvaluateAsync("script", keys, values, CommandFlags.None);
            await mock.Received().ScriptEvaluateAsync("script", Arg.Is(valid), values, CommandFlags.None);
        }

        [Fact]
        public async Task SetAddAsync_1()
        {
            await prefixed.SetAddAsync("key", "value", CommandFlags.None);
            await mock.Received().SetAddAsync("prefix:key", "value", CommandFlags.None);
        }

        [Fact]
        public async Task SetAddAsync_2()
        {
            RedisValue[] values = Array.Empty<RedisValue>();
            await prefixed.SetAddAsync("key", values, CommandFlags.None);
            await mock.Received().SetAddAsync("prefix:key", values, CommandFlags.None);
        }

        [Fact]
        public async Task SetCombineAndStoreAsync_1()
        {
            await prefixed.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", "first", "second", CommandFlags.None);
            await mock.Received().SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.None);
        }

        [Fact]
        public async Task SetCombineAndStoreAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Predicate<RedisKey[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            await prefixed.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", keys, CommandFlags.None);
            await mock.Received().SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", Arg.Is(valid), CommandFlags.None);
        }

        [Fact]
        public async Task SetCombineAsync_1()
        {
            await prefixed.SetCombineAsync(SetOperation.Intersect, "first", "second", CommandFlags.None);
            await mock.Received().SetCombineAsync(SetOperation.Intersect, "prefix:first", "prefix:second", CommandFlags.None);
        }

        [Fact]
        public async Task SetCombineAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Predicate<RedisKey[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            await prefixed.SetCombineAsync(SetOperation.Intersect, keys, CommandFlags.None);
            await mock.Received().SetCombineAsync(SetOperation.Intersect, Arg.Is(valid), CommandFlags.None);
        }

        [Fact]
        public async Task SetContainsAsync()
        {
            await prefixed.SetContainsAsync("key", "value", CommandFlags.None);
            await mock.Received().SetContainsAsync("prefix:key", "value", CommandFlags.None);
        }

        [Fact]
        public async Task SetContainsAsync_2()
        {
            RedisValue[] values = new RedisValue[] { "value1", "value2" };
            await prefixed.SetContainsAsync("key", values, CommandFlags.None);
            await mock.Received().SetContainsAsync("prefix:key", values, CommandFlags.None);
        }

        [Fact]
        public async Task SetIntersectionLengthAsync()
        {
            var keys = new RedisKey[] { "key1", "key2" };
            await prefixed.SetIntersectionLengthAsync(keys);
            await mock.Received().SetIntersectionLengthAsync(keys, 0, CommandFlags.None);
        }

        [Fact]
        public async Task SetLengthAsync()
        {
            await prefixed.SetLengthAsync("key", CommandFlags.None);
            await mock.Received().SetLengthAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task SetMembersAsync()
        {
            await prefixed.SetMembersAsync("key", CommandFlags.None);
            await mock.Received().SetMembersAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task SetMoveAsync()
        {
            await prefixed.SetMoveAsync("source", "destination", "value", CommandFlags.None);
            await mock.Received().SetMoveAsync("prefix:source", "prefix:destination", "value", CommandFlags.None);
        }

        [Fact]
        public async Task SetPopAsync_1()
        {
            await prefixed.SetPopAsync("key", CommandFlags.None);
            await mock.Received().SetPopAsync("prefix:key", CommandFlags.None);

            await prefixed.SetPopAsync("key", 5, CommandFlags.None);
            await mock.Received().SetPopAsync("prefix:key", 5, CommandFlags.None);
        }

        [Fact]
        public async Task SetPopAsync_2()
        {
            await prefixed.SetPopAsync("key", 5, CommandFlags.None);
            await mock.Received().SetPopAsync("prefix:key", 5, CommandFlags.None);
        }

        [Fact]
        public async Task SetRandomMemberAsync()
        {
            await prefixed.SetRandomMemberAsync("key", CommandFlags.None);
            await mock.Received().SetRandomMemberAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task SetRandomMembersAsync()
        {
            await prefixed.SetRandomMembersAsync("key", 123, CommandFlags.None);
            await mock.Received().SetRandomMembersAsync("prefix:key", 123, CommandFlags.None);
        }

        [Fact]
        public async Task SetRemoveAsync_1()
        {
            await prefixed.SetRemoveAsync("key", "value", CommandFlags.None);
            await mock.Received().SetRemoveAsync("prefix:key", "value", CommandFlags.None);
        }

        [Fact]
        public async Task SetRemoveAsync_2()
        {
            RedisValue[] values = Array.Empty<RedisValue>();
            await prefixed.SetRemoveAsync("key", values, CommandFlags.None);
            await mock.Received().SetRemoveAsync("prefix:key", values, CommandFlags.None);
        }

        [Fact]
        public async Task SortAndStoreAsync()
        {
            RedisValue[] get = new RedisValue[] { "a", "#" };
            Expression<Predicate<RedisValue[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "#";

            await prefixed.SortAndStoreAsync("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.None);
            await prefixed.SortAndStoreAsync("destination", "key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.None);

            await mock.Received().SortAndStoreAsync("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", Arg.Is(valid), CommandFlags.None);
            await mock.Received().SortAndStoreAsync("prefix:destination", "prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", Arg.Is(valid), CommandFlags.None);
        }

        [Fact]
        public async Task SortAsync()
        {
            RedisValue[] get = new RedisValue[] { "a", "#" };
            Expression<Predicate<RedisValue[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "#";

            await prefixed.SortAsync("key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", get, CommandFlags.None);
            await prefixed.SortAsync("key", 123, 456, Order.Descending, SortType.Alphabetic, "by", get, CommandFlags.None);

            await mock.Received().SortAsync("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "nosort", Arg.Is(valid), CommandFlags.None);
            await mock.Received().SortAsync("prefix:key", 123, 456, Order.Descending, SortType.Alphabetic, "prefix:by", Arg.Is(valid), CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetAddAsync_1()
        {
            await prefixed.SortedSetAddAsync("key", "member", 1.23, When.Exists, CommandFlags.None);
            await mock.Received().SortedSetAddAsync("prefix:key", "member", 1.23, When.Exists, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetAddAsync_2()
        {
            SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
            await prefixed.SortedSetAddAsync("key", values, When.Exists, CommandFlags.None);
            await mock.Received().SortedSetAddAsync("prefix:key", values, When.Exists, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetAddAsync_3()
        {
            SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
            await prefixed.SortedSetAddAsync("key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
            await mock.Received().SortedSetAddAsync("prefix:key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetCombineAsync()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            await prefixed.SortedSetCombineAsync(SetOperation.Intersect, keys);
            await mock.Received().SortedSetCombineAsync(SetOperation.Intersect, keys, null, Aggregate.Sum, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetCombineWithScoresAsync()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            await prefixed.SortedSetCombineWithScoresAsync(SetOperation.Intersect, keys);
            await mock.Received().SortedSetCombineWithScoresAsync(SetOperation.Intersect, keys, null, Aggregate.Sum, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetCombineAndStoreAsync_1()
        {
            await prefixed.SortedSetCombineAndStoreAsync(SetOperation.Intersect, "destination", "first", "second", Aggregate.Max, CommandFlags.None);
            await mock.Received().SortedSetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", "prefix:first", "prefix:second", Aggregate.Max, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetCombineAndStoreAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Predicate<RedisKey[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            await prefixed.SetCombineAndStoreAsync(SetOperation.Intersect, "destination", keys, CommandFlags.None);
            await mock.Received().SetCombineAndStoreAsync(SetOperation.Intersect, "prefix:destination", Arg.Is(valid), CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetDecrementAsync()
        {
            await prefixed.SortedSetDecrementAsync("key", "member", 1.23, CommandFlags.None);
            await mock.Received().SortedSetDecrementAsync("prefix:key", "member", 1.23, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetIncrementAsync()
        {
            await prefixed.SortedSetIncrementAsync("key", "member", 1.23, CommandFlags.None);
            await mock.Received().SortedSetIncrementAsync("prefix:key", "member", 1.23, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetIntersectionLengthAsync()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            await prefixed.SortedSetIntersectionLengthAsync(keys, 1, CommandFlags.None);
            await mock.Received().SortedSetIntersectionLengthAsync(keys, 1, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetLengthAsync()
        {
            await prefixed.SortedSetLengthAsync("key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
            await mock.Received().SortedSetLengthAsync("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetLengthByValueAsync()
        {
            await prefixed.SortedSetLengthByValueAsync("key", "min", "max", Exclude.Start, CommandFlags.None);
            await mock.Received().SortedSetLengthByValueAsync("prefix:key", "min", "max", Exclude.Start, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRandomMemberAsync()
        {
            await prefixed.SortedSetRandomMemberAsync("key", CommandFlags.None);
            await mock.Received().SortedSetRandomMemberAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRandomMembersAsync()
        {
            await prefixed.SortedSetRandomMembersAsync("key", 2, CommandFlags.None);
            await mock.Received().SortedSetRandomMembersAsync("prefix:key", 2, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRandomMemberWithScoresAsync()
        {
            await prefixed.SortedSetRandomMembersWithScoresAsync("key", 2, CommandFlags.None);
            await mock.Received().SortedSetRandomMembersWithScoresAsync("prefix:key", 2, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRangeByRankAsync()
        {
            await prefixed.SortedSetRangeByRankAsync("key", 123, 456, Order.Descending, CommandFlags.None);
            await mock.Received().SortedSetRangeByRankAsync("prefix:key", 123, 456, Order.Descending, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRangeByRankWithScoresAsync()
        {
            await prefixed.SortedSetRangeByRankWithScoresAsync("key", 123, 456, Order.Descending, CommandFlags.None);
            await mock.Received().SortedSetRangeByRankWithScoresAsync("prefix:key", 123, 456, Order.Descending, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRangeByScoreAsync()
        {
            await prefixed.SortedSetRangeByScoreAsync("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
            await mock.Received().SortedSetRangeByScoreAsync("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRangeByScoreWithScoresAsync()
        {
            await prefixed.SortedSetRangeByScoreWithScoresAsync("key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
            await mock.Received().SortedSetRangeByScoreWithScoresAsync("prefix:key", 1.23, 1.23, Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRangeByValueAsync()
        {
            await prefixed.SortedSetRangeByValueAsync("key", "min", "max", Exclude.Start, 123, 456, CommandFlags.None);
            await mock.Received().SortedSetRangeByValueAsync("prefix:key", "min", "max", Exclude.Start, Order.Ascending, 123, 456, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRangeByValueDescAsync()
        {
            await prefixed.SortedSetRangeByValueAsync("key", "min", "max", Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
            await mock.Received().SortedSetRangeByValueAsync("prefix:key", "min", "max", Exclude.Start, Order.Descending, 123, 456, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRankAsync()
        {
            await prefixed.SortedSetRankAsync("key", "member", Order.Descending, CommandFlags.None);
            await mock.Received().SortedSetRankAsync("prefix:key", "member", Order.Descending, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRemoveAsync_1()
        {
            await prefixed.SortedSetRemoveAsync("key", "member", CommandFlags.None);
            await mock.Received().SortedSetRemoveAsync("prefix:key", "member", CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRemoveAsync_2()
        {
            RedisValue[] members = Array.Empty<RedisValue>();
            await prefixed.SortedSetRemoveAsync("key", members, CommandFlags.None);
            await mock.Received().SortedSetRemoveAsync("prefix:key", members, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRemoveRangeByRankAsync()
        {
            await prefixed.SortedSetRemoveRangeByRankAsync("key", 123, 456, CommandFlags.None);
            await mock.Received().SortedSetRemoveRangeByRankAsync("prefix:key", 123, 456, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRemoveRangeByScoreAsync()
        {
            await prefixed.SortedSetRemoveRangeByScoreAsync("key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
            await mock.Received().SortedSetRemoveRangeByScoreAsync("prefix:key", 1.23, 1.23, Exclude.Start, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetRemoveRangeByValueAsync()
        {
            await prefixed.SortedSetRemoveRangeByValueAsync("key", "min", "max", Exclude.Start, CommandFlags.None);
            await mock.Received().SortedSetRemoveRangeByValueAsync("prefix:key", "min", "max", Exclude.Start, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetScoreAsync()
        {
            await prefixed.SortedSetScoreAsync("key", "member", CommandFlags.None);
            await mock.Received().SortedSetScoreAsync("prefix:key", "member", CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetScoreAsync_Multiple()
        {
            var values = new RedisValue[] { "member1", "member2" };
            await prefixed.SortedSetScoresAsync("key", values, CommandFlags.None);
            await mock.Received().SortedSetScoresAsync("prefix:key", values, CommandFlags.None);
        }

        [Fact]
        public async Task SortedSetUpdateAsync()
        {
            SortedSetEntry[] values = Array.Empty<SortedSetEntry>();
            await prefixed.SortedSetUpdateAsync("key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
            await mock.Received().SortedSetUpdateAsync("prefix:key", values, SortedSetWhen.GreaterThan, CommandFlags.None);
        }

        [Fact]
        public async Task StreamAcknowledgeAsync_1()
        {
            await prefixed.StreamAcknowledgeAsync("key", "group", "0-0", CommandFlags.None);
            await mock.Received().StreamAcknowledgeAsync("prefix:key", "group", "0-0", CommandFlags.None);
        }

        [Fact]
        public async Task StreamAcknowledgeAsync_2()
        {
            var messageIds = new RedisValue[] { "0-0", "0-1", "0-2" };
            await prefixed.StreamAcknowledgeAsync("key", "group", messageIds, CommandFlags.None);
            await mock.Received().StreamAcknowledgeAsync("prefix:key", "group", messageIds, CommandFlags.None);
        }

        [Fact]
        public async Task StreamAddAsync_1()
        {
            await prefixed.StreamAddAsync("key", "field1", "value1", "*", 1000, true, CommandFlags.None);
            await mock.Received().StreamAddAsync("prefix:key", "field1", "value1", "*", 1000, true, CommandFlags.None);
        }

        [Fact]
        public async Task StreamAddAsync_2()
        {
            var fields = Array.Empty<NameValueEntry>();
            await prefixed.StreamAddAsync("key", fields, "*", 1000, true, CommandFlags.None);
            await mock.Received().StreamAddAsync("prefix:key", fields, "*", 1000, true, CommandFlags.None);
        }

        [Fact]
        public async Task StreamAutoClaimAsync()
        {
            await prefixed.StreamAutoClaimAsync("key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
            await mock.Received().StreamAutoClaimAsync("prefix:key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
        }

        [Fact]
        public async Task StreamAutoClaimIdsOnlyAsync()
        {
            await prefixed.StreamAutoClaimIdsOnlyAsync("key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
            await mock.Received().StreamAutoClaimIdsOnlyAsync("prefix:key", "group", "consumer", 0, "0-0", 100, CommandFlags.None);
        }

        [Fact]
        public async Task StreamClaimMessagesAsync()
        {
            var messageIds = Array.Empty<RedisValue>();
            await prefixed.StreamClaimAsync("key", "group", "consumer", 1000, messageIds, CommandFlags.None);
            await mock.Received().StreamClaimAsync("prefix:key", "group", "consumer", 1000, messageIds, CommandFlags.None);
        }

        [Fact]
        public async Task StreamClaimMessagesReturningIdsAsync()
        {
            var messageIds = Array.Empty<RedisValue>();
            await prefixed.StreamClaimIdsOnlyAsync("key", "group", "consumer", 1000, messageIds, CommandFlags.None);
            await mock.Received().StreamClaimIdsOnlyAsync("prefix:key", "group", "consumer", 1000, messageIds, CommandFlags.None);
        }

        [Fact]
        public async Task StreamConsumerInfoGetAsync()
        {
            await prefixed.StreamConsumerInfoAsync("key", "group", CommandFlags.None);
            await mock.Received().StreamConsumerInfoAsync("prefix:key", "group", CommandFlags.None);
        }

        [Fact]
        public async Task StreamConsumerGroupSetPositionAsync()
        {
            await prefixed.StreamConsumerGroupSetPositionAsync("key", "group", StreamPosition.Beginning, CommandFlags.None);
            await mock.Received().StreamConsumerGroupSetPositionAsync("prefix:key", "group", StreamPosition.Beginning, CommandFlags.None);
        }

        [Fact]
        public async Task StreamCreateConsumerGroupAsync()
        {
            await prefixed.StreamCreateConsumerGroupAsync("key", "group", "0-0", false, CommandFlags.None);
            await mock.Received().StreamCreateConsumerGroupAsync("prefix:key", "group", "0-0", false, CommandFlags.None);
        }

        [Fact]
        public async Task StreamGroupInfoGetAsync()
        {
            await prefixed.StreamGroupInfoAsync("key", CommandFlags.None);
            await mock.Received().StreamGroupInfoAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task StreamInfoGetAsync()
        {
            await prefixed.StreamInfoAsync("key", CommandFlags.None);
            await mock.Received().StreamInfoAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task StreamLengthAsync()
        {
            await prefixed.StreamLengthAsync("key", CommandFlags.None);
            await mock.Received().StreamLengthAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task StreamMessagesDeleteAsync()
        {
            var messageIds = Array.Empty<RedisValue>();
            await prefixed.StreamDeleteAsync("key", messageIds, CommandFlags.None);
            await mock.Received().StreamDeleteAsync("prefix:key", messageIds, CommandFlags.None);
        }

        [Fact]
        public async Task StreamDeleteConsumerAsync()
        {
            await prefixed.StreamDeleteConsumerAsync("key", "group", "consumer", CommandFlags.None);
            await mock.Received().StreamDeleteConsumerAsync("prefix:key", "group", "consumer", CommandFlags.None);
        }

        [Fact]
        public async Task StreamDeleteConsumerGroupAsync()
        {
            await prefixed.StreamDeleteConsumerGroupAsync("key", "group", CommandFlags.None);
            await mock.Received().StreamDeleteConsumerGroupAsync("prefix:key", "group", CommandFlags.None);
        }

        [Fact]
        public async Task StreamPendingInfoGetAsync()
        {
            await prefixed.StreamPendingAsync("key", "group", CommandFlags.None);
            await mock.Received().StreamPendingAsync("prefix:key", "group", CommandFlags.None);
        }

        [Fact]
        public async Task StreamPendingMessageInfoGetAsync()
        {
            await prefixed.StreamPendingMessagesAsync("key", "group", 10, RedisValue.Null, "-", "+", CommandFlags.None);
            await mock.Received().StreamPendingMessagesAsync("prefix:key", "group", 10, RedisValue.Null, "-", "+", CommandFlags.None);
        }

        [Fact]
        public async Task StreamRangeAsync()
        {
            await prefixed.StreamRangeAsync("key", "-", "+", null, Order.Ascending, CommandFlags.None);
            await mock.Received().StreamRangeAsync("prefix:key", "-", "+", null, Order.Ascending, CommandFlags.None);
        }

        [Fact]
        public async Task StreamReadAsync_1()
        {
            var streamPositions = Array.Empty<StreamPosition>();
            await prefixed.StreamReadAsync(streamPositions, null, CommandFlags.None);
            await mock.Received().StreamReadAsync(streamPositions, null, CommandFlags.None);
        }

        [Fact]
        public async Task StreamReadAsync_2()
        {
            await prefixed.StreamReadAsync("key", "0-0", null, CommandFlags.None);
            await mock.Received().StreamReadAsync("prefix:key", "0-0", null, CommandFlags.None);
        }

        [Fact]
        public async Task StreamReadGroupAsync_1()
        {
            await prefixed.StreamReadGroupAsync("key", "group", "consumer", StreamPosition.Beginning, 10, false, CommandFlags.None);
            await mock.Received().StreamReadGroupAsync("prefix:key", "group", "consumer", StreamPosition.Beginning, 10, false, CommandFlags.None);
        }

        [Fact]
        public async Task StreamStreamReadGroupAsync_2()
        {
            var streamPositions = Array.Empty<StreamPosition>();
            await prefixed.StreamReadGroupAsync(streamPositions, "group", "consumer", 10, false, CommandFlags.None);
            await mock.Received().StreamReadGroupAsync(streamPositions, "group", "consumer", 10, false, CommandFlags.None);
        }

        [Fact]
        public async Task StreamTrimAsync()
        {
            await prefixed.StreamTrimAsync("key", 1000, true, CommandFlags.None);
            await mock.Received().StreamTrimAsync("prefix:key", 1000, true, CommandFlags.None);
        }

        [Fact]
        public async Task StringAppendAsync()
        {
            await prefixed.StringAppendAsync("key", "value", CommandFlags.None);
            await mock.Received().StringAppendAsync("prefix:key", "value", CommandFlags.None);
        }

        [Fact]
        public async Task StringBitCountAsync()
        {
            await prefixed.StringBitCountAsync("key", 123, 456, CommandFlags.None);
            await mock.Received().StringBitCountAsync("prefix:key", 123, 456, CommandFlags.None);
        }

        [Fact]
        public async Task StringBitCountAsync_2()
        {
            await prefixed.StringBitCountAsync("key", 123, 456, StringIndexType.Byte, CommandFlags.None);
            await mock.Received().StringBitCountAsync("prefix:key", 123, 456, StringIndexType.Byte, CommandFlags.None);
        }

        [Fact]
        public async Task StringBitOperationAsync_1()
        {
            await prefixed.StringBitOperationAsync(Bitwise.Xor, "destination", "first", "second", CommandFlags.None);
            await mock.Received().StringBitOperationAsync(Bitwise.Xor, "prefix:destination", "prefix:first", "prefix:second", CommandFlags.None);
        }

        [Fact]
        public async Task StringBitOperationAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Predicate<RedisKey[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            await prefixed.StringBitOperationAsync(Bitwise.Xor, "destination", keys, CommandFlags.None);
            await mock.Received().StringBitOperationAsync(Bitwise.Xor, "prefix:destination", Arg.Is(valid), CommandFlags.None);
        }

        [Fact]
        public async Task StringBitPositionAsync()
        {
            await prefixed.StringBitPositionAsync("key", true, 123, 456, CommandFlags.None);
            await mock.Received().StringBitPositionAsync("prefix:key", true, 123, 456, CommandFlags.None);
        }

        [Fact]
        public async Task StringBitPositionAsync_2()
        {
            await prefixed.StringBitPositionAsync("key", true, 123, 456, StringIndexType.Byte, CommandFlags.None);
            await mock.Received().StringBitPositionAsync("prefix:key", true, 123, 456, StringIndexType.Byte, CommandFlags.None);
        }

        [Fact]
        public async Task StringDecrementAsync_1()
        {
            await prefixed.StringDecrementAsync("key", 123, CommandFlags.None);
            await mock.Received().StringDecrementAsync("prefix:key", 123, CommandFlags.None);
        }

        [Fact]
        public async Task StringDecrementAsync_2()
        {
            await prefixed.StringDecrementAsync("key", 1.23, CommandFlags.None);
            await mock.Received().StringDecrementAsync("prefix:key", 1.23, CommandFlags.None);
        }

        [Fact]
        public async Task StringGetAsync_1()
        {
            await prefixed.StringGetAsync("key", CommandFlags.None);
            await mock.Received().StringGetAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task StringGetAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Predicate<RedisKey[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            await prefixed.StringGetAsync(keys, CommandFlags.None);
            await mock.Received().StringGetAsync(Arg.Is(valid), CommandFlags.None);
        }

        [Fact]
        public async Task StringGetBitAsync()
        {
            await prefixed.StringGetBitAsync("key", 123, CommandFlags.None);
            await mock.Received().StringGetBitAsync("prefix:key", 123, CommandFlags.None);
        }

        [Fact]
        public async Task StringGetRangeAsync()
        {
            await prefixed.StringGetRangeAsync("key", 123, 456, CommandFlags.None);
            await mock.Received().StringGetRangeAsync("prefix:key", 123, 456, CommandFlags.None);
        }

        [Fact]
        public async Task StringGetSetAsync()
        {
            await prefixed.StringGetSetAsync("key", "value", CommandFlags.None);
            await mock.Received().StringGetSetAsync("prefix:key", "value", CommandFlags.None);
        }

        [Fact]
        public async Task StringGetDeleteAsync()
        {
            await prefixed.StringGetDeleteAsync("key", CommandFlags.None);
            await mock.Received().StringGetDeleteAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task StringGetWithExpiryAsync()
        {
            await prefixed.StringGetWithExpiryAsync("key", CommandFlags.None);
            await mock.Received().StringGetWithExpiryAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task StringIncrementAsync_1()
        {
            await prefixed.StringIncrementAsync("key", 123, CommandFlags.None);
            await mock.Received().StringIncrementAsync("prefix:key", 123, CommandFlags.None);
        }

        [Fact]
        public async Task StringIncrementAsync_2()
        {
            await prefixed.StringIncrementAsync("key", 1.23, CommandFlags.None);
            await mock.Received().StringIncrementAsync("prefix:key", 1.23, CommandFlags.None);
        }

        [Fact]
        public async Task StringLengthAsync()
        {
            await prefixed.StringLengthAsync("key", CommandFlags.None);
            await mock.Received().StringLengthAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task StringSetAsync_1()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            await prefixed.StringSetAsync("key", "value", expiry, When.Exists, CommandFlags.None);
            await mock.Received().StringSetAsync("prefix:key", "value", expiry, When.Exists, CommandFlags.None);
        }

        [Fact]
        public async Task StringSetAsync_2()
        {
            TimeSpan? expiry = null;
            await prefixed.StringSetAsync("key", "value", expiry, true, When.Exists, CommandFlags.None);
            await mock.Received().StringSetAsync("prefix:key", "value", expiry, true, When.Exists, CommandFlags.None);
        }

        [Fact]
        public async Task StringSetAsync_3()
        {
            KeyValuePair<RedisKey, RedisValue>[] values = new KeyValuePair<RedisKey, RedisValue>[] { new KeyValuePair<RedisKey, RedisValue>("a", "x"), new KeyValuePair<RedisKey, RedisValue>("b", "y") };
            Expression<Predicate<KeyValuePair<RedisKey, RedisValue>[]>> valid = _ => _.Length == 2 && _[0].Key == "prefix:a" && _[0].Value == "x" && _[1].Key == "prefix:b" && _[1].Value == "y";
            await prefixed.StringSetAsync(values, When.Exists, CommandFlags.None);
            await mock.Received().StringSetAsync(Arg.Is(valid), When.Exists, CommandFlags.None);
        }

        [Fact]
        public async Task StringSetAsync_Compat()
        {
            TimeSpan expiry = TimeSpan.FromSeconds(123);
            await prefixed.StringSetAsync("key", "value", expiry, When.Exists);
            await mock.Received().StringSetAsync("prefix:key", "value", expiry, When.Exists);
        }

        [Fact]
        public async Task StringSetBitAsync()
        {
            await prefixed.StringSetBitAsync("key", 123, true, CommandFlags.None);
            await mock.Received().StringSetBitAsync("prefix:key", 123, true, CommandFlags.None);
        }

        [Fact]
        public async Task StringSetRangeAsync()
        {
            await prefixed.StringSetRangeAsync("key", 123, "value", CommandFlags.None);
            await mock.Received().StringSetRangeAsync("prefix:key", 123, "value", CommandFlags.None);
        }

        [Fact]
        public async Task KeyTouchAsync_1()
        {
            await prefixed.KeyTouchAsync("key", CommandFlags.None);
            await mock.Received().KeyTouchAsync("prefix:key", CommandFlags.None);
        }

        [Fact]
        public async Task KeyTouchAsync_2()
        {
            RedisKey[] keys = new RedisKey[] { "a", "b" };
            Expression<Predicate<RedisKey[]>> valid = _ => _.Length == 2 && _[0] == "prefix:a" && _[1] == "prefix:b";
            await prefixed.KeyTouchAsync(keys, CommandFlags.None);
            await mock.Received().KeyTouchAsync(Arg.Is(valid), CommandFlags.None);
        }
    }
}
