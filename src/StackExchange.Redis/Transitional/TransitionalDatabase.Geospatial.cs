using System;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// The geospatial commands, where they have moved to the RESP context surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GeoRadius</c> is the interesting adapter: the group has no method for it, because
    /// <c>GEORADIUS</c> is a circular <c>GEOSEARCH</c> and has been deprecated in its favour since 6.2.
    /// The translation is version-gated, so a caller on an older server still gets the command that
    /// exists there - the same arrangement as <c>StringGetSet</c> and <c>ListRightPopLeftPush</c>.
    /// </para>
    /// <para>
    /// <c>GeoSearchAndStore</c> reverses its first two arguments on the way through: the old surface takes
    /// (source, destination) and the new one takes (destination, source), matching <c>SortAndStore</c> and
    /// the sorted-set combines, where the thing being written comes first.
    /// </para>
    /// </remarks>
    internal sealed partial class TransitionalDatabase
    {
        /// <inheritdoc/>
        public bool GeoAdd(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.AddAsync(key, new GeoEntry(longitude, latitude, member), flags));

        /// <inheritdoc/>
        public Task<bool> GeoAddAsync(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.AddAsync(key, new GeoEntry(longitude, latitude, member), flags).AsTask();

        /// <inheritdoc/>
        public bool GeoAdd(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.AddAsync(key, value, flags));

        /// <inheritdoc/>
        public Task<bool> GeoAddAsync(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.AddAsync(key, value, flags).AsTask();

        /// <inheritdoc/>
        public long GeoAdd(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.AddAsync(key, Required(values, nameof(values)), flags));

        /// <inheritdoc/>
        public Task<long> GeoAddAsync(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.AddAsync(key, Required(values, nameof(values)), flags).AsTask();

        /// <inheritdoc/>
        public bool GeoRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.RemoveAsync(key, member, flags));

        /// <inheritdoc/>
        public Task<bool> GeoRemoveAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.RemoveAsync(key, member, flags).AsTask();

        /// <inheritdoc/>
        public double? GeoDistance(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit = GeoUnit.Meters, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.DistanceAsync(key, member1, member2, unit, flags));

        /// <inheritdoc/>
        public Task<double?> GeoDistanceAsync(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit = GeoUnit.Meters, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.DistanceAsync(key, member1, member2, unit, flags).AsTask();

        /// <inheritdoc/>
        public string? GeoHash(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.HashAsync(key, member, flags));

        /// <inheritdoc/>
        public Task<string?> GeoHashAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.HashAsync(key, member, flags).AsTask();

        /// <inheritdoc/>
        public string?[] GeoHash(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.HashArray(key, Required(members, nameof(members)), flags));

        /// <inheritdoc/>
        public Task<string?[]> GeoHashAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.HashArray(key, Required(members, nameof(members)), flags).AsTask();

        /// <inheritdoc/>
        public GeoPosition? GeoPosition(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.PositionAsync(key, member, flags));

        /// <inheritdoc/>
        public Task<GeoPosition?> GeoPositionAsync(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.PositionAsync(key, member, flags).AsTask();

        /// <inheritdoc/>
        public GeoPosition?[] GeoPosition(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.PositionArray(key, Required(members, nameof(members)), flags));

        /// <inheritdoc/>
        public Task<GeoPosition?[]> GeoPositionAsync(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.PositionArray(key, Required(members, nameof(members)), flags).AsTask();

        /// <inheritdoc/>
        public GeoRadiusResult[] GeoRadius(RedisKey key, RedisValue member, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
        {
            DemandNotDouble(member);
            return Wait(_inner.Geospatial.RadiusArray(key, member, double.NaN, double.NaN, radius, unit, count, order, options, flags));
        }

        /// <inheritdoc/>
        public Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, RedisValue member, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
        {
            DemandNotDouble(member);
            return _inner.Geospatial.RadiusArray(key, member, double.NaN, double.NaN, radius, unit, count, order, options, flags).AsTask();
        }

        /// <inheritdoc/>
        public GeoRadiusResult[] GeoRadius(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.RadiusArray(key, RedisValue.Null, longitude, latitude, radius, unit, count, order, options, flags));

        /// <inheritdoc/>
        public Task<GeoRadiusResult[]> GeoRadiusAsync(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.RadiusArray(key, RedisValue.Null, longitude, latitude, radius, unit, count, order, options, flags).AsTask();

        /// <inheritdoc/>
        public GeoRadiusResult[] GeoSearch(RedisKey key, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.SearchArray(key, member, double.NaN, double.NaN, shape, count, demandClosest, order, options, flags));

        /// <inheritdoc/>
        public Task<GeoRadiusResult[]> GeoSearchAsync(RedisKey key, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.SearchArray(key, member, double.NaN, double.NaN, shape, count, demandClosest, order, options, flags).AsTask();

        /// <inheritdoc/>
        public GeoRadiusResult[] GeoSearch(RedisKey key, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.SearchArray(key, RedisValue.Null, longitude, latitude, shape, count, demandClosest, order, options, flags));

        /// <inheritdoc/>
        public Task<GeoRadiusResult[]> GeoSearchAsync(RedisKey key, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.SearchArray(key, RedisValue.Null, longitude, latitude, shape, count, demandClosest, order, options, flags).AsTask();

        /// <inheritdoc/>
        public long GeoSearchAndStore(RedisKey sourceKey, RedisKey destinationKey, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.SearchAndStoreAsync(destinationKey, sourceKey, member, shape, count, demandClosest, order, storeDistances, flags));

        /// <inheritdoc/>
        public Task<long> GeoSearchAndStoreAsync(RedisKey sourceKey, RedisKey destinationKey, RedisValue member, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.SearchAndStoreAsync(destinationKey, sourceKey, member, shape, count, demandClosest, order, storeDistances, flags).AsTask();

        /// <inheritdoc/>
        public long GeoSearchAndStore(RedisKey sourceKey, RedisKey destinationKey, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None)
            => Wait(_inner.Geospatial.SearchAndStoreAsync(destinationKey, sourceKey, longitude, latitude, shape, count, demandClosest, order, storeDistances, flags));

        /// <inheritdoc/>
        public Task<long> GeoSearchAndStoreAsync(RedisKey sourceKey, RedisKey destinationKey, double longitude, double latitude, GeoSearchShape shape, int count = -1, bool demandClosest = true, Order? order = null, bool storeDistances = false, CommandFlags flags = CommandFlags.None)
            => _inner.Geospatial.SearchAndStoreAsync(destinationKey, sourceKey, longitude, latitude, shape, count, demandClosest, order, storeDistances, flags).AsTask();

        /// <summary>
        /// The old surface rejects a numeric member here, because it is almost certainly a longitude that
        /// bound to the wrong overload.
        /// </summary>
        private static void DemandNotDouble(in RedisValue member)
        {
            if (member.Type == RedisValue.StorageType.Double)
            {
                throw new ArgumentException("Member should not be a double, you likely want the GeoRadius(RedisKey, double, double, ...) overload.", nameof(member));
            }
        }
    }
}
