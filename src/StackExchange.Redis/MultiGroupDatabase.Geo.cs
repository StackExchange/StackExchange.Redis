namespace StackExchange.Redis;

internal sealed partial class MultiGroupDatabase
{
    // Geo operations
    public bool GeoAdd(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoAdd(key, longitude, latitude, member, flags);

    public bool GeoAdd(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoAdd(key, value, flags);

    public long GeoAdd(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoAdd(key, values, flags);

    public bool GeoRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoRemove(key, member, flags);

    public double? GeoDistance(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit = GeoUnit.Meters, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoDistance(key, member1, member2, unit, flags);

    public string?[] GeoHash(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoHash(key, members, flags);

    public string? GeoHash(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoHash(key, member, flags);

    public GeoPosition?[] GeoPosition(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoPosition(key, members, flags);

    public GeoPosition? GeoPosition(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoPosition(key, member, flags);

    public GeoRadiusResult[] GeoRadius(RedisKey key, RedisValue member, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoRadius(key, member, radius, unit, count, order, options, flags);

    public GeoRadiusResult[] GeoRadius(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoRadius(key, longitude, latitude, radius, unit, count, order, options, flags);

    public GeoRadiusResult[] GeoSearch(RedisKey key, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoSearch(key, member, shape, count, demandClosest, order, options, flags);

    public GeoRadiusResult[] GeoSearch(RedisKey key, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoSearch(key, longitude, latitude, shape, count, demandClosest, order, options, flags);

    public long GeoSearchAndStore(RedisKey sourceKey, RedisKey destinationKey, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoSearchAndStore(sourceKey, destinationKey, member, shape, count, demandClosest, order, storeDistances, flags);

    public long GeoSearchAndStore(RedisKey sourceKey, RedisKey destinationKey, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None)
        => GetDatabase().GeoSearchAndStore(sourceKey, destinationKey, longitude, latitude, shape, count, demandClosest, order, storeDistances, flags);
}
