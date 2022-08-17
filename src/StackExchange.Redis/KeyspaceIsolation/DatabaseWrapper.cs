using System;
using System.Collections.Generic;
using System.Net;

namespace StackExchange.Redis.KeyspaceIsolation
{
    internal sealed class DatabaseWrapper : WrapperBase<IDatabase>, IDatabase
    {
        public DatabaseWrapper(IDatabase inner, byte[] prefix) : base(inner, prefix)
        {
        }

        public IBatch CreateBatch(object? asyncState = null) =>
            new BatchWrapper(Inner.CreateBatch(asyncState), Prefix);

        public ITransaction CreateTransaction(object? asyncState = null) =>
            new TransactionWrapper(Inner.CreateTransaction(asyncState), Prefix);

        public int Database => Inner.Database;

        public RedisValue DebugObject(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.DebugObject(ToInner(key), flags);

        public bool GeoAdd(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoAdd(ToInner(key), longitude, latitude, member, flags);

        public long GeoAdd(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoAdd(ToInner(key), values, flags);

        public bool GeoAdd(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoAdd(ToInner(key), value, flags);

        public bool GeoRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoRemove(ToInner(key), member, flags);

        public double? GeoDistance(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit = GeoUnit.Meters,CommandFlags flags = CommandFlags.None) =>
            Inner.GeoDistance(ToInner(key), member1, member2, unit, flags);

        public string?[] GeoHash(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoHash(ToInner(key), members, flags);

        public string? GeoHash(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoHash(ToInner(key), member, flags);

        public GeoPosition?[] GeoPosition(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoPosition(ToInner(key), members, flags);

        public GeoPosition? GeoPosition(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoPosition(ToInner(key), member, flags);

        public GeoRadiusResult[] GeoRadius(RedisKey key, RedisValue member, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null,GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoRadius(ToInner(key), member, radius, unit, count, order, options, flags);

        public GeoRadiusResult[] GeoRadius(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoRadius(ToInner(key), longitude, latitude, radius, unit, count, order, options, flags);

        public GeoRadiusResult[] GeoSearch(RedisKey key, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoSearch(ToInner(key), member, shape, count, demandClosest, order, options, flags);

        public GeoRadiusResult[] GeoSearch(RedisKey key, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoSearch(ToInner(key), longitude, latitude, shape, count, demandClosest, order, options, flags);

        public long GeoSearchAndStore(RedisKey sourceKey, RedisKey destinationKey, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoSearchAndStore(ToInner(sourceKey), ToInner(destinationKey), member, shape, count, demandClosest, order, storeDistances, flags);

        public long GeoSearchAndStore(RedisKey sourceKey, RedisKey destinationKey, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None) =>
            Inner.GeoSearchAndStore(ToInner(sourceKey), ToInner(destinationKey), longitude, latitude, shape, count, demandClosest, order, storeDistances, flags);

        public double HashDecrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None) =>
            Inner.HashDecrement(ToInner(key), hashField, value, flags);

        public long HashDecrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None) =>
            Inner.HashDecrement(ToInner(key), hashField, value, flags);

        public long HashDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None) =>
            Inner.HashDelete(ToInner(key), hashFields, flags);

        public bool HashDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) =>
            Inner.HashDelete(ToInner(key), hashField, flags);

        public bool HashExists(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) =>
            Inner.HashExists(ToInner(key), hashField, flags);

        public HashEntry[] HashGetAll(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.HashGetAll(ToInner(key), flags);

        public RedisValue[] HashGet(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None) =>
            Inner.HashGet(ToInner(key), hashFields, flags);

        public RedisValue HashGet(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) =>
            Inner.HashGet(ToInner(key), hashField, flags);

        public Lease<byte>? HashGetLease(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) =>
            Inner.HashGetLease(ToInner(key), hashField, flags);

        public double HashIncrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None) =>
            Inner.HashIncrement(ToInner(key), hashField, value, flags);

        public long HashIncrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None) =>
            Inner.HashIncrement(ToInner(key), hashField, value, flags);

        public RedisValue[] HashKeys(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.HashKeys(ToInner(key), flags);

        public long HashLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.HashLength(ToInner(key), flags);

        public RedisValue HashRandomField(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.HashRandomField(ToInner(key), flags);

        public RedisValue[] HashRandomFields(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
            Inner.HashRandomFields(ToInner(key), count, flags);

        public HashEntry[] HashRandomFieldsWithValues(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
            Inner.HashRandomFieldsWithValues(ToInner(key), count, flags);

        public bool HashSet(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.HashSet(ToInner(key), hashField, value, when, flags);

        public long HashStringLength(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None) =>
            Inner.HashStringLength(ToInner(key), hashField, flags);

        public void HashSet(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None) =>
            Inner.HashSet(ToInner(key), hashFields, flags);

        public RedisValue[] HashValues(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.HashValues(ToInner(key), flags);

        public bool HyperLogLogAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
            Inner.HyperLogLogAdd(ToInner(key), values, flags);

        public bool HyperLogLogAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.HyperLogLogAdd(ToInner(key), value, flags);

        public long HyperLogLogLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.HyperLogLogLength(ToInner(key), flags);

        public long HyperLogLogLength(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
            Inner.HyperLogLogLength(ToInner(keys), flags);

        public void HyperLogLogMerge(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None) =>
            Inner.HyperLogLogMerge(ToInner(destination), ToInner(sourceKeys), flags);

        public void HyperLogLogMerge(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) =>
            Inner.HyperLogLogMerge(ToInner(destination), ToInner(first), ToInner(second), flags);

        public EndPoint? IdentifyEndpoint(RedisKey key = default, CommandFlags flags = CommandFlags.None) =>
            Inner.IdentifyEndpoint(ToInner(key), flags);

        public bool KeyCopy(RedisKey sourceKey, RedisKey destinationKey, int destinationDatabase = -1, bool replace = false, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyCopy(ToInner(sourceKey), ToInner(destinationKey), destinationDatabase, replace, flags);

        public long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyDelete(ToInner(keys), flags);

        public bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyDelete(ToInner(key), flags);

        public byte[]? KeyDump(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyDump(ToInner(key), flags);

        public string? KeyEncoding(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyEncoding(ToInner(key), flags);

        public bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyExists(ToInner(key), flags);
        public long KeyExists(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyExists(ToInner(keys), flags);

        public bool KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags) =>
            Inner.KeyExpire(ToInner(key), expiry, flags);

        public bool KeyExpire(RedisKey key, DateTime? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyExpire(ToInner(key), expiry, when, flags);

        public bool KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags) =>
            Inner.KeyExpire(ToInner(key), expiry, flags);

        public bool KeyExpire(RedisKey key, TimeSpan? expiry, ExpireWhen when = ExpireWhen.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyExpire(ToInner(key), expiry, when, flags);

        public DateTime? KeyExpireTime(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyExpireTime(ToInner(key), flags);

        public long? KeyFrequency(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyFrequency(ToInner(key), flags);

        public TimeSpan? KeyIdleTime(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyIdleTime(ToInner(key), flags);

        public void KeyMigrate(RedisKey key, EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyMigrate(ToInner(key), toServer, toDatabase, timeoutMilliseconds, migrateOptions, flags);

        public bool KeyMove(RedisKey key, int database, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyMove(ToInner(key), database, flags);

        public bool KeyPersist(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyPersist(ToInner(key), flags);

        public RedisKey KeyRandom(CommandFlags flags = CommandFlags.None) =>
            throw new NotSupportedException("RANDOMKEY is not supported when a key-prefix is specified");

        public long? KeyRefCount(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyRefCount(ToInner(key), flags);

        public bool KeyRename(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyRename(ToInner(key), ToInner(newKey), when, flags);

        public void KeyRestore(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyRestore(ToInner(key), value, expiry, flags);

        public TimeSpan? KeyTimeToLive(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyTimeToLive(ToInner(key), flags);

        public RedisType KeyType(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyType(ToInner(key), flags);

        public RedisValue ListGetByIndex(RedisKey key, long index, CommandFlags flags = CommandFlags.None) =>
            Inner.ListGetByIndex(ToInner(key), index, flags);

        public long ListInsertAfter(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.ListInsertAfter(ToInner(key), pivot, value, flags);

        public long ListInsertBefore(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.ListInsertBefore(ToInner(key), pivot, value, flags);

        public RedisValue ListLeftPop(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.ListLeftPop(ToInner(key), flags);

        public RedisValue[] ListLeftPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
            Inner.ListLeftPop(ToInner(key), count, flags);

        public ListPopResult ListLeftPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None) =>
            Inner.ListLeftPop(ToInner(keys), count, flags);

        public long ListPosition(RedisKey key, RedisValue element, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None) =>
            Inner.ListPosition(ToInner(key), element, rank, maxLength, flags);

        public long[] ListPositions(RedisKey key, RedisValue element, long count, long rank = 1, long maxLength = 0, CommandFlags flags = CommandFlags.None) =>
            Inner.ListPositions(ToInner(key), element, count, rank, maxLength, flags);

        public long ListLeftPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
            Inner.ListLeftPush(ToInner(key), values, flags);

        public long ListLeftPush(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.ListLeftPush(ToInner(key), values, when, flags);

        public long ListLeftPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.ListLeftPush(ToInner(key), value, when, flags);

        public long ListLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.ListLength(ToInner(key), flags);

        public RedisValue ListMove(RedisKey sourceKey, RedisKey destinationKey, ListSide sourceSide, ListSide destinationSide, CommandFlags flags = CommandFlags.None) =>
            Inner.ListMove(ToInner(sourceKey), ToInner(destinationKey), sourceSide, destinationSide);

        public RedisValue[] ListRange(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None) =>
            Inner.ListRange(ToInner(key), start, stop, flags);

        public long ListRemove(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None) =>
            Inner.ListRemove(ToInner(key), value, count, flags);

        public RedisValue ListRightPop(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.ListRightPop(ToInner(key), flags);

        public RedisValue[] ListRightPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
            Inner.ListRightPop(ToInner(key), count, flags);

        public ListPopResult ListRightPop(RedisKey[] keys, long count, CommandFlags flags = CommandFlags.None) =>
            Inner.ListRightPop(ToInner(keys), count, flags);

        public RedisValue ListRightPopLeftPush(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None) =>
            Inner.ListRightPopLeftPush(ToInner(source), ToInner(destination), flags);

        public long ListRightPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
            Inner.ListRightPush(ToInner(key), values, flags);

        public long ListRightPush(RedisKey key, RedisValue[] values, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.ListRightPush(ToInner(key), values, when, flags);

        public long ListRightPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.ListRightPush(ToInner(key), value, when, flags);

        public void ListSetByIndex(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.ListSetByIndex(ToInner(key), index, value, flags);

        public void ListTrim(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None) =>
            Inner.ListTrim(ToInner(key), start, stop, flags);

        public bool LockExtend(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None) =>
            Inner.LockExtend(ToInner(key), value, expiry, flags);

        public RedisValue LockQuery(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.LockQuery(ToInner(key), flags);

        public bool LockRelease(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.LockRelease(ToInner(key), value, flags);

        public bool LockTake(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None) =>
            Inner.LockTake(ToInner(key), value, expiry, flags);

        public string? StringLongestCommonSubsequence(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) =>
            Inner.StringLongestCommonSubsequence(ToInner(first), ToInner(second), flags);

        public long StringLongestCommonSubsequenceLength(RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) =>
            Inner.StringLongestCommonSubsequenceLength(ToInner(first), ToInner(second), flags);

        public LCSMatchResult StringLongestCommonSubsequenceWithMatches(RedisKey first, RedisKey second, long minLength = 0, CommandFlags flags = CommandFlags.None) =>
            Inner.StringLongestCommonSubsequenceWithMatches(ToInner(first), ToInner(second), minLength, flags);

        public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) =>
            Inner.Publish(ToInner(channel), message, flags);

        public RedisResult Execute(string command, params object[] args)
            => Inner.Execute(command, ToInner(args), CommandFlags.None);

        public RedisResult Execute(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None)
            => Inner.Execute(command, ToInner(args), flags);

        public RedisResult ScriptEvaluate(byte[] hash, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None) =>
            // TODO: The return value could contain prefixed keys. It might make sense to 'unprefix' those?
            Inner.ScriptEvaluate(hash, ToInner(keys), values, flags);

        public RedisResult ScriptEvaluate(string script, RedisKey[]? keys = null, RedisValue[]? values = null, CommandFlags flags = CommandFlags.None) =>
            // TODO: The return value could contain prefixed keys. It might make sense to 'unprefix' those?
            Inner.ScriptEvaluate(script, ToInner(keys), values, flags);

        public RedisResult ScriptEvaluate(LuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None) =>
            // TODO: The return value could contain prefixed keys. It might make sense to 'unprefix' those?
            script.Evaluate(Inner, parameters, Prefix, flags);

        public RedisResult ScriptEvaluate(LoadedLuaScript script, object? parameters = null, CommandFlags flags = CommandFlags.None) =>
            // TODO: The return value could contain prefixed keys. It might make sense to 'unprefix' those?
            script.Evaluate(Inner, parameters, Prefix, flags);

        public long SetAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
            Inner.SetAdd(ToInner(key), values, flags);

        public bool SetAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.SetAdd(ToInner(key), value, flags);

        public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
            Inner.SetCombineAndStore(operation, ToInner(destination), ToInner(keys), flags);

        public long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) =>
            Inner.SetCombineAndStore(operation, ToInner(destination), ToInner(first), ToInner(second), flags);

        public RedisValue[] SetCombine(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
            Inner.SetCombine(operation, ToInner(keys), flags);

        public RedisValue[] SetCombine(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None) =>
            Inner.SetCombine(operation, ToInner(first), ToInner(second), flags);

        public bool SetContains(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.SetContains(ToInner(key), value, flags);

        public bool[] SetContains(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
            Inner.SetContains(ToInner(key), values, flags);

        public long SetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None) =>
            Inner.SetIntersectionLength(keys, limit, flags);

        public long SetLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.SetLength(ToInner(key), flags);

        public RedisValue[] SetMembers(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.SetMembers(ToInner(key), flags);

        public bool SetMove(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.SetMove(ToInner(source), ToInner(destination), value, flags);

        public RedisValue SetPop(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.SetPop(ToInner(key), flags);

        public RedisValue[] SetPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
            Inner.SetPop(ToInner(key), count, flags);

        public RedisValue SetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.SetRandomMember(ToInner(key), flags);

        public RedisValue[] SetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
            Inner.SetRandomMembers(ToInner(key), count, flags);

        public long SetRemove(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None) =>
            Inner.SetRemove(ToInner(key), values, flags);

        public bool SetRemove(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.SetRemove(ToInner(key), value, flags);

        public long SortAndStore(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None) =>
            Inner.SortAndStore(ToInner(destination), ToInner(key), skip, take, order, sortType, SortByToInner(by), SortGetToInner(get), flags);

        public RedisValue[] Sort(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default, RedisValue[]? get = null, CommandFlags flags = CommandFlags.None) =>
            Inner.Sort(ToInner(key), skip, take, order, sortType, SortByToInner(by), SortGetToInner(get), flags);

        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, CommandFlags flags) =>
            Inner.SortedSetAdd(ToInner(key), values, flags);

        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetAdd(ToInner(key), values, when, flags);

        public long SortedSetAdd(RedisKey key, SortedSetEntry[] values,SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetAdd(ToInner(key), values, when, flags);

        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, CommandFlags flags) =>
            Inner.SortedSetAdd(ToInner(key), member, score, flags);

        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetAdd(ToInner(key), member, score, when, flags);

        public bool SortedSetAdd(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetAdd(ToInner(key), member, score, when, flags);

        public RedisValue[] SortedSetCombine(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetCombine(operation, keys, weights, aggregate, flags);

        public SortedSetEntry[] SortedSetCombineWithScores(SetOperation operation, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetCombineWithScores(operation, keys, weights, aggregate, flags);

        public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, double[]? weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetCombineAndStore(operation, ToInner(destination), ToInner(keys), weights, aggregate, flags);

        public long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetCombineAndStore(operation, ToInner(destination), ToInner(first), ToInner(second), aggregate, flags);

        public double SortedSetDecrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetDecrement(ToInner(key), member, value, flags);

        public double SortedSetIncrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetIncrement(ToInner(key), member, value, flags);

        public long SortedSetIntersectionLength(RedisKey[] keys, long limit = 0, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetIntersectionLength(keys, limit, flags);

        public long SortedSetLength(RedisKey key, double min = -1.0 / 0.0, double max = 1.0 / 0.0, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetLength(ToInner(key), min, max, exclude, flags);

        public long SortedSetLengthByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetLengthByValue(ToInner(key), min, max, exclude, flags);

        public RedisValue SortedSetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRandomMember(ToInner(key), flags);

        public RedisValue[] SortedSetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRandomMembers(ToInner(key), count, flags);

        public SortedSetEntry[] SortedSetRandomMembersWithScores(RedisKey key, long count, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRandomMembersWithScores(ToInner(key), count, flags);

        public RedisValue[] SortedSetRangeByRank(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRangeByRank(ToInner(key), start, stop, order, flags);

        public long SortedSetRangeAndStore(
            RedisKey destinationKey,
            RedisKey sourceKey,
            RedisValue start,
            RedisValue stop,
            SortedSetOrder sortedSetOrder = SortedSetOrder.ByRank,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long? take = null,
            CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRangeAndStore(ToInner(sourceKey), ToInner(destinationKey), start, stop, sortedSetOrder, exclude, order, skip, take, flags);

        public SortedSetEntry[] SortedSetRangeByRankWithScores(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRangeByRankWithScores(ToInner(key), start, stop, order, flags);

        public RedisValue[] SortedSetRangeByScore(RedisKey key, double start = -1.0 / 0.0, double stop = 1.0 / 0.0, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRangeByScore(ToInner(key), start, stop, exclude, order, skip, take, flags);

        public SortedSetEntry[] SortedSetRangeByScoreWithScores(RedisKey key, double start = -1.0 / 0.0, double stop = 1.0 / 0.0, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRangeByScoreWithScores(ToInner(key), start, stop, exclude, order, skip, take, flags);

        public RedisValue[] SortedSetRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude, long skip, long take, CommandFlags flags) =>
            Inner.SortedSetRangeByValue(ToInner(key), min, max, exclude, Order.Ascending, skip, take, flags);

        public RedisValue[] SortedSetRangeByValue(RedisKey key, RedisValue min = default, RedisValue max = default, Exclude exclude = Exclude.None, Order order = Order.Ascending, long skip = 0, long take = -1, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRangeByValue(ToInner(key), min, max, exclude, order, skip, take, flags);

        public long? SortedSetRank(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRank(ToInner(key), member, order, flags);

        public long SortedSetRemove(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRemove(ToInner(key), members, flags);

        public bool SortedSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRemove(ToInner(key), member, flags);

        public long SortedSetRemoveRangeByRank(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRemoveRangeByRank(ToInner(key), start, stop, flags);

        public long SortedSetRemoveRangeByScore(RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRemoveRangeByScore(ToInner(key), start, stop, exclude, flags);

        public long SortedSetRemoveRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetRemoveRangeByValue(ToInner(key), min, max, exclude, flags);

        public double? SortedSetScore(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetScore(ToInner(key), member, flags);

        public double?[] SortedSetScores(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetScores(ToInner(key), members, flags);

        public long SortedSetUpdate(RedisKey key, SortedSetEntry[] values,SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetUpdate(ToInner(key), values, when, flags);

        public bool SortedSetUpdate(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetUpdate(ToInner(key), member, score, when, flags);

        public SortedSetEntry? SortedSetPop(RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetPop(ToInner(key), order, flags);

        public SortedSetEntry[] SortedSetPop(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetPop(ToInner(key), count, order, flags);

        public SortedSetPopResult SortedSetPop(RedisKey[] keys, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None) =>
            Inner.SortedSetPop(ToInner(keys), count, order, flags);

        public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamAcknowledge(ToInner(key), groupName, messageId, flags);

        public long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamAcknowledge(ToInner(key), groupName, messageIds, flags);

        public RedisValue StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamAdd(ToInner(key), streamField, streamValue, messageId, maxLength, useApproximateMaxLength, flags);

        public RedisValue StreamAdd(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamAdd(ToInner(key), streamPairs, messageId, maxLength, useApproximateMaxLength, flags);

        public StreamAutoClaimResult StreamAutoClaim(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamAutoClaim(ToInner(key), consumerGroup, claimingConsumer, minIdleTimeInMs, startAtId, count, flags);

        public StreamAutoClaimIdsOnlyResult StreamAutoClaimIdsOnly(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue startAtId, int? count = null, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamAutoClaimIdsOnly(ToInner(key), consumerGroup, claimingConsumer, minIdleTimeInMs, startAtId, count, flags);

        public StreamEntry[] StreamClaim(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamClaim(ToInner(key), consumerGroup, claimingConsumer, minIdleTimeInMs, messageIds, flags);

        public RedisValue[] StreamClaimIdsOnly(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamClaimIdsOnly(ToInner(key), consumerGroup, claimingConsumer, minIdleTimeInMs, messageIds, flags);

        public bool StreamConsumerGroupSetPosition(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamConsumerGroupSetPosition(ToInner(key), groupName, position, flags);

        public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position, CommandFlags flags) =>
            Inner.StreamCreateConsumerGroup(ToInner(key), groupName, position, flags);

        public bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position = null, bool createStream = true, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamCreateConsumerGroup(ToInner(key), groupName, position, createStream, flags);

        public StreamInfo StreamInfo(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamInfo(ToInner(key), flags);

        public StreamGroupInfo[] StreamGroupInfo(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamGroupInfo(ToInner(key), flags);

        public StreamConsumerInfo[] StreamConsumerInfo(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamConsumerInfo(ToInner(key), groupName, flags);

        public long StreamLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamLength(ToInner(key), flags);

        public long StreamDelete(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamDelete(ToInner(key), messageIds, flags);

        public long StreamDeleteConsumer(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamDeleteConsumer(ToInner(key), groupName, consumerName, flags);

        public bool StreamDeleteConsumerGroup(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamDeleteConsumerGroup(ToInner(key), groupName, flags);

        public StreamPendingInfo StreamPending(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamPending(ToInner(key), groupName, flags);

        public StreamPendingMessageInfo[] StreamPendingMessages(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamPendingMessages(ToInner(key), groupName, count, consumerName, minId, maxId, flags);

        public StreamEntry[] StreamRange(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamRange(ToInner(key), minId, maxId, count, messageOrder, flags);

        public StreamEntry[] StreamRead(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamRead(ToInner(key), position, count, flags);

        public RedisStream[] StreamRead(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamRead(streamPositions, countPerStream, flags);

        public StreamEntry[] StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position, int? count, CommandFlags flags) =>
            Inner.StreamReadGroup(ToInner(key), groupName, consumerName, position, count, flags);

        public StreamEntry[] StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, bool noAck = false, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamReadGroup(ToInner(key), groupName, consumerName, position, count, noAck, flags);

        public RedisStream[] StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream, CommandFlags flags) =>
            Inner.StreamReadGroup(streamPositions, groupName, consumerName, countPerStream, flags);

        public RedisStream[] StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, bool noAck = false, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamReadGroup(streamPositions, groupName, consumerName, countPerStream, noAck, flags);

        public long StreamTrim(RedisKey key, int maxLength, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None) =>
            Inner.StreamTrim(ToInner(key), maxLength, useApproximateMaxLength, flags);

        public long StringAppend(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.StringAppend(ToInner(key), value, flags);

        public long StringBitCount(RedisKey key, long start, long end, CommandFlags flags) =>
            Inner.StringBitCount(ToInner(key), start, end, flags);

        public long StringBitCount(RedisKey key, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None) =>
            Inner.StringBitCount(ToInner(key), start, end, indexType, flags);

        public long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
            Inner.StringBitOperation(operation, ToInner(destination), ToInner(keys), flags);

        public long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second = default, CommandFlags flags = CommandFlags.None) =>
            Inner.StringBitOperation(operation, ToInner(destination), ToInner(first), ToInnerOrDefault(second), flags);

        public long StringBitPosition(RedisKey key, bool bit, long start, long end, CommandFlags flags) =>
            Inner.StringBitPosition(ToInner(key), bit, start, end, flags);

        public long StringBitPosition(RedisKey key, bool bit, long start = 0, long end = -1, StringIndexType indexType = StringIndexType.Byte, CommandFlags flags = CommandFlags.None) =>
            Inner.StringBitPosition(ToInner(key), bit, start, end, indexType, flags);

        public double StringDecrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None) =>
            Inner.StringDecrement(ToInner(key), value, flags);

        public long StringDecrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None) =>
            Inner.StringDecrement(ToInner(key), value, flags);

        public RedisValue[] StringGet(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
            Inner.StringGet(ToInner(keys), flags);

        public RedisValue StringGet(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.StringGet(ToInner(key), flags);

        public RedisValue StringGetSetExpiry(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None) =>
            Inner.StringGetSetExpiry(ToInner(key), expiry, flags);

        public RedisValue StringGetSetExpiry(RedisKey key, DateTime expiry, CommandFlags flags = CommandFlags.None) =>
            Inner.StringGetSetExpiry(ToInner(key), expiry, flags);

        public Lease<byte>? StringGetLease(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.StringGetLease(ToInner(key), flags);

        public bool StringGetBit(RedisKey key, long offset, CommandFlags flags = CommandFlags.None) =>
            Inner.StringGetBit(ToInner(key), offset, flags);

        public RedisValue StringGetRange(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None) =>
            Inner.StringGetRange(ToInner(key), start, end, flags);

        public RedisValue StringGetSet(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.StringGetSet(ToInner(key), value, flags);

        public RedisValue StringGetDelete(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.StringGetDelete(ToInner(key), flags);

        public RedisValueWithExpiry StringGetWithExpiry(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.StringGetWithExpiry(ToInner(key), flags);

        public double StringIncrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None) =>
            Inner.StringIncrement(ToInner(key), value, flags);

        public long StringIncrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None) =>
            Inner.StringIncrement(ToInner(key), value, flags);

        public long StringLength(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.StringLength(ToInner(key), flags);

        public bool StringSet(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.StringSet(ToInner(values), when, flags);

        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when) =>
            Inner.StringSet(ToInner(key), value, expiry, when);
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) =>
            Inner.StringSet(ToInner(key), value, expiry, when, flags);
        public bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.StringSet(ToInner(key), value, expiry, keepTtl, when, flags);

        public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry, When when, CommandFlags flags) =>
            Inner.StringSetAndGet(ToInner(key), value, expiry, when, flags);

        public RedisValue StringSetAndGet(RedisKey key, RedisValue value, TimeSpan? expiry = null, bool keepTtl = false, When when = When.Always, CommandFlags flags = CommandFlags.None) =>
            Inner.StringSetAndGet(ToInner(key), value, expiry, keepTtl, when, flags);

        public bool StringSetBit(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None) =>
            Inner.StringSetBit(ToInner(key), offset, bit, flags);

        public RedisValue StringSetRange(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None) =>
            Inner.StringSetRange(ToInner(key), offset, value, flags);

        public TimeSpan Ping(CommandFlags flags = CommandFlags.None) =>
            Inner.Ping(flags);

        IEnumerable<HashEntry> IDatabase.HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
            => Inner.HashScan(ToInner(key), pattern, pageSize, flags);

        IEnumerable<HashEntry> IDatabase.HashScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => Inner.HashScan(ToInner(key), pattern, pageSize, cursor, pageOffset, flags);

        IEnumerable<RedisValue> IDatabase.SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
            =>  Inner.SetScan(ToInner(key), pattern, pageSize, flags);

        IEnumerable<RedisValue> IDatabase.SetScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => Inner.SetScan(ToInner(key), pattern, pageSize, cursor, pageOffset, flags);

        IEnumerable<SortedSetEntry> IDatabase.SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags)
            => Inner.SortedSetScan(ToInner(key), pattern, pageSize, flags);

        IEnumerable<SortedSetEntry> IDatabase.SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, long cursor, int pageOffset, CommandFlags flags)
            => Inner.SortedSetScan(ToInner(key), pattern, pageSize, cursor, pageOffset, flags);

        public bool KeyTouch(RedisKey key, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyTouch(ToInner(key), flags);

        public long KeyTouch(RedisKey[] keys, CommandFlags flags = CommandFlags.None) =>
            Inner.KeyTouch(ToInner(keys), flags);
    }
}
