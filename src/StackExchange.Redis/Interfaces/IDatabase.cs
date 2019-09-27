using System;
using System.Collections.Generic;
using System.Net;

namespace StackExchange.Redis
{
    /// <summary>
    /// Describes functionality that is common to both standalone redis servers and redis clusters
    /// </summary>
    public interface IDatabase : IRedis, IDatabaseAsync
    {
        /// <summary>
        /// The numeric identifier of this database
        /// </summary>
        int Database { get; }

        /// <summary>
        /// Allows creation of a group of operations that will be sent to the server as a single unit,
        /// but which may or may not be processed on the server contiguously.
        /// </summary>
        /// <param name="asyncState">The async object state to be passed into the created <see cref="IBatch"/>.</param>
        /// <returns>The created batch.</returns>
        IBatch CreateBatch(object asyncState = null);

        /// <summary>
        /// Allows creation of a group of operations that will be sent to the server as a single unit,
        /// and processed on the server as a single unit.
        /// </summary>
        /// <param name="asyncState">The async object state to be passed into the created <see cref="ITransaction"/>.</param>
        /// <returns>The created transaction.</returns>
        ITransaction CreateTransaction(object asyncState = null);

        /// <summary>
        /// Atomically transfer a key from a source Redis instance to a destination Redis instance. On success the key is deleted from the original instance by default, and is guaranteed to exist in the target instance.
        /// </summary>
        /// <param name="key">The key to migrate.</param>
        /// <param name="toServer">The server to migrate the key to.</param>
        /// <param name="toDatabase">The database to migrate the key to.</param>
        /// <param name="timeoutMilliseconds">The timeout to use for the transfer.</param>
        /// <param name="migrateOptions">The options to use for this migration.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>https://redis.io/commands/MIGRATE</remarks>
        void KeyMigrate(RedisKey key, EndPoint toServer, int toDatabase = 0, int timeoutMilliseconds = 0, MigrateOptions migrateOptions = MigrateOptions.None, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the raw DEBUG OBJECT output for a key; this command is not fully documented and should be avoided unless you have good reason, and then avoided anyway.
        /// </summary>
        /// <param name="key">The key to debug.</param>
        /// <param name="flags">The flags to use for this migration.</param>
        /// <returns>The raw output from DEBUG OBJECT.</returns>
        /// <remarks>https://redis.io/commands/debug-object</remarks>
        RedisValue DebugObject(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Add the specified member to the set stored at key. Specified members that are already a member of this set are ignored. If key does not exist, a new set is created before adding the specified members.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="longitude">The longitude of geo entry.</param>
        /// <param name="latitude">The latitude of the geo entry.</param>
        /// <param name="member">The value to set at this entry.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the specified member was not already present in the set, else False.</returns>
        /// <remarks>https://redis.io/commands/geoadd</remarks>
        bool GeoAdd(RedisKey key, double longitude, double latitude, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Add the specified member to the set stored at key. Specified members that are already a member of this set are ignored. If key does not exist, a new set is created before adding the specified members.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="value">The geo value to store.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the specified member was not already present in the set, else False</returns>
        /// <remarks>https://redis.io/commands/geoadd</remarks>
        bool GeoAdd(RedisKey key, GeoEntry value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Add the specified members to the set stored at key. Specified members that are already a member of this set are ignored. If key does not exist, a new set is created before adding the specified members.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="values">The geo values add to the set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of elements that were added to the set, not including all the elements already present into the set.</returns>
        /// <remarks>https://redis.io/commands/geoadd</remarks>
        long GeoAdd(RedisKey key, GeoEntry[] values, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes the specified member from the geo sorted set stored at key. Non existing members are ignored.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="member">The geo value to remove.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the member existed in the sorted set and was removed; False otherwise.</returns>
        /// <remarks>https://redis.io/commands/zrem</remarks>
        bool GeoRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the distance between two members in the geospatial index represented by the sorted set.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="member1">The first member to check.</param>
        /// <param name="member2">The second member to check.</param>
        /// <param name="unit">The unit of distance to return (defaults to meters).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The command returns the distance as a double (represented as a string) in the specified unit, or NULL if one or both the elements are missing.</returns>
        /// <remarks>https://redis.io/commands/geodist</remarks>
        double? GeoDistance(RedisKey key, RedisValue member1, RedisValue member2, GeoUnit unit = GeoUnit.Meters, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return valid Geohash strings representing the position of one or more elements in a sorted set value representing a geospatial index (where elements were added using GEOADD).
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="members">The members to get.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The command returns an array where each element is the Geohash corresponding to each member name passed as argument to the command.</returns>
        /// <remarks>https://redis.io/commands/geohash</remarks>
        string[] GeoHash(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return valid Geohash strings representing the position of one or more elements in a sorted set value representing a geospatial index (where elements were added using GEOADD).
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="member">The member to get.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The command returns an array where each element is the Geohash corresponding to each member name passed as argument to the command.</returns>
        /// <remarks>https://redis.io/commands/geohash</remarks>
        string GeoHash(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the positions (longitude,latitude) of all the specified members of the geospatial index represented by the sorted set at key.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="members">The members to get.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The command returns an array where each element is a two elements array representing longitude and latitude (x,y) of each member name passed as argument to the command.Non existing elements are reported as NULL elements of the array.</returns>
        /// <remarks>https://redis.io/commands/geopos</remarks>
        GeoPosition?[] GeoPosition(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the positions (longitude,latitude) of all the specified members of the geospatial index represented by the sorted set at key.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="member">The member to get.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The command returns an array where each element is a two elements array representing longitude and latitude (x,y) of each member name passed as argument to the command.Non existing elements are reported as NULL elements of the array.</returns>
        /// <remarks>https://redis.io/commands/geopos</remarks>
        GeoPosition? GeoPosition(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the members of a sorted set populated with geospatial information using GEOADD, which are within the borders of the area specified with the center location and the maximum distance from the center (the radius).
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="member">The member to get a radius of results from.</param>
        /// <param name="radius">The radius to check.</param>
        /// <param name="unit">The unit of <paramref name="radius"/> (defaults to meters).</param>
        /// <param name="count">The count of results to get, -1 for unlimited.</param>
        /// <param name="order">The order of the results.</param>
        /// <param name="options">The search options to use.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The results found within the radius, if any.</returns>
        /// <remarks>https://redis.io/commands/georadius</remarks>
        GeoRadiusResult[] GeoRadius(RedisKey key, RedisValue member, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the members of a sorted set populated with geospatial information using GEOADD, which are within the borders of the area specified with the center location and the maximum distance from the center (the radius).
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="longitude">The longitude of the point to get a radius of results from.</param>
        /// <param name="latitude">The latitude of the point to get a radius of results from.</param>
        /// <param name="radius">The radius to check.</param>
        /// <param name="unit">The unit of <paramref name="radius"/> (defaults to meters).</param>
        /// <param name="count">The count of results to get, -1 for unlimited.</param>
        /// <param name="order">The order of the results.</param>
        /// <param name="options">The search options to use.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The results found within the radius, if any.</returns>
        /// <remarks>https://redis.io/commands/georadius</remarks>
        GeoRadiusResult[] GeoRadius(RedisKey key, double longitude, double latitude, double radius, GeoUnit unit = GeoUnit.Meters, int count = -1, Order? order = null, GeoRadiusOptions options = GeoRadiusOptions.Default, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Decrements the number stored at field in the hash stored at key by decrement. If key does not exist, a new key holding a hash is created. If field does not exist or holds a string that cannot be interpreted as integer, the value is set to 0 before the operation is performed.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashField">The field in the hash to decrement.</param>
        /// <param name="value">The amount to decrement by.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value at field after the decrement operation.</returns>
        /// <remarks>The range of values supported by HINCRBY is limited to 64 bit signed integers.</remarks>
        /// <remarks>https://redis.io/commands/hincrby</remarks>
        long HashDecrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Decrement the specified field of an hash stored at key, and representing a floating point number, by the specified decrement. If the field does not exist, it is set to 0 before performing the operation.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashField">The field in the hash to decrement.</param>
        /// <param name="value">The amount to decrement by.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value at field after the decrement operation.</returns>
        /// <remarks>The precision of the output is fixed at 17 digits after the decimal point regardless of the actual internal precision of the computation.</remarks>
        /// <remarks>https://redis.io/commands/hincrbyfloat</remarks>
        double HashDecrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes the specified fields from the hash stored at key. Non-existing fields are ignored. Non-existing keys are treated as empty hashes and this command returns 0.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashField">The field in the hash to delete.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of fields that were removed.</returns>
        /// <remarks>https://redis.io/commands/hdel</remarks>
        bool HashDelete(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes the specified fields from the hash stored at key. Non-existing fields are ignored. Non-existing keys are treated as empty hashes and this command returns 0.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashFields">The fields in the hash to delete.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of fields that were removed.</returns>
        /// <remarks>https://redis.io/commands/hdel</remarks>
        long HashDelete(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns if field is an existing field in the hash stored at key.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashField">The field in the hash to check.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>1 if the hash contains field. 0 if the hash does not contain field, or key does not exist.</returns>
        /// <remarks>https://redis.io/commands/hexists</remarks>
        bool HashExists(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the value associated with field in the hash stored at key.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashField">The field in the hash to get.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value associated with field, or nil when field is not present in the hash or key does not exist.</returns>
        /// <remarks>https://redis.io/commands/hget</remarks>
        RedisValue HashGet(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the value associated with field in the hash stored at key.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashField">The field in the hash to get.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value associated with field, or nil when field is not present in the hash or key does not exist.</returns>
        /// <remarks>https://redis.io/commands/hget</remarks>
        Lease<byte> HashGetLease(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the values associated with the specified fields in the hash stored at key.
        /// For every field that does not exist in the hash, a nil value is returned.Because a non-existing keys are treated as empty hashes, running HMGET against a non-existing key will return a list of nil values.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashFields">The fields in the hash to delete.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>List of values associated with the given fields, in the same order as they are requested.</returns>
        /// <remarks>https://redis.io/commands/hmget</remarks>
        RedisValue[] HashGet(RedisKey key, RedisValue[] hashFields, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns all fields and values of the hash stored at key. 
        /// </summary>
        /// <param name="key">The key of the hash to get all entries from.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>List of fields and their values stored in the hash, or an empty list when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/hgetall</remarks>
        HashEntry[] HashGetAll(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Increments the number stored at field in the hash stored at key by increment. If key does not exist, a new key holding a hash is created. If field does not exist or holds a string that cannot be interpreted as integer, the value is set to 0 before the operation is performed.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashField">The field in the hash to increment.</param>
        /// <param name="value">The amount to increment by.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value at field after the increment operation.</returns>
        /// <remarks>The range of values supported by HINCRBY is limited to 64 bit signed integers.</remarks>
        /// <remarks>https://redis.io/commands/hincrby</remarks>
        long HashIncrement(RedisKey key, RedisValue hashField, long value = 1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Increment the specified field of an hash stored at key, and representing a floating point number, by the specified increment. If the field does not exist, it is set to 0 before performing the operation.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashField">The field in the hash to increment.</param>
        /// <param name="value">The amount to increment by.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value at field after the increment operation.</returns>
        /// <remarks>The precision of the output is fixed at 17 digits after the decimal point regardless of the actual internal precision of the computation.</remarks>
        /// <remarks>https://redis.io/commands/hincrbyfloat</remarks>
        double HashIncrement(RedisKey key, RedisValue hashField, double value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns all field names in the hash stored at key.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>List of fields in the hash, or an empty list when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/hkeys</remarks>
        RedisValue[] HashKeys(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the number of fields contained in the hash stored at key.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of fields in the hash, or 0 when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/hlen</remarks>
        long HashLength(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The HSCAN command is used to incrementally iterate over a hash.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="pattern">The pattern of keys to get entries for.</param>
        /// <param name="pageSize">The page size to iterate by.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>Yields all elements of the hash matching the pattern.</returns>
        /// <remarks>https://redis.io/commands/hscan</remarks>
        IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags);

        /// <summary>
        /// The HSCAN command is used to incrementally iterate over a hash; note: to resume an iteration via <i>cursor</i>, cast the original enumerable or enumerator to <i>IScanningCursor</i>.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="pattern">The pattern of keys to get entries for.</param>
        /// <param name="pageSize">The page size to iterate by.</param>
        /// <param name="cursor">The cursor position to start at.</param>
        /// <param name="pageOffset">The page offset to start at.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>Yields all elements of the hash matching the pattern.</returns>
        /// <remarks>https://redis.io/commands/hscan</remarks>
        IEnumerable<HashEntry> HashScan(RedisKey key, RedisValue pattern = default(RedisValue), int pageSize = RedisBase.CursorUtils.DefaultPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Sets the specified fields to their respective values in the hash stored at key. This command overwrites any specified fields that already exist in the hash, leaving other unspecified fields untouched. If key does not exist, a new key holding a hash is created.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashFields">The entries to set in the hash.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>https://redis.io/commands/hmset</remarks>
        void HashSet(RedisKey key, HashEntry[] hashFields, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Sets field in the hash stored at key to value. If key does not exist, a new key holding a hash is created. If field already exists in the hash, it is overwritten.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashField">The field to set in the hash.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="when">Which conditions under which to set the field value (defaults to always).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>1 if field is a new field in the hash and value was set. 0 if field already exists in the hash and the value was updated.</returns>
        /// <remarks>https://redis.io/commands/hset</remarks>
        /// <remarks>https://redis.io/commands/hsetnx</remarks>
        bool HashSet(RedisKey key, RedisValue hashField, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the string length of the value associated with field in the hash stored at key.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="hashField">The field containing the string</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>the length of the string at field, or 0 when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/hstrlen</remarks>
        long HashStringLength(RedisKey key, RedisValue hashField, CommandFlags flags = CommandFlags.None);
        
        /// <summary>
        /// Returns all values in the hash stored at key.
        /// </summary>
        /// <param name="key">The key of the hash.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>List of values in the hash, or an empty list when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/hvals</remarks>
        RedisValue[] HashValues(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Adds the element to the HyperLogLog data structure stored at the variable name specified as first argument.
        /// </summary>
        /// <param name="key">The key of the hyperloglog.</param>
        /// <param name="value">The value to add.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if at least 1 HyperLogLog internal register was altered, false otherwise.</returns>
        /// <remarks>https://redis.io/commands/pfadd</remarks>
        bool HyperLogLogAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Adds all the element arguments to the HyperLogLog data structure stored at the variable name specified as first argument.
        /// </summary>
        /// <param name="key">The key of the hyperloglog.</param>
        /// <param name="values">The values to add.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if at least 1 HyperLogLog internal register was altered, false otherwise.</returns>
        /// <remarks>https://redis.io/commands/pfadd</remarks>
        bool HyperLogLogAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the approximated cardinality computed by the HyperLogLog data structure stored at the specified variable, or 0 if the variable does not exist.
        /// </summary>
        /// <param name="key">The key of the hyperloglog.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The approximated number of unique elements observed via HyperLogLogAdd.</returns>
        /// <remarks>https://redis.io/commands/pfcount</remarks>
        long HyperLogLogLength(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the approximated cardinality of the union of the HyperLogLogs passed, by internally merging the HyperLogLogs stored at the provided keys into a temporary hyperLogLog, or 0 if the variable does not exist.
        /// </summary>
        /// <param name="keys">The keys of the hyperloglogs.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The approximated number of unique elements observed via HyperLogLogAdd.</returns>
        /// <remarks>https://redis.io/commands/pfcount</remarks>
        long HyperLogLogLength(RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Merge multiple HyperLogLog values into an unique value that will approximate the cardinality of the union of the observed Sets of the source HyperLogLog structures.
        /// </summary>
        /// <param name="destination">The key of the merged hyperloglog.</param>
        /// <param name="first">The key of the first hyperloglog to merge.</param>
        /// <param name="second">The key of the first hyperloglog to merge.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>https://redis.io/commands/pfmerge</remarks>
        void HyperLogLogMerge(RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Merge multiple HyperLogLog values into an unique value that will approximate the cardinality of the union of the observed Sets of the source HyperLogLog structures.
        /// </summary>
        /// <param name="destination">The key of the merged hyperloglog.</param>
        /// <param name="sourceKeys">The keys of the hyperloglogs to merge.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>https://redis.io/commands/pfmerge</remarks>
        void HyperLogLogMerge(RedisKey destination, RedisKey[] sourceKeys, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Inidicate exactly which redis server we are talking to.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The endpoint serving the key.</returns>
        EndPoint IdentifyEndpoint(RedisKey key = default(RedisKey), CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes the specified key. A key is ignored if it does not exist.
        /// If UNLINK is available (Redis 4.0+), it will be used.
        /// </summary>
        /// <param name="key">The key to delete.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the key was removed.</returns>
        /// <remarks>https://redis.io/commands/del</remarks>
        /// <remarks>https://redis.io/commands/unlink</remarks>
        bool KeyDelete(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes the specified keys. A key is ignored if it does not exist.
        /// If UNLINK is available (Redis 4.0+), it will be used.
        /// </summary>
        /// <param name="keys">The keys to delete.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of keys that were removed.</returns>
        /// <remarks>https://redis.io/commands/del</remarks>
        /// <remarks>https://redis.io/commands/unlink</remarks>
        long KeyDelete(RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Serialize the value stored at key in a Redis-specific format and return it to the user. The returned value can be synthesized back into a Redis key using the RESTORE command.
        /// </summary>
        /// <param name="key">The key to dump.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>the serialized value.</returns>
        /// <remarks>https://redis.io/commands/dump</remarks>
        byte[] KeyDump(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns if key exists.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>1 if the key exists. 0 if the key does not exist.</returns>
        /// <remarks>https://redis.io/commands/exists</remarks>
        bool KeyExists(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Indicates how many of the supplied keys exists.
        /// </summary>
        /// <param name="keys">The keys to check.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of keys that existed.</returns>
        /// <remarks>https://redis.io/commands/exists</remarks>
        long KeyExists(RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Set a timeout on key. After the timeout has expired, the key will automatically be deleted. A key with an associated timeout is said to be volatile in Redis terminology.
        /// </summary>
        /// <param name="key">The key to set the expiration for.</param>
        /// <param name="expiry">The timeout to set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>1 if the timeout was set. 0 if key does not exist or the timeout could not be set.</returns>
        /// <remarks>If key is updated before the timeout has expired, then the timeout is removed as if the PERSIST command was invoked on key.
        /// For Redis versions &lt; 2.1.3, existing timeouts cannot be overwritten. So, if key already has an associated timeout, it will do nothing and return 0. Since Redis 2.1.3, you can update the timeout of a key. It is also possible to remove the timeout using the PERSIST command. See the page on key expiry for more information.</remarks>
        /// <remarks>https://redis.io/commands/expire</remarks>
        /// <remarks>https://redis.io/commands/pexpire</remarks>
        /// <remarks>https://redis.io/commands/persist</remarks>
        bool KeyExpire(RedisKey key, TimeSpan? expiry, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Set a timeout on key. After the timeout has expired, the key will automatically be deleted. A key with an associated timeout is said to be volatile in Redis terminology.
        /// </summary>
        /// <param name="key">The key to set the expiration for.</param>
        /// <param name="expiry">The exact date to expiry to set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>1 if the timeout was set. 0 if key does not exist or the timeout could not be set.</returns>
        /// <remarks>If key is updated before the timeout has expired, then the timeout is removed as if the PERSIST command was invoked on key.
        /// For Redis versions &lt; 2.1.3, existing timeouts cannot be overwritten. So, if key already has an associated timeout, it will do nothing and return 0. Since Redis 2.1.3, you can update the timeout of a key. It is also possible to remove the timeout using the PERSIST command. See the page on key expiry for more information.</remarks>
        /// <remarks>https://redis.io/commands/expireat</remarks>
        /// <remarks>https://redis.io/commands/pexpireat</remarks>
        /// <remarks>https://redis.io/commands/persist</remarks>
        bool KeyExpire(RedisKey key, DateTime? expiry, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the time since the object stored at the specified key is idle (not requested by read or write operations)
        /// </summary>
        /// <param name="key">The key to get the time of.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The time since the object stored at the specified key is idle</returns>
        /// <remarks>https://redis.io/commands/object</remarks>
        TimeSpan? KeyIdleTime(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Move key from the currently selected database (see SELECT) to the specified destination database. When key already exists in the destination database, or it does not exist in the source database, it does nothing. It is possible to use MOVE as a locking primitive because of this.
        /// </summary>
        /// <param name="key">The key to move.</param>
        /// <param name="database">The database to move the key to.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>1 if key was moved; 0 if key was not moved.</returns>
        /// <remarks>https://redis.io/commands/move</remarks>
        bool KeyMove(RedisKey key, int database, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Remove the existing timeout on key, turning the key from volatile (a key with an expire set) to persistent (a key that will never expire as no timeout is associated).
        /// </summary>
        /// <param name="key">The key to presist.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>1 if the timeout was removed. 0 if key does not exist or does not have an associated timeout.</returns>
        /// <remarks>https://redis.io/commands/persist</remarks>
        bool KeyPersist(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return a random key from the currently selected database.
        /// </summary>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The random key, or nil when the database is empty.</returns>
        /// <remarks>https://redis.io/commands/randomkey</remarks>
        RedisKey KeyRandom(CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Renames key to newkey. It returns an error when the source and destination names are the same, or when key does not exist. 
        /// </summary>
        /// <param name="key">The key to rename.</param>
        /// <param name="newKey">The key to rename to.</param>
        /// <param name="when">What conditions to rename under (defaults to always).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the key was renamed, false otherwise.</returns>
        /// <remarks>https://redis.io/commands/rename</remarks>
        /// <remarks>https://redis.io/commands/renamenx</remarks>
        bool KeyRename(RedisKey key, RedisKey newKey, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Create a key associated with a value that is obtained by deserializing the provided serialized value (obtained via DUMP).
        /// If ttl is 0 the key is created without any expire, otherwise the specified expire time(in milliseconds) is set.
        /// </summary>
        /// <param name="key">The key to restore.</param>
        /// <param name="value">The value of the key.</param>
        /// <param name="expiry">The expiry to set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>https://redis.io/commands/restore</remarks>
        void KeyRestore(RedisKey key, byte[] value, TimeSpan? expiry = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the remaining time to live of a key that has a timeout.  This introspection capability allows a Redis client to check how many seconds a given key will continue to be part of the dataset.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>TTL, or nil when key does not exist or does not have a timeout.</returns>
        /// <remarks>https://redis.io/commands/ttl</remarks>
        TimeSpan? KeyTimeToLive(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the string representation of the type of the value stored at key. The different types that can be returned are: string, list, set, zset and hash.
        /// </summary>
        /// <param name="key">The key to get the type of.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>Type of key, or none when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/type</remarks>
        RedisType KeyType(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the element at index index in the list stored at key. The index is zero-based, so 0 means the first element, 1 the second element and so on. Negative indices can be used to designate elements starting at the tail of the list. Here, -1 means the last element, -2 means the penultimate and so forth.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="index">The index position to ge the value at.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The requested element, or nil when index is out of range.</returns>
        /// <remarks>https://redis.io/commands/lindex</remarks>
        RedisValue ListGetByIndex(RedisKey key, long index, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Inserts value in the list stored at key either before or after the reference value pivot.
        /// When key does not exist, it is considered an empty list and no operation is performed.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="pivot">The value to insert after.</param>
        /// <param name="value">The value to insert.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The length of the list after the insert operation, or -1 when the value pivot was not found.</returns>
        /// <remarks>https://redis.io/commands/linsert</remarks>
        long ListInsertAfter(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Inserts value in the list stored at key either before or after the reference value pivot.
        /// When key does not exist, it is considered an empty list and no operation is performed.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="pivot">The value to insert before.</param>
        /// <param name="value">The value to insert.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The length of the list after the insert operation, or -1 when the value pivot was not found.</returns>
        /// <remarks>https://redis.io/commands/linsert</remarks>
        long ListInsertBefore(RedisKey key, RedisValue pivot, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes and returns the first element of the list stored at key.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value of the first element, or nil when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/lpop</remarks>
        RedisValue ListLeftPop(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Insert the specified value at the head of the list stored at key. If key does not exist, it is created as empty list before performing the push operations.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="value">The value to add to the head of the list.</param>
        /// <param name="when">Which conditions to add to the list under (defaults to always).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The length of the list after the push operations.</returns>
        /// <remarks>https://redis.io/commands/lpush</remarks>
        /// <remarks>https://redis.io/commands/lpushx</remarks>
        long ListLeftPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Insert all the specified values at the head of the list stored at key. If key does not exist, it is created as empty list before performing the push operations.
        /// Elements are inserted one after the other to the head of the list, from the leftmost element to the rightmost element. So for instance the command LPUSH mylist a b c will result into a list containing c as first element, b as second element and a as third element.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="values">The values to add to the head of the list.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The length of the list after the push operations.</returns>
        /// <remarks>https://redis.io/commands/lpush</remarks>
        long ListLeftPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the length of the list stored at key. If key does not exist, it is interpreted as an empty list and 0 is returned. 
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The length of the list at key.</returns>
        /// <remarks>https://redis.io/commands/llen</remarks>
        long ListLength(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the specified elements of the list stored at key. The offsets start and stop are zero-based indexes, with 0 being the first element of the list (the head of the list), 1 being the next element and so on.
        /// These offsets can also be negative numbers indicating offsets starting at the end of the list.For example, -1 is the last element of the list, -2 the penultimate, and so on.
        /// Note that if you have a list of numbers from 0 to 100, LRANGE list 0 10 will return 11 elements, that is, the rightmost item is included. 
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="start">The start index of the list.</param>
        /// <param name="stop">The stop index of the list.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>List of elements in the specified range.</returns>
        /// <remarks>https://redis.io/commands/lrange</remarks>
        RedisValue[] ListRange(RedisKey key, long start = 0, long stop = -1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes the first count occurrences of elements equal to value from the list stored at key. The count argument influences the operation in the following ways:
        /// count &gt; 0: Remove elements equal to value moving from head to tail.
        /// count &lt; 0: Remove elements equal to value moving from tail to head.
        /// count = 0: Remove all elements equal to value.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="value">The value to remove from the list.</param>
        /// <param name="count">The count behavior (see method summary).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of removed elements.</returns>
        /// <remarks>https://redis.io/commands/lrem</remarks>
        long ListRemove(RedisKey key, RedisValue value, long count = 0, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes and returns the last element of the list stored at key.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The element being popped.</returns>
        /// <remarks>https://redis.io/commands/rpop</remarks>
        RedisValue ListRightPop(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Atomically returns and removes the last element (tail) of the list stored at source, and pushes the element at the first element (head) of the list stored at destination.
        /// </summary>
        /// <param name="source">The key of the source list.</param>
        /// <param name="destination">The key of the destination list.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The element being popped and pushed.</returns>
        /// <remarks>https://redis.io/commands/rpoplpush</remarks>
        RedisValue ListRightPopLeftPush(RedisKey source, RedisKey destination, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Insert the specified value at the tail of the list stored at key. If key does not exist, it is created as empty list before performing the push operation.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="value">The value to add to the tail of the list.</param>
        /// <param name="when">Which conditions to add to the list under.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The length of the list after the push operation.</returns>
        /// <remarks>https://redis.io/commands/rpush</remarks>
        /// <remarks>https://redis.io/commands/rpushx</remarks>
        long ListRightPush(RedisKey key, RedisValue value, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Insert all the specified values at the tail of the list stored at key. If key does not exist, it is created as empty list before performing the push operation. 
        /// Elements are inserted one after the other to the tail of the list, from the leftmost element to the rightmost element. So for instance the command RPUSH mylist a b c will result into a list containing a as first element, b as second element and c as third element.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="values">The values to add to the tail of the list.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The length of the list after the push operation.</returns>
        /// <remarks>https://redis.io/commands/rpush</remarks>
        long ListRightPush(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Sets the list element at index to value. For more information on the index argument, see ListGetByIndex. An error is returned for out of range indexes.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="index">The index to set the value at.</param>
        /// <param name="value">The values to add to the list.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>https://redis.io/commands/lset</remarks>
        void ListSetByIndex(RedisKey key, long index, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Trim an existing list so that it will contain only the specified range of elements specified. Both start and stop are zero-based indexes, where 0 is the first element of the list (the head), 1 the next element and so on.
        /// For example: LTRIM foobar 0 2 will modify the list stored at foobar so that only the first three elements of the list will remain.
        /// start and end can also be negative numbers indicating offsets from the end of the list, where -1 is the last element of the list, -2 the penultimate element and so on.
        /// </summary>
        /// <param name="key">The key of the list.</param>
        /// <param name="start">The start index of the list to trim to.</param>
        /// <param name="stop">The end index of the list to trim to.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>https://redis.io/commands/ltrim</remarks>
        void ListTrim(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Extends a lock, if the token value is correct.
        /// </summary>
        /// <param name="key">The key of the lock.</param>
        /// <param name="value">The value to set at the key.</param>
        /// <param name="expiry">The expiration of the lock key.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the lock was successfully extended.</returns>
        bool LockExtend(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Queries the token held against a lock.
        /// </summary>
        /// <param name="key">The key of the lock.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The current value of the lock, if any.</returns>
        RedisValue LockQuery(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Releases a lock, if the token value is correct.
        /// </summary>
        /// <param name="key">The key of the lock.</param>
        /// <param name="value">The value at the key tht must match.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the lock was successfully released, false otherwise.</returns>
        bool LockRelease(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Takes a lock (specifying a token value) if it is not already taken.
        /// </summary>
        /// <param name="key">The key of the lock.</param>
        /// <param name="value">The value to set at the key.</param>
        /// <param name="expiry">The expiration of the lock key.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the lock was successfully taken, false otherwise.</returns>
        bool LockTake(RedisKey key, RedisValue value, TimeSpan expiry, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Posts a message to the given channel.
        /// </summary>
        /// <param name="channel">The channel to publish to.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of clients that received the message.</returns>
        /// <remarks>https://redis.io/commands/publish</remarks>
        long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Execute an arbitrary command against the server; this is primarily intended for
        /// executing modules, but may also be used to provide access to new features that lack
        /// a direct API.
        /// </summary>
        /// <param name="command">The command to run.</param>
        /// <param name="args">The arguments to pass for the command.</param>
        /// <remarks>This API should be considered an advanced feature; inappropriate use can be harmful</remarks>
        /// <returns>A dynamic representation of the command's result</returns>
        RedisResult Execute(string command, params object[] args);

        /// <summary>
        /// Execute an arbitrary command against the server; this is primarily intended for
        /// executing modules, but may also be used to provide access to new features that lack
        /// a direct API.
        /// </summary>
        /// <param name="command">The command to run.</param>
        /// <param name="args">The arguments to pass for the command.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>This API should be considered an advanced feature; inappropriate use can be harmful</remarks>
        /// <returns>A dynamic representation of the command's result</returns>
        RedisResult Execute(string command, ICollection<object> args, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Execute a Lua script against the server.
        /// </summary>
        /// <param name="script">The script to execute.</param>
        /// <param name="keys">The keys to execute against.</param>
        /// <param name="values">The values to execute against.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>A dynamic representation of the script's result</returns>
        /// <remarks>https://redis.io/commands/eval</remarks>
        /// <remarks>https://redis.io/commands/evalsha</remarks>
        RedisResult ScriptEvaluate(string script, RedisKey[] keys = null, RedisValue[] values = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Execute a Lua script against the server using just the SHA1 hash
        /// </summary>
        /// <param name="hash">The hash of the script to execute.</param>
        /// <param name="keys">The keys to execute against.</param>
        /// <param name="values">The values to execute against.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>A dynamic representation of the script's result</returns>
        /// <remarks>https://redis.io/commands/evalsha</remarks>
        RedisResult ScriptEvaluate(byte[] hash, RedisKey[] keys = null, RedisValue[] values = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Execute a lua script against the server, using previously prepared script.
        /// Named parameters, if any, are provided by the `parameters` object.
        /// </summary>
        /// <param name="script">The script to execute.</param>
        /// <param name="parameters">The parameters to pass to the script.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>A dynamic representation of the script's result</returns>
        /// <remarks>https://redis.io/commands/eval</remarks>
        RedisResult ScriptEvaluate(LuaScript script, object parameters = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Execute a lua script against the server, using previously prepared and loaded script.
        /// This method sends only the SHA1 hash of the lua script to Redis.
        /// Named parameters, if any, are provided by the `parameters` object.
        /// </summary>
        /// <param name="script">The already-loaded script to execute.</param>
        /// <param name="parameters">The parameters to pass to the script.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>A dynamic representation of the script's result</returns>
        /// <remarks>https://redis.io/commands/eval</remarks>
        RedisResult ScriptEvaluate(LoadedLuaScript script, object parameters = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Add the specified member to the set stored at key.
        /// Specified members that are already a member of this set are ignored.
        /// If key does not exist, a new set is created before adding the specified members.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="value">The value to add to the set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the specified member was not already present in the set, else False</returns>
        /// <remarks>https://redis.io/commands/sadd</remarks>
        bool SetAdd(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Add the specified members to the set stored at key.
        /// Specified members that are already a member of this set are ignored.
        /// If key does not exist, a new set is created before adding the specified members.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="values">The values to add to the set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of elements that were added to the set, not including all the elements already present into the set.</returns>
        /// <remarks>https://redis.io/commands/sadd</remarks>
        long SetAdd(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the members of the set resulting from the specified operation against the given sets.
        /// </summary>
        /// <param name="operation">The operation to perform.</param>
        /// <param name="first">The key of the first set.</param>
        /// <param name="second">The key of the second set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>List with members of the resulting set.</returns>
        /// <remarks>https://redis.io/commands/sunion</remarks>
        /// <remarks>https://redis.io/commands/sinter</remarks>
        /// <remarks>https://redis.io/commands/sdiff</remarks>
        RedisValue[] SetCombine(SetOperation operation, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the members of the set resulting from the specified operation against the given sets.
        /// </summary>
        /// <param name="operation">The operation to perform.</param>
        /// <param name="keys">The keys of the sets to operate on.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>List with members of the resulting set.</returns>
        /// <remarks>https://redis.io/commands/sunion</remarks>
        /// <remarks>https://redis.io/commands/sinter</remarks>
        /// <remarks>https://redis.io/commands/sdiff</remarks>
        RedisValue[] SetCombine(SetOperation operation, RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// This command is equal to SetCombine, but instead of returning the resulting set, it is stored in destination. If destination already exists, it is overwritten.
        /// </summary>
        /// <param name="operation">The operation to perform.</param>
        /// <param name="destination">The key of the destination set.</param>
        /// <param name="first">The key of the first set.</param>
        /// <param name="second">The key of the second set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of elements in the resulting set.</returns>
        /// <remarks>https://redis.io/commands/sunionstore</remarks>
        /// <remarks>https://redis.io/commands/sinterstore</remarks>
        /// <remarks>https://redis.io/commands/sdiffstore</remarks>
        long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// This command is equal to SetCombine, but instead of returning the resulting set, it is stored in destination. If destination already exists, it is overwritten.
        /// </summary>
        /// <param name="operation">The operation to perform.</param>
        /// <param name="destination">The key of the destination set.</param>
        /// <param name="keys">The keys of the sets to operate on.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of elements in the resulting set.</returns>
        /// <remarks>https://redis.io/commands/sunionstore</remarks>
        /// <remarks>https://redis.io/commands/sinterstore</remarks>
        /// <remarks>https://redis.io/commands/sdiffstore</remarks>
        long SetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns if member is a member of the set stored at key.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="value">The value to check for .</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>1 if the element is a member of the set. 0 if the element is not a member of the set, or if key does not exist.</returns>
        /// <remarks>https://redis.io/commands/sismember</remarks>
        bool SetContains(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the set cardinality (number of elements) of the set stored at key.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The cardinality (number of elements) of the set, or 0 if key does not exist.</returns>
        /// <remarks>https://redis.io/commands/scard</remarks>
        long SetLength(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns all the members of the set value stored at key.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>All elements of the set.</returns>
        /// <remarks>https://redis.io/commands/smembers</remarks>
        RedisValue[] SetMembers(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Move member from the set at source to the set at destination. This operation is atomic. In every given moment the element will appear to be a member of source or destination for other clients.
        /// When the specified element already exists in the destination set, it is only removed from the source set.
        /// </summary>
        /// <param name="source">The key of the source set.</param>
        /// <param name="destination">The key of the destination set.</param>
        /// <param name="value">The value to move.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>1 if the element is moved. 0 if the element is not a member of source and no operation was performed.</returns>
        /// <remarks>https://redis.io/commands/smove</remarks>
        bool SetMove(RedisKey source, RedisKey destination, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes and returns a random element from the set value stored at key.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The removed element, or nil when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/spop</remarks>
        RedisValue SetPop(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes and returns the specified number of random elements from the set value stored at key.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="count">The number of elements to return.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>An array of elements, or an empty array when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/spop</remarks>
        RedisValue[] SetPop(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return a random element from the set value stored at key.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The randomly selected element, or nil when key does not exist</returns>
        /// <remarks>https://redis.io/commands/srandmember</remarks>
        RedisValue SetRandomMember(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return an array of count distinct elements if count is positive. If called with a negative count the behavior changes and the command is allowed to return the same element multiple times.
        /// In this case the numer of returned elements is the absolute value of the specified count.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="count">The count of members to get.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>An array of elements, or an empty array when key does not exist</returns>
        /// <remarks>https://redis.io/commands/srandmember</remarks>
        RedisValue[] SetRandomMembers(RedisKey key, long count, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Remove the specified member from the set stored at key.  Specified members that are not a member of this set are ignored.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="value">The value to remove.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the specified member was already present in the set, else False</returns>
        /// <remarks>https://redis.io/commands/srem</remarks>
        bool SetRemove(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Remove the specified members from the set stored at key. Specified members that are not a member of this set are ignored.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="values">The values to remove.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of members that were removed from the set, not including non existing members.</returns>
        /// <remarks>https://redis.io/commands/srem</remarks>
        long SetRemove(RedisKey key, RedisValue[] values, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The SSCAN command is used to incrementally iterate over set
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="pattern">The pattern to match.</param>
        /// <param name="pageSize">The page size to iterate by.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>Yields all matching elements of the set.</returns>
        /// <remarks>https://redis.io/commands/sscan</remarks>
        IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags);

        /// <summary>
        /// The SSCAN command is used to incrementally iterate over set; note: to resume an iteration via <i>cursor</i>, cast the original enumerable or enumerator to <i>IScanningCursor</i>.
        /// </summary>
        /// <param name="key">The key of the set.</param>
        /// <param name="pattern">The pattern to match.</param>
        /// <param name="pageSize">The page size to iterate by.</param>
        /// <param name="cursor">The cursor position to start at.</param>
        /// <param name="pageOffset">The page offset to start at.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>Yields all matching elements of the set.</returns>
        /// <remarks>https://redis.io/commands/sscan</remarks>
        IEnumerable<RedisValue> SetScan(RedisKey key, RedisValue pattern = default(RedisValue), int pageSize = RedisBase.CursorUtils.DefaultPageSize, long cursor = RedisBase.CursorUtils.Origin, int pageOffset = 0, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Sorts a list, set or sorted set (numerically or alphabetically, ascending by default); By default, the elements themselves are compared, but the values can also be
        /// used to perform external key-lookups using the <c>by</c> parameter. By default, the elements themselves are returned, but external key-lookups (one or many) can
        /// be performed instead by specifying the <c>get</c> parameter (note that <c>#</c> specifies the element itself, when used in <c>get</c>).
        /// Referring to the <a href="https://redis.io/commands/sort">redis SORT documentation </a> for examples is recommended. When used in hashes, <c>by</c> and <c>get</c>
        /// can be used to specify fields using <c>-&gt;</c> notation (again, refer to redis documentation).
        /// </summary>
        /// <param name="key">The key of the list, set, or sorted set.</param>
        /// <param name="skip">How many entries to skip on the return.</param>
        /// <param name="take">How many entries to take on the return.</param>
        /// <param name="order">The ascending or descending order (defaults to ascending).</param>
        /// <param name="sortType">The sorting method (defaults to numeric).</param>
        /// <param name="by">The key pattern to sort by, if any. e.g. ExternalKey_* would sort by ExternalKey_{listvalue} as a lookup.</param>
        /// <param name="get">The key pattern to sort by, if any e.g. ExternalKey_* would return the value of ExternalKey_{listvalue} for each entry.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The sorted elements, or the external values if <c>get</c> is specified.</returns>
        /// <remarks>https://redis.io/commands/sort</remarks>
        RedisValue[] Sort(RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default(RedisValue), RedisValue[] get = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Sorts a list, set or sorted set (numerically or alphabetically, ascending by default); By default, the elements themselves are compared, but the values can also be
        /// used to perform external key-lookups using the <c>by</c> parameter. By default, the elements themselves are returned, but external key-lookups (one or many) can
        /// be performed instead by specifying the <c>get</c> parameter (note that <c>#</c> specifies the element itself, when used in <c>get</c>).
        /// Referring to the <a href="https://redis.io/commands/sort">redis SORT documentation </a> for examples is recommended. When used in hashes, <c>by</c> and <c>get</c>
        /// can be used to specify fields using <c>-&gt;</c> notation (again, refer to redis documentation).
        /// </summary>
        /// <param name="destination">The destination key to store results in.</param>
        /// <param name="key">The key of the list, set, or sorted set.</param>
        /// <param name="skip">How many entries to skip on the return.</param>
        /// <param name="take">How many entries to take on the return.</param>
        /// <param name="order">The ascending or descending order (defaults to ascending).</param>
        /// <param name="sortType">The sorting method (defaults to numeric).</param>
        /// <param name="by">The key pattern to sort by, if any. e.g. ExternalKey_* would sort by ExternalKey_{listvalue} as a lookup.</param>
        /// <param name="get">The key pattern to sort by, if any e.g. ExternalKey_* would return the value of ExternalKey_{listvalue} for each entry.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of elements stored in the new list.</returns>
        /// <remarks>https://redis.io/commands/sort</remarks>
        long SortAndStore(RedisKey destination, RedisKey key, long skip = 0, long take = -1, Order order = Order.Ascending, SortType sortType = SortType.Numeric, RedisValue by = default(RedisValue), RedisValue[] get = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Adds the specified member with the specified score to the sorted set stored at key. If the specified member is already a member of the sorted set, the score is updated and the element reinserted at the right position to ensure the correct ordering.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="member">The member to add to the sorted set.</param>
        /// <param name="score">The score for the member to add to the sorted set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the value was added, False if it already existed (the score is still updated)</returns>
        /// <remarks>https://redis.io/commands/zadd</remarks>
        bool SortedSetAdd(RedisKey key, RedisValue member, double score, CommandFlags flags);

        /// <summary>
        /// Adds the specified member with the specified score to the sorted set stored at key. If the specified member is already a member of the sorted set, the score is updated and the element reinserted at the right position to ensure the correct ordering.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="member">The member to add to the sorted set.</param>
        /// <param name="score">The score for the member to add to the sorted set.</param>
        /// <param name="when">What conditions to add the element under (defaults to always).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the value was added, False if it already existed (the score is still updated)</returns>
        /// <remarks>https://redis.io/commands/zadd</remarks>
        bool SortedSetAdd(RedisKey key, RedisValue member, double score, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Adds all the specified members with the specified scores to the sorted set stored at key. If a specified member is already a member of the sorted set, the score is updated and the element reinserted at the right position to ensure the correct ordering.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="values">The members and vlues to add to the sorted set.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of elements added to the sorted sets, not including elements already existing for which the score was updated.</returns>
        /// <remarks>https://redis.io/commands/zadd</remarks>
        long SortedSetAdd(RedisKey key, SortedSetEntry[] values, CommandFlags flags);

        /// <summary>
        /// Adds all the specified members with the specified scores to the sorted set stored at key. If a specified member is already a member of the sorted set, the score is updated and the element reinserted at the right position to ensure the correct ordering.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="values">The members and vlues to add to the sorted set.</param>
        /// <param name="when">What conditions to add the element under (defaults to always).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of elements added to the sorted sets, not including elements already existing for which the score was updated.</returns>
        /// <remarks>https://redis.io/commands/zadd</remarks>
        long SortedSetAdd(RedisKey key, SortedSetEntry[] values, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Computes a set operation over two sorted sets, and stores the result in destination, optionally performing 
        /// a specific aggregation (defaults to sum).
        /// </summary>
        /// <param name="operation">The operation to perform.</param>
        /// <param name="destination">The key to store the results in.</param>
        /// <param name="first">The key of the first sorted set.</param>
        /// <param name="second">The key of the second sorted set.</param>
        /// <param name="aggregate">The aggregation method (defaults to sum).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>https://redis.io/commands/zunionstore</remarks>
        /// <remarks>https://redis.io/commands/zinterstore</remarks>
        /// <returns>the number of elements in the resulting sorted set at destination</returns>
        long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey first, RedisKey second, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Computes a set operation over multiple sorted sets (optionally using per-set weights), and stores the result in destination, optionally performing 
        /// a specific aggregation (defaults to sum).
        /// </summary>
        /// <param name="operation">The operation to perform.</param>
        /// <param name="destination">The key to store the results in.</param>
        /// <param name="keys">The keys of the sorted sets.</param>
        /// <param name="weights">The optional weights per set that correspond to <paramref name="keys"/>.</param>
        /// <param name="aggregate">The aggregation method (defaults to sum).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>https://redis.io/commands/zunionstore</remarks>
        /// <remarks>https://redis.io/commands/zinterstore</remarks>
        /// <returns>the number of elements in the resulting sorted set at destination</returns>
        long SortedSetCombineAndStore(SetOperation operation, RedisKey destination, RedisKey[] keys, double[] weights = null, Aggregate aggregate = Aggregate.Sum, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Decrements the score of member in the sorted set stored at key by decrement. If member does not exist in the sorted set, it is added with -decrement as its score (as if its previous score was 0.0).
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="member">The member to decrement.</param>
        /// <param name="value">The amount to decrement by.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The new score of member.</returns>
        /// <remarks>https://redis.io/commands/zincrby</remarks>
        double SortedSetDecrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Increments the score of member in the sorted set stored at key by increment. If member does not exist in the sorted set, it is added with increment as its score (as if its previous score was 0.0).
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="member">The member to increment.</param>
        /// <param name="value">The amount to increment by.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The new score of member.</returns>
        /// <remarks>https://redis.io/commands/zincrby</remarks>
        double SortedSetIncrement(RedisKey key, RedisValue member, double value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the sorted set cardinality (number of elements) of the sorted set stored at key.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="min">The min score to filter by (defaults to negative infinity).</param>
        /// <param name="max">The max score to filter by (defaults to positive infinity).</param>
        /// <param name="exclude">Whether to exclude <paramref name="min"/> and <paramref name="max"/> from the range check (defaults to both inclusive).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The cardinality (number of elements) of the sorted set, or 0 if key does not exist.</returns>
        /// <remarks>https://redis.io/commands/zcard</remarks>
        long SortedSetLength(RedisKey key, double min = double.NegativeInfinity, double max = double.PositiveInfinity, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// When all the elements in a sorted set are inserted with the same score, in order to force lexicographical ordering, this command returns the number of elements in the sorted set at key with a value between min and max.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="min">The min value to filter by.</param>
        /// <param name="max">The max value to filter by.</param>
        /// <param name="exclude">Whether to exclude <paramref name="min"/> and <paramref name="max"/> from the range check (defaults to both inclusive).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of elements in the specified score range.</returns>
        /// <remarks>https://redis.io/commands/zlexcount</remarks>
        long SortedSetLengthByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the specified range of elements in the sorted set stored at key. By default the elements are considered to be ordered from the lowest to the highest score. Lexicographical order is used for elements with equal score.
        /// Both start and stop are zero-based indexes, where 0 is the first element, 1 is the next element and so on. They can also be negative numbers indicating offsets from the end of the sorted set, with -1 being the last element of the sorted set, -2 the penultimate element and so on.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="start">The start index to get.</param>
        /// <param name="stop">The stop index to get.</param>
        /// <param name="order">The order to sort by (defaults to ascending).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>List of elements in the specified range.</returns>
        /// <remarks>https://redis.io/commands/zrange</remarks>
        /// <remarks>https://redis.io/commands/zrevrange</remarks>
        RedisValue[] SortedSetRangeByRank(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the specified range of elements in the sorted set stored at key. By default the elements are considered to be ordered from the lowest to the highest score. Lexicographical order is used for elements with equal score.
        /// Both start and stop are zero-based indexes, where 0 is the first element, 1 is the next element and so on. They can also be negative numbers indicating offsets from the end of the sorted set, with -1 being the last element of the sorted set, -2 the penultimate element and so on.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="start">The start index to get.</param>
        /// <param name="stop">The stop index to get.</param>
        /// <param name="order">The order to sort by (defaults to ascending).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>List of elements in the specified range.</returns>
        /// <remarks>https://redis.io/commands/zrange</remarks>
        /// <remarks>https://redis.io/commands/zrevrange</remarks>
        SortedSetEntry[] SortedSetRangeByRankWithScores(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the specified range of elements in the sorted set stored at key. By default the elements are considered to be ordered from the lowest to the highest score. Lexicographical order is used for elements with equal score.
        /// Start and stop are used to specify the min and max range for score values. Similar to other range methods the values are inclusive.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="start">The minimum score to filter by.</param>
        /// <param name="stop">The maximum score to filter by.</param>
        /// <param name="exclude">Which of <paramref name="start"/> and <paramref name="stop"/> to exclude (defaults to both inclusive).</param>
        /// <param name="order">The order to sort by (defaults to ascending).</param>
        /// <param name="skip">How many items to skip.</param>
        /// <param name="take">How many items to take.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>List of elements in the specified score range.</returns>
        /// <remarks>https://redis.io/commands/zrangebyscore</remarks>
        /// <remarks>https://redis.io/commands/zrevrangebyscore</remarks>
        RedisValue[] SortedSetRangeByScore(RedisKey key,
            double start = double.NegativeInfinity,
            double stop = double.PositiveInfinity,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the specified range of elements in the sorted set stored at key. By default the elements are considered to be ordered from the lowest to the highest score. Lexicographical order is used for elements with equal score.
        /// Start and stop are used to specify the min and max range for score values. Similar to other range methods the values are inclusive.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="start">The minimum score to filter by.</param>
        /// <param name="stop">The maximum score to filter by.</param>
        /// <param name="exclude">Which of <paramref name="start"/> and <paramref name="stop"/> to exclude (defaults to both inclusive).</param>
        /// <param name="order">The order to sort by (defaults to ascending).</param>
        /// <param name="skip">How many items to skip.</param>
        /// <param name="take">How many items to take.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>List of elements in the specified score range.</returns>
        /// <remarks>https://redis.io/commands/zrangebyscore</remarks>
        /// <remarks>https://redis.io/commands/zrevrangebyscore</remarks>
        SortedSetEntry[] SortedSetRangeByScoreWithScores(RedisKey key,
            double start = double.NegativeInfinity,
            double stop = double.PositiveInfinity,
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// When all the elements in a sorted set are inserted with the same score, in order to force lexicographical ordering, this command returns all the elements in the sorted set at key with a value between min and max.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="min">The min value to filter by.</param>
        /// <param name="max">The max value to filter by.</param>
        /// <param name="exclude">Which of <paramref name="min"/> and <paramref name="max"/> to exclude (defaults to both inclusive).</param>
        /// <param name="skip">How many items to skip.</param>
        /// <param name="take">How many items to take.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>https://redis.io/commands/zrangebylex</remarks>
        /// <returns>list of elements in the specified score range.</returns>
        RedisValue[] SortedSetRangeByValue(RedisKey key,
            RedisValue min,
            RedisValue max,
            Exclude exclude,
            long skip,
            long take = -1,
            CommandFlags flags = CommandFlags.None); // defaults removed to avoid ambiguity with overload with order

        /// <summary>
        /// When all the elements in a sorted set are inserted with the same score, in order to force lexicographical ordering, this command returns all the elements in the sorted set at key with a value between min and max.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="min">The min value to filter by.</param>
        /// <param name="max">The max value to filter by.</param>
        /// <param name="exclude">Which of <paramref name="min"/> and <paramref name="max"/> to exclude (defaults to both inclusive).</param>
        /// <param name="order">Whether to order the data ascending or descending</param>
        /// <param name="skip">How many items to skip.</param>
        /// <param name="take">How many items to take.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <remarks>https://redis.io/commands/zrangebylex</remarks>
        /// <remarks>https://redis.io/commands/zrevrangebylex</remarks>
        /// <returns>list of elements in the specified score range.</returns>
        RedisValue[] SortedSetRangeByValue(RedisKey key,
            RedisValue min = default(RedisValue),
            RedisValue max = default(RedisValue),
            Exclude exclude = Exclude.None,
            Order order = Order.Ascending,
            long skip = 0,
            long take = -1,
            CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the rank of member in the sorted set stored at key, by default with the scores ordered from low to high. The rank (or index) is 0-based, which means that the member with the lowest score has rank 0.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="member">The member to get the rank of.</param>
        /// <param name="order">The order to sort by (defaults to ascending).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>If member exists in the sorted set, the rank of member; If member does not exist in the sorted set or key does not exist, null</returns>
        /// <remarks>https://redis.io/commands/zrank</remarks>
        /// <remarks>https://redis.io/commands/zrevrank</remarks>
        long? SortedSetRank(RedisKey key, RedisValue member, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes the specified member from the sorted set stored at key. Non existing members are ignored.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="member">The member to remove.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the member existed in the sorted set and was removed; False otherwise.</returns>
        /// <remarks>https://redis.io/commands/zrem</remarks>
        bool SortedSetRemove(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes the specified members from the sorted set stored at key. Non existing members are ignored.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="members">The members to remove.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of members removed from the sorted set, not including non existing members.</returns>
        /// <remarks>https://redis.io/commands/zrem</remarks>
        long SortedSetRemove(RedisKey key, RedisValue[] members, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes all elements in the sorted set stored at key with rank between start and stop. Both start and stop are 0 -based indexes with 0 being the element with the lowest score. These indexes can be negative numbers, where they indicate offsets starting at the element with the highest score. For example: -1 is the element with the highest score, -2 the element with the second highest score and so forth.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="start">The minimum rank to remove.</param>
        /// <param name="stop">The maximum rank to remove.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of elements removed.</returns>
        /// <remarks>https://redis.io/commands/zremrangebyrank</remarks>
        long SortedSetRemoveRangeByRank(RedisKey key, long start, long stop, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes all elements in the sorted set stored at key with a score between min and max (inclusive by default).
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="start">The minimum score to remove.</param>
        /// <param name="stop">The maximum score to remove.</param>
        /// <param name="exclude">Which of <paramref name="start"/> and <paramref name="stop"/> to exclude (defaults to both inclusive).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of elements removed.</returns>
        /// <remarks>https://redis.io/commands/zremrangebyscore</remarks>
        long SortedSetRemoveRangeByScore(RedisKey key, double start, double stop, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// When all the elements in a sorted set are inserted with the same score, in order to force lexicographical ordering, this command removes all elements in the sorted set stored at key between the lexicographical range specified by min and max.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="min">The minimum value to remove.</param>
        /// <param name="max">The maximum value to remove.</param>
        /// <param name="exclude">Which of <paramref name="min"/> and <paramref name="max"/> to exclude (defaults to both inclusive).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>the number of elements removed.</returns>
        /// <remarks>https://redis.io/commands/zremrangebylex</remarks>
        long SortedSetRemoveRangeByValue(RedisKey key, RedisValue min, RedisValue max, Exclude exclude = Exclude.None, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// The ZSCAN command is used to incrementally iterate over a sorted set
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="pattern">The pattern to match.</param>
        /// <param name="pageSize">The page size to iterate by.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>Yields all matching elements of the sorted set.</returns>
        /// <remarks>https://redis.io/commands/zscan</remarks>
        IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key, RedisValue pattern, int pageSize, CommandFlags flags);

        /// <summary>
        /// The ZSCAN command is used to incrementally iterate over a sorted set; note: to resume an iteration via <i>cursor</i>, cast the original enumerable or enumerator to <i>IScanningCursor</i>.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="pattern">The pattern to match.</param>
        /// <param name="pageSize">The page size to iterate by.</param>
        /// <param name="cursor">The cursor position to start at.</param>
        /// <param name="pageOffset">The page offset to start at.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>Yields all matching elements of the sorted set.</returns>
        /// <remarks>https://redis.io/commands/zscan</remarks>
        IEnumerable<SortedSetEntry> SortedSetScan(RedisKey key,
            RedisValue pattern = default(RedisValue),
            int pageSize = RedisBase.CursorUtils.DefaultPageSize,
            long cursor = RedisBase.CursorUtils.Origin,
            int pageOffset = 0,
            CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the score of member in the sorted set at key; If member does not exist in the sorted set, or key does not exist, nil is returned.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="member">The member to get a score for.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The score of the member.</returns>
        /// <remarks>https://redis.io/commands/zscore</remarks>
        double? SortedSetScore(RedisKey key, RedisValue member, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes and returns the first element from the sorted set stored at key, by default with the scores ordered from low to high.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="order">The order to sort by (defaults to ascending).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The removed element, or nil when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/zpopmin</remarks>
        /// <remarks>https://redis.io/commands/zpopmax</remarks>
        SortedSetEntry? SortedSetPop(RedisKey key, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Removes and returns the specified number of first elements from the sorted set stored at key, by default with the scores ordered from low to high.
        /// </summary>
        /// <param name="key">The key of the sorted set.</param>
        /// <param name="count">The number of elements to return.</param>
        /// <param name="order">The order to sort by (defaults to ascending).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>An array of elements, or an empty array when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/zpopmin</remarks>
        /// <remarks>https://redis.io/commands/zpopmax</remarks>
        SortedSetEntry[] SortedSetPop(RedisKey key, long count, Order order = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Allow the consumer to mark a pending message as correctly processed. Returns the number of messages acknowledged.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="groupName">The name of the consumer group that received the message.</param>
        /// <param name="messageId">The ID of the message to acknowledge.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of messages acknowledged.</returns>
        /// <remarks>https://redis.io/topics/streams-intro</remarks>
        long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue messageId, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Allow the consumer to mark a pending message as correctly processed. Returns the number of messages acknowledged.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="groupName">The name of the consumer group that received the message.</param>
        /// <param name="messageIds">The IDs of the messages to acknowledge.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of messages acknowledged.</returns>
        /// <remarks>https://redis.io/topics/streams-intro</remarks>
        long StreamAcknowledge(RedisKey key, RedisValue groupName, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Adds an entry using the specified values to the given stream key. If key does not exist, a new key holding a stream is created. The command returns the ID of the newly created stream entry.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="streamField">The field name for the stream entry.</param>
        /// <param name="streamValue">The value to set in the stream entry.</param>
        /// <param name="messageId">The ID to assign to the stream entry, defaults to an auto-generated ID ("*").</param>
        /// <param name="maxLength">The maximum length of the stream.</param>
        /// <param name="useApproximateMaxLength">If true, the "~" argument is used to allow the stream to exceed max length by a small number. This improves performance when removing messages.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The ID of the newly created message.</returns>
        /// <remarks>https://redis.io/commands/xadd</remarks>
        RedisValue StreamAdd(RedisKey key, RedisValue streamField, RedisValue streamValue, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Adds an entry using the specified values to the given stream key. If key does not exist, a new key holding a stream is created. The command returns the ID of the newly created stream entry.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="streamPairs">The fields and their associated values to set in the stream entry.</param>
        /// <param name="messageId">The ID to assign to the stream entry, defaults to an auto-generated ID ("*").</param>
        /// <param name="maxLength">The maximum length of the stream.</param>
        /// <param name="useApproximateMaxLength">If true, the "~" argument is used to allow the stream to exceed max length by a small number. This improves performance when removing messages.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The ID of the newly created message.</returns>
        /// <remarks>https://redis.io/commands/xadd</remarks>
        RedisValue StreamAdd(RedisKey key, NameValueEntry[] streamPairs, RedisValue? messageId = null, int? maxLength = null, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Change ownership of messages consumed, but not yet acknowledged, by a different consumer. This method returns the complete message for the claimed message(s).
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="consumerGroup">The consumer group.</param>
        /// <param name="claimingConsumer">The consumer claiming the given message(s).</param>
        /// <param name="minIdleTimeInMs">The minimum message idle time to allow the reassignment of the message(s).</param>
        /// <param name="messageIds">The IDs of the messages to claim for the given consumer.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The messages successfully claimed by the given consumer.</returns>
        /// <remarks>https://redis.io/topics/streams-intro</remarks>
        StreamEntry[] StreamClaim(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Change ownership of messages consumed, but not yet acknowledged, by a different consumer. This method returns the IDs for the claimed message(s).
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="consumerGroup">The consumer group.</param>
        /// <param name="claimingConsumer">The consumer claiming the given message(s).</param>
        /// <param name="minIdleTimeInMs">The minimum message idle time to allow the reassignment of the message(s).</param>
        /// <param name="messageIds">The IDs of the messages to claim for the given consumer.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The message IDs for the messages successfully claimed by the given consumer.</returns>
        /// <remarks>https://redis.io/topics/streams-intro</remarks>
        RedisValue[] StreamClaimIdsOnly(RedisKey key, RedisValue consumerGroup, RedisValue claimingConsumer, long minIdleTimeInMs, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Set the position from which to read a stream for a consumer group.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="groupName">The name of the consumer group.</param>
        /// <param name="position">The position from which to read for the consumer group.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if successful, otherwise false.</returns>
        bool StreamConsumerGroupSetPosition(RedisKey key, RedisValue groupName, RedisValue position, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Retrieve information about the consumers for the given consumer group. This is the equivalent of calling "XINFO GROUPS key group".
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="groupName">The consumer group name.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>An instance of <see cref="Redis.StreamConsumerInfo"/> for each of the consumer group's consumers.</returns>
        /// <remarks>https://redis.io/topics/streams-intro</remarks>
        StreamConsumerInfo[] StreamConsumerInfo(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Create a consumer group for the given stream.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="groupName">The name of the group to create.</param>
        /// <param name="position">The position to begin reading the stream. Defaults to <see cref="StreamPosition.NewMessages"/>.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the group was created.</returns>
        /// <remarks>https://redis.io/topics/streams-intro</remarks>
        bool StreamCreateConsumerGroup(RedisKey key, RedisValue groupName, RedisValue? position = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Delete messages in the stream. This method does not delete the stream.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="messageIds">The IDs of the messages to delete.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>Returns the number of messages successfully deleted from the stream.</returns>
        /// <remarks>https://redis.io/topics/streams-intro</remarks>
        long StreamDelete(RedisKey key, RedisValue[] messageIds, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Delete a consumer from a consumer group.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="groupName">The name of the consumer group.</param>
        /// <param name="consumerName">The name of the consumer.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of messages that were pending for the deleted consumer.</returns>
        long StreamDeleteConsumer(RedisKey key, RedisValue groupName, RedisValue consumerName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Delete a consumer group.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="groupName">The name of the consumer group.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if deleted, otherwise false.</returns>
        bool StreamDeleteConsumerGroup(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Retrieve information about the groups created for the given stream. This is the equivalent of calling "XINFO GROUPS key".
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>An instance of <see cref="Redis.StreamGroupInfo"/> for each of the stream's groups.</returns>
        /// <remarks>https://redis.io/topics/streams-intro</remarks>
        StreamGroupInfo[] StreamGroupInfo(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Retrieve information about the given stream. This is the equivalent of calling "XINFO STREAM key".
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>A <see cref="Redis.StreamInfo"/> instance with information about the stream.</returns>
        /// <remarks>https://redis.io/topics/streams-intro</remarks>
        StreamInfo StreamInfo(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the number of entries in a stream.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of entries inside the given stream.</returns>
        /// <remarks>https://redis.io/commands/xlen</remarks>
        long StreamLength(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// View information about pending messages for a stream.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="groupName">The name of the consumer group</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>An instance of <see cref="StreamPendingInfo"/>. <see cref="StreamPendingInfo"/> contains the number of pending messages, the highest and lowest ID of the pending messages, and the consumers with their pending message count.</returns>
        /// <remarks>The equivalent of calling XPENDING key group.</remarks>
        /// <remarks>https://redis.io/commands/xpending</remarks>
        StreamPendingInfo StreamPending(RedisKey key, RedisValue groupName, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// View information about each pending message.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="groupName">The name of the consumer group.</param>
        /// <param name="count">The maximum number of pending messages to return.</param>
        /// <param name="consumerName">The consumer name for the pending messages. Pass RedisValue.Null to include pending messages for all consumers.</param>
        /// <param name="minId">The minimum ID from which to read the stream of pending messages. The method will default to reading from the beginning of the stream.</param>
        /// <param name="maxId">The maximum ID to read to within the stream of pending messages. The method will default to reading to the end of the stream.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>An instance of <see cref="StreamPendingMessageInfo"/> for each pending message.</returns>
        /// <remarks>Equivalent of calling XPENDING key group start-id end-id count consumer-name.</remarks>
        /// <remarks>https://redis.io/commands/xpending</remarks>
        StreamPendingMessageInfo[] StreamPendingMessages(RedisKey key, RedisValue groupName, int count, RedisValue consumerName, RedisValue? minId = null, RedisValue? maxId = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Read a stream using the given range of IDs.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="minId">The minimum ID from which to read the stream. The method will default to reading from the beginning of the stream.</param>
        /// <param name="maxId">The maximum ID to read to within the stream. The method will default to reading to the end of the stream.</param>
        /// <param name="count">The maximum number of messages to return.</param>
        /// <param name="messageOrder">The order of the messages. <see cref="Order.Ascending"/> will execute XRANGE and <see cref="Order.Descending"/> wil execute XREVRANGE.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>Returns an instance of <see cref="StreamEntry"/> for each message returned.</returns>
        /// <remarks>https://redis.io/commands/xrange</remarks>
        StreamEntry[] StreamRange(RedisKey key, RedisValue? minId = null, RedisValue? maxId = null, int? count = null, Order messageOrder = Order.Ascending, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Read from a single stream.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="position">The position from which to read the stream.</param>
        /// <param name="count">The maximum number of messages to return.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>Returns a value of <see cref="StreamEntry"/> for each message returned.</returns>
        /// <remarks>Equivalent of calling XREAD COUNT num STREAMS key id.</remarks>
        /// <remarks>https://redis.io/commands/xread</remarks>
        StreamEntry[] StreamRead(RedisKey key, RedisValue position, int? count = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Read from multiple streams.
        /// </summary>
        /// <param name="streamPositions">Array of streams and the positions from which to begin reading for each stream.</param>
        /// <param name="countPerStream">The maximum number of messages to return from each stream.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>A value of <see cref="RedisStream"/> for each stream.</returns>
        /// <remarks>Equivalent of calling XREAD COUNT num STREAMS key1 key2 id1 id2.</remarks>
        /// <remarks>https://redis.io/commands/xread</remarks>
        RedisStream[] StreamRead(StreamPosition[] streamPositions, int? countPerStream = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Read messages from a stream into an associated consumer group.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="groupName">The name of the consumer group.</param>
        /// <param name="consumerName">The consumer name.</param>
        /// <param name="position">The position from which to read the stream. Defaults to <see cref="StreamPosition.NewMessages"/> when null.</param>
        /// <param name="count">The maximum number of messages to return.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>Returns a value of <see cref="StreamEntry"/> for each message returned.</returns>
        /// <remarks>https://redis.io/commands/xreadgroup</remarks>
        StreamEntry[] StreamReadGroup(RedisKey key, RedisValue groupName, RedisValue consumerName, RedisValue? position = null, int? count = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Read from multiple streams into the given consumer group. The consumer group with the given <paramref name="groupName"/>
        /// will need to have been created for each stream prior to calling this method.
        /// </summary>
        /// <param name="streamPositions">Array of streams and the positions from which to begin reading for each stream.</param>
        /// <param name="groupName">The name of the consumer group.</param>
        /// <param name="consumerName"></param>
        /// <param name="countPerStream">The maximum number of messages to return from each stream.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>A value of <see cref="RedisStream"/> for each stream.</returns>
        /// <remarks>Equivalent of calling XREADGROUP GROUP groupName consumerName COUNT countPerStream STREAMS stream1 stream2 id1 id2</remarks>
        /// <remarks>https://redis.io/commands/xreadgroup</remarks>
        RedisStream[] StreamReadGroup(StreamPosition[] streamPositions, RedisValue groupName, RedisValue consumerName, int? countPerStream = null, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Trim the stream to a specified maximum length.
        /// </summary>
        /// <param name="key">The key of the stream.</param>
        /// <param name="maxLength">The maximum length of the stream.</param>
        /// <param name="useApproximateMaxLength">If true, the "~" argument is used to allow the stream to exceed max length by a small number. This improves performance when removing messages.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of messages removed from the stream.</returns>
        /// <remarks>https://redis.io/topics/streams-intro</remarks>
        long StreamTrim(RedisKey key, int maxLength, bool useApproximateMaxLength = false, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// If key already exists and is a string, this command appends the value at the end of the string. If key does not exist it is created and set as an empty string,
        /// so APPEND will be similar to SET in this special case.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="value">The value to append to the string.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The length of the string after the append operation.</returns>
        /// <remarks>https://redis.io/commands/append</remarks>
        long StringAppend(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Count the number of set bits (population counting) in a string.
        /// By default all the bytes contained in the string are examined. It is possible to specify the counting operation only in an interval passing the additional arguments start and end.
        /// Like for the GETRANGE command start and end can contain negative values in order to index bytes starting from the end of the string, where -1 is the last byte, -2 is the penultimate, and so forth.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="start">The start byte to count at.</param>
        /// <param name="end">The end byte to count at.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The number of bits set to 1.</returns>
        /// <remarks>https://redis.io/commands/bitcount</remarks>
        long StringBitCount(RedisKey key, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Perform a bitwise operation between multiple keys (containing string values) and store the result in the destination key.
        /// The BITOP command supports four bitwise operations; note that NOT is a unary operator: the second key should be omitted in this case
        /// and only the first key will be considered.
        /// The result of the operation is always stored at destkey.
        /// </summary>
        /// <param name="operation">The operation to perform.</param>
        /// <param name="destination">The destination key to store the result in.</param>
        /// <param name="first">The first key to get the bit value from.</param>
        /// <param name="second">The second key to get the bit value from.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The size of the string stored in the destination key, that is equal to the size of the longest input string.</returns>
        /// <remarks>https://redis.io/commands/bitop</remarks>
        long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey first, RedisKey second = default(RedisKey), CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Perform a bitwise operation between multiple keys (containing string values) and store the result in the destination key.
        /// The BITOP command supports four bitwise operations; note that NOT is a unary operator.
        /// The result of the operation is always stored at destkey.
        /// </summary>
        /// <param name="operation">The operation to perform.</param>
        /// <param name="destination">The destination key to store the result in.</param>
        /// <param name="keys">The keys to get the bit values from.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The size of the string stored in the destination key, that is equal to the size of the longest input string.</returns>
        /// <remarks>https://redis.io/commands/bitop</remarks>
        long StringBitOperation(Bitwise operation, RedisKey destination, RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Return the position of the first bit set to 1 or 0 in a string.
        /// The position is returned thinking at the string as an array of bits from left to right where the first byte most significant bit is at position 0, the second byte most significant bit is at position 8 and so forth.
        /// An start and end may be specified; these are in bytes, not bits; start and end can contain negative values in order to index bytes starting from the end of the string, where -1 is the last byte, -2 is the penultimate, and so forth.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="bit">True to check for the first 1 bit, false to check for the first 0 bit.</param>
        /// <param name="start">The position to start looking (defaults to 0).</param>
        /// <param name="end">The position to stop looking (defaults to -1, unlimited).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The command returns the position of the first bit set to 1 or 0 according to the request.
        /// If we look for set bits(the bit argument is 1) and the string is empty or composed of just zero bytes, -1 is returned.</returns>
        /// <remarks>https://redis.io/commands/bitpos</remarks>
        long StringBitPosition(RedisKey key, bool bit, long start = 0, long end = -1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Decrements the number stored at key by decrement. If the key does not exist, it is set to 0 before performing the operation.
        /// An error is returned if the key contains a value of the wrong type or contains a string that is not representable as integer. This operation is limited to 64 bit signed integers.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="value">The amount to decrement by (defaults to 1).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value of key after the decrement.</returns>
        /// <remarks>https://redis.io/commands/decrby</remarks>
        /// <remarks>https://redis.io/commands/decr</remarks>
        long StringDecrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Decrements the string representing a floating point number stored at key by the specified decrement. If the key does not exist, it is set to 0 before performing the operation. The precision of the output is fixed at 17 digits after the decimal point regardless of the actual internal precision of the computation.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="value">The amount to decrement by (defaults to 1).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value of key after the decrement.</returns>
        /// <remarks>https://redis.io/commands/incrbyfloat</remarks>
        double StringDecrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Get the value of key. If the key does not exist the special value nil is returned. An error is returned if the value stored at key is not a string, because GET only handles string values.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value of key, or nil when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/get</remarks>
        RedisValue StringGet(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the values of all specified keys. For every key that does not hold a string value or does not exist, the special value nil is returned.
        /// </summary>
        /// <param name="keys">The keys of the strings.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The values of the strings with nil for keys do not exist.</returns>
        /// <remarks>https://redis.io/commands/mget</remarks>
        RedisValue[] StringGet(RedisKey[] keys, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Get the value of key. If the key does not exist the special value nil is returned. An error is returned if the value stored at key is not a string, because GET only handles string values.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value of key, or nil when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/get</remarks>
        Lease<byte> StringGetLease(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the bit value at offset in the string value stored at key.
        /// When offset is beyond the string length, the string is assumed to be a contiguous space with 0 bits.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="offset">The offset in the string to get a bit at.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The bit value stored at offset.</returns>
        /// <remarks>https://redis.io/commands/getbit</remarks>
        bool StringGetBit(RedisKey key, long offset, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the substring of the string value stored at key, determined by the offsets start and end (both are inclusive). Negative offsets can be used in order to provide an offset starting from the end of the string. So -1 means the last character, -2 the penultimate and so forth.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="start">The start index of the substring to get.</param>
        /// <param name="end">The end index of the substring to get.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The substring of the string value stored at key.</returns>
        /// <remarks>https://redis.io/commands/getrange</remarks>
        RedisValue StringGetRange(RedisKey key, long start, long end, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Atomically sets key to value and returns the old value stored at key.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="value">The value to replace the existing value with.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The old value stored at key, or nil when key did not exist.</returns>
        /// <remarks>https://redis.io/commands/getset</remarks>
        RedisValue StringGetSet(RedisKey key, RedisValue value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Get the value of key. If the key does not exist the special value nil is returned. An error is returned if the value stored at key is not a string, because GET only handles string values.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value of key and its expiry, or nil when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/get</remarks>
        RedisValueWithExpiry StringGetWithExpiry(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Increments the number stored at key by increment. If the key does not exist, it is set to 0 before performing the operation. An error is returned if the key contains a value of the wrong type or contains a string that is not representable as integer. This operation is limited to 64 bit signed integers.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="value">The amount to increment by (defaults to 1).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value of key after the increment.</returns>
        /// <remarks>https://redis.io/commands/incrby</remarks>
        /// <remarks>https://redis.io/commands/incr</remarks>
        long StringIncrement(RedisKey key, long value = 1, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Increments the string representing a floating point number stored at key by the specified increment. If the key does not exist, it is set to 0 before performing the operation. The precision of the output is fixed at 17 digits after the decimal point regardless of the actual internal precision of the computation.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="value">The amount to increment by (defaults to 1).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The value of key after the increment.</returns>
        /// <remarks>https://redis.io/commands/incrbyfloat</remarks>
        double StringIncrement(RedisKey key, double value, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Returns the length of the string value stored at key.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>the length of the string at key, or 0 when key does not exist.</returns>
        /// <remarks>https://redis.io/commands/strlen</remarks>
        long StringLength(RedisKey key, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Set key to hold the string value. If key already holds a value, it is overwritten, regardless of its type.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="expiry">The expiry to set.</param>
        /// <param name="when">Which condition to set the value under (detaults to always).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the string was set, false otherwise.</returns>
        /// <remarks>https://redis.io/commands/set</remarks>
        bool StringSet(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Sets the given keys to their respective values. If "not exists" is specified, this will not perform any operation at all even if just a single key already exists.
        /// </summary>
        /// <param name="values">The keys and values to set.</param>
        /// <param name="when">Which condition to set the value under (detaults to always).</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>True if the keys were set, else False</returns>
        /// <remarks>https://redis.io/commands/mset</remarks>
        /// <remarks>https://redis.io/commands/msetnx</remarks>
        bool StringSet(KeyValuePair<RedisKey, RedisValue>[] values, When when = When.Always, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Sets or clears the bit at offset in the string value stored at key.
        /// The bit is either set or cleared depending on value, which can be either 0 or 1. When key does not exist, a new string value is created.The string is grown to make sure it can hold a bit at offset.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="offset">The offset in the string to set <paramref name="bit"/>.</param>
        /// <param name="bit">The bit value to set, true for 1, false for 0.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The original bit value stored at offset.</returns>
        /// <remarks>https://redis.io/commands/setbit</remarks>
        bool StringSetBit(RedisKey key, long offset, bool bit, CommandFlags flags = CommandFlags.None);

        /// <summary>
        /// Overwrites part of the string stored at key, starting at the specified offset, for the entire length of value. If the offset is larger than the current length of the string at key, the string is padded with zero-bytes to make offset fit. Non-existing keys are considered as empty strings, so this command will make sure it holds a string large enough to be able to set value at offset.
        /// </summary>
        /// <param name="key">The key of the string.</param>
        /// <param name="offset">The offset in the string to overwrite.</param>
        /// <param name="value">The value to overwrite with.</param>
        /// <param name="flags">The flags to use for this operation.</param>
        /// <returns>The length of the string after it was modified by the command.</returns>
        /// <remarks>https://redis.io/commands/setrange</remarks>
        RedisValue StringSetRange(RedisKey key, long offset, RedisValue value, CommandFlags flags = CommandFlags.None);
    }
}
