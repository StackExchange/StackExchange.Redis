using System;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Redis;

namespace Saxo.RedisCache
{
    public class RedisCache
    {
        private readonly IRedisImplementation _cache;

        public RedisCache(IRedisCacheSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            
            _cache = new StackexchangeRedisImplementation(settings);
        }

        public RedisCache(IRedisImplementation cache)
        {
            _cache = cache;
        }

        public void Clear()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Add a serialized value to the redis database with a given key. The key is assumed to contain a primary key.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void Add(RedisCacheKey key, byte[] value)
        {
            if (!key.HasSecondaryKey)
            {
                _cache.StringSet(key.PrimaryKey, value);
            }
            else
            {
                AddAll(new List<RedisCacheKey> { key }, new List<byte[]> { value });
            }
        }

        /// <summary>
        /// Get a serialized value from the database corresponding to a given key. Cache misses are represented with the default value.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public byte[] Get(RedisCacheKey key)
        {
            var primaryKey = RetrievePrimaryKey(key);

            return _cache.StringGet(primaryKey);
        }
        
        /// <summary>
        /// Remove a key-value pair from the database corresponding to a given key.
        /// </summary>
        /// <param name="key"></param>
        public void Remove(RedisCacheKey key)
        {
            var primaryKey = RetrievePrimaryKey(key);

            _cache.KeyDelete(primaryKey);
        }

        /// <summary>
        /// Add all values from a list to the database, using the a list of keys.
        /// </summary>
        /// <param name="keys"></param>
        /// <param name="values"></param>
        public void AddAll(List<RedisCacheKey> keys, List<byte[]> values)
        {
            if (keys.Any(k => !k.HasPrimaryKey))
            {
                throw new RedisCacheException("Value cannot be added without a primary key.");
            }
            
            var entries = ToKeyValuePairList(keys.Select(k => k.PrimaryKey), values);

            foreach (var key in keys)
            {
                entries.AddRange(RetrieveSecondaryKeyPairs(key));
            }

            _cache.StringSet(entries.ToArray());
        }
        
        /// <summary>
        /// Get all values associated with a list of cache keys. Cache misses are represented with the default value.
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public List<byte[]> GetAll(List<RedisCacheKey> keys)
        {
            var primaryKeys = RetrievePrimaryKeys(keys);
            var keysForLookup =
                primaryKeys.Where(key => !string.IsNullOrEmpty(key)).Select(key => (RedisKey) key).ToArray();

            var valuesFromCache = _cache.StringGet(keysForLookup).Select(r => (byte[]) r).ToList();
            var valuesWithMisses = ReplaceWhereNotNull(primaryKeys, valuesFromCache);

            return valuesWithMisses;
        }

        /// <summary>
        /// Remove all cache entries corresponding to a list of keys.
        /// </summary>
        /// <param name="keys"></param>
        public void RemoveAll(List<RedisCacheKey> keys)
        {
            var primaryKeys = RetrievePrimaryKeys(keys);
            var keysForLookup =
                primaryKeys.Where(key => !string.IsNullOrEmpty(key)).Select(key => (RedisKey)key).ToArray();

            _cache.KeyDelete(keysForLookup);
        }

        /// <summary>
        /// Build a list of secondary-primary key pairs corresponding to a cache key.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private IEnumerable<KeyValuePair<RedisKey, RedisValue>> RetrieveSecondaryKeyPairs(RedisCacheKey key)
        {
            return key.SecondaryKeys.Select(secondaryKey => new KeyValuePair<RedisKey, RedisValue>(secondaryKey, key.PrimaryKey));
        }

        /// <summary>
        /// Build a list of key-value pairs corresponding to a list of keys and a list of values.
        /// </summary>
        /// <param name="keys"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        private List<KeyValuePair<RedisKey, RedisValue>> ToKeyValuePairList(IEnumerable<string> keys, IEnumerable<byte[]> values)
        {
            return keys.Zip(values, (k, v) => new KeyValuePair<RedisKey, RedisValue>(k, v)).ToList();
        }

        /// <summary>
        /// Get the primary key corresponding to a cache key, retrieving from the cache if necessary.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private string RetrievePrimaryKey(RedisCacheKey key)
        {
            var primaryKey = key.PrimaryKey;
            if (!key.HasPrimaryKey)
            {
                primaryKey = _cache.StringGet(key.SecondaryKeys.First());
            }
            return primaryKey;
        }

        /// <summary>
        /// Get the primary keys corresponding to a list of cache keys, retrieving from the cache if necessary.
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        private List<string> RetrievePrimaryKeys(List<RedisCacheKey> keys)
        {
            var keysWithMissingPrimary = keys.Where(key => !key.HasPrimaryKey);
            var secondaryKeysToLookup = keysWithMissingPrimary.Select(key => (RedisKey) key.SecondaryKeys.First()).ToArray();

            var foundPrimaryKeys = _cache.StringGet(secondaryKeysToLookup).Select(r => (string)r).ToList();

            return ReplaceUponCondition(keys.Select(key => key.PrimaryKey), foundPrimaryKeys, string.IsNullOrEmpty);
        }

        /// <summary>
        /// Iterate through an enumerable, testing each value against a condition. If the condition is found to hold, the value is replaced with the first element of the replacement list.
        /// </summary>
        /// <param name="primaryList"></param>
        /// <param name="replacementList"></param>
        /// <param name="condition"></param>
        /// <returns></returns>
        private List<string> ReplaceUponCondition(IEnumerable<string> primaryList, List<string> replacementList, Func<string, bool> condition)
        {
            var newList = new List<string>();

            var i = 0;
            foreach (var key in primaryList)
            {
                if (!condition(key))
                {
                    newList.Add(key);
                }
                else
                {
                    newList.Add(replacementList[i]);
                    i++;
                }
            }
            return newList;
        }

        private List<byte[]> ReplaceWhereNotNull(IEnumerable<string> primaryList, List<byte[]> replacementList)
        {
            var newList = new List<byte[]>();

            var i = 0;
            foreach (var key in primaryList)
            {
                if (string.IsNullOrEmpty(key))
                {
                    newList.Add(null);
                }
                else
                {
                    newList.Add(replacementList[i]);
                    i++;
                }
            }
            return newList;
        }
    }
}
