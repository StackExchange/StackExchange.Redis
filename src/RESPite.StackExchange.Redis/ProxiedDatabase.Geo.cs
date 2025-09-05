using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal sealed partial class ProxiedDatabase
{
    // Async Geo methods
    public Task<bool> GeoAddAsync(
        RedisKey key,
        double longitude,
        double latitude,
        RedisValue member,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> GeoAddAsync(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> GeoAddAsync(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<bool> GeoRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<double?> GeoDistanceAsync(
        RedisKey key,
        RedisValue member1,
        RedisValue member2,
        GeoUnit unit = GeoUnit.Meters,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<string?[]> GeoHashAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<string?> GeoHashAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<GeoPosition?[]> GeoPositionAsync(
        RedisKey key,
        RedisValue[] members,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<GeoPosition?> GeoPositionAsync(
        RedisKey key,
        RedisValue member,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<GeoRadiusResult[]> GeoRadiusAsync(
        RedisKey key,
        RedisValue member,
        double radius,
        GeoUnit unit = GeoUnit.Meters,
        int count = -1,
        Order? order = null,
        GeoRadiusOptions options = GeoRadiusOptions.Default,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<GeoRadiusResult[]> GeoRadiusAsync(
        RedisKey key,
        double longitude,
        double latitude,
        double radius,
        GeoUnit unit = GeoUnit.Meters,
        int count = -1,
        Order? order = null,
        GeoRadiusOptions options = GeoRadiusOptions.Default,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<GeoRadiusResult[]> GeoSearchAsync(
        RedisKey key,
        RedisValue member,
        GeoSearchShape shape,
        int count = -1,
        bool demandClosest = true,
        Order? order = null,
        GeoRadiusOptions options = GeoRadiusOptions.Default,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<GeoRadiusResult[]> GeoSearchAsync(
        RedisKey key,
        double longitude,
        double latitude,
        GeoSearchShape shape,
        int count = -1,
        bool demandClosest = true,
        Order? order = null,
        GeoRadiusOptions options = GeoRadiusOptions.Default,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> GeoSearchAndStoreAsync(
        RedisKey sourceKey,
        RedisKey destinationKey,
        RedisValue member,
        GeoSearchShape shape,
        int count = -1,
        bool demandClosest = true,
        Order? order = null,
        bool storeDistances = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<long> GeoSearchAndStoreAsync(
        RedisKey sourceKey,
        RedisKey destinationKey,
        double longitude,
        double latitude,
        GeoSearchShape shape,
        int count = -1,
        bool demandClosest = true,
        Order? order = null,
        bool storeDistances = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Synchronous Geo methods
    public bool GeoAdd(
        RedisKey key,
        double longitude,
        double latitude,
        RedisValue member,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool GeoAdd(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long GeoAdd(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public bool GeoRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public double? GeoDistance(
        RedisKey key,
        RedisValue member1,
        RedisValue member2,
        GeoUnit unit = GeoUnit.Meters,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public string?[] GeoHash(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public string? GeoHash(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public GeoPosition?[] GeoPosition(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public GeoPosition? GeoPosition(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public GeoRadiusResult[] GeoRadius(
        RedisKey key,
        RedisValue member,
        double radius,
        GeoUnit unit = GeoUnit.Meters,
        int count = -1,
        Order? order = null,
        GeoRadiusOptions options = GeoRadiusOptions.Default,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public GeoRadiusResult[] GeoRadius(
        RedisKey key,
        double longitude,
        double latitude,
        double radius,
        GeoUnit unit = GeoUnit.Meters,
        int count = -1,
        Order? order = null,
        GeoRadiusOptions options = GeoRadiusOptions.Default,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public GeoRadiusResult[] GeoSearch(
        RedisKey key,
        RedisValue member,
        GeoSearchShape shape,
        int count = -1,
        bool demandClosest = true,
        Order? order = null,
        GeoRadiusOptions options = GeoRadiusOptions.Default,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public GeoRadiusResult[] GeoSearch(
        RedisKey key,
        double longitude,
        double latitude,
        GeoSearchShape shape,
        int count = -1,
        bool demandClosest = true,
        Order? order = null,
        GeoRadiusOptions options = GeoRadiusOptions.Default,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long GeoSearchAndStore(
        RedisKey sourceKey,
        RedisKey destinationKey,
        RedisValue member,
        GeoSearchShape shape,
        int count = -1,
        bool demandClosest = true,
        Order? order = null,
        bool storeDistances = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public long GeoSearchAndStore(
        RedisKey sourceKey,
        RedisKey destinationKey,
        double longitude,
        double latitude,
        GeoSearchShape shape,
        int count = -1,
        bool demandClosest = true,
        Order? order = null,
        bool storeDistances = false,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
