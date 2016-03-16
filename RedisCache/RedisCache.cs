using System;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Redis;

namespace RedisCache
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

        /// <summary>
        /// Add a serialized value to the redis database with a given key. The key is assumed to contain a primary key.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void Add(RedisCacheKey key, string value)
        {
            if (!key.HasSecondaryKey)
            {
                _cache.StringSet(key.PrimaryKey, value);
            }
            else
            {
                AddAll(new List<RedisCacheKey> { key }, new List<string> { value });
            }
        }

        /// <summary>
        /// Get a serialized value from the database corresponding to a given key. Cache misses are represented with the default value.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string Get(RedisCacheKey key)
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
        public void AddAll(List<RedisCacheKey> keys, List<string> values)
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
        public List<string> GetAll(List<RedisCacheKey> keys)
        {
            var primaryKeys = RetrievePrimaryKeys(keys);

            var res = _cache.StringGet(primaryKeys.Where(key => !string.IsNullOrEmpty(key)).Select(key => (RedisKey) key).ToArray()).Select(r => (string) r).ToList();
            var nres = ReplaceUponCondition(primaryKeys, res, k => !string.IsNullOrEmpty(k));

            return nres;
        }

        /// <summary>
        /// Remove all cache entries corresponding to a list of keys.
        /// </summary>
        /// <param name="keys"></param>
        public void RemoveAll(List<RedisCacheKey> keys)
        {
            var primaryKeys = RetrievePrimaryKeys(keys);

            _cache.KeyDelete(primaryKeys.Where(key => !string.IsNullOrEmpty(key)).Select(key => (RedisKey)key).ToArray());
        }

        /// <summary>
        /// Build a list of secondary-primary key pairs corresponding to a cache key.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private IEnumerable<KeyValuePair<RedisKey, RedisValue>> RetrieveSecondaryKeyPairs(RedisCacheKey key)
        {
            return key.SecondaryKeys.Select(k => new KeyValuePair<RedisKey, RedisValue>(k, key.PrimaryKey));
        }

        /// <summary>
        /// Build a list of key-value pairs corresponding to a list of keys and a list of values.
        /// </summary>
        /// <param name="keys"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        private List<KeyValuePair<RedisKey, RedisValue>> ToKeyValuePairList(IEnumerable<string> keys, IEnumerable<string> values)
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
            var keysWithoutPrimary = keys.Where(key => !key.HasPrimaryKey);
            var secondaryKeysToLookup = keysWithoutPrimary.Select(key => (RedisKey) key.SecondaryKeys.First()).ToArray();
            var foundPrimarykeys = _cache.StringGet(secondaryKeysToLookup).Select(r => (string)r);

            return ReplaceUponCondition(keys.Select(key => key.PrimaryKey), foundPrimarykeys.ToList(), string.IsNullOrEmpty);
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
    }
}
