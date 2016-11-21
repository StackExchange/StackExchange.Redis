using System;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Redis;

namespace Saxo.RedisCache
{
    internal class StackexchangeRedisImplementation : IRedisImplementation, IDisposable
    {
        private readonly IRedisCacheSettings _settings;

        private ConfigurationOptions ConfigurationOptions
        {
            get
            {
                var configurationOptions = ConfigurationOptions.Parse(_settings.ServerAddress);
                configurationOptions.AbortOnConnectFail = false;
                configurationOptions.KeepAlive = 180;
                configurationOptions.Ssl = false;
                return configurationOptions;
            }
        }
        
        public StackexchangeRedisImplementation(IRedisCacheSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            _settings = settings;
        }
        
        private static volatile ConnectionMultiplexer _connectionMultiplexer;
        private static readonly object ConnectionLock = new object();

        private ConnectionMultiplexer ConnectionMultiplexer
        {
            get
            {
                if (_connectionMultiplexer != null)
                {
                    return _connectionMultiplexer;
                }
                lock (ConnectionLock)
                {
                    if (_connectionMultiplexer != null)
                    {
                        return _connectionMultiplexer;
                    }
                    _connectionMultiplexer = ConnectionMultiplexer.Connect(ConfigurationOptions);
                }
                return _connectionMultiplexer;
            }
        }
        
        public IDatabase Database => ConnectionMultiplexer.GetDatabase();

        public bool IsAlive()
        {
            return ConnectionMultiplexer.IsConnected;
        }

        public void StringSet(RedisKey primaryKey, RedisValue value, TimeSpan? expire = null)
        {            
            Database.StringSet(primaryKey, value, expire);
        }

        public void Clear()
        {
            var endpoints = ConnectionMultiplexer.GetEndPoints(true);
            foreach (var endpoint in endpoints)
            {
                var server = ConnectionMultiplexer.GetServer(endpoint);
                server.FlushAllDatabases();
            }
        }

        public void StringSet(KeyValuePair<RedisKey, RedisValue>[] keyValueArray, TimeSpan? expire = null)
        {
            Database.StringSet(keyValueArray);
            keyValueArray.ToList().ForEach(keyValue =>
            {
                Database.KeyExpire(keyValue.Key, expire);
            });            
        }

        public RedisValue StringGet(RedisKey primaryKey)
        {
            return Database.StringGet(primaryKey);
        }

        public RedisValue[] StringGet(RedisKey[] primaryKeys)
        {
            return Database.StringGet(primaryKeys);
        }

        public void KeyDelete(RedisKey primaryKey)
        {
            Database.KeyDelete(primaryKey);
        }

        public void KeyDelete(RedisKey[] primaryKeys)
        {
            Database.KeyDelete(primaryKeys);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ConnectionMultiplexer?.Dispose();
            }
        }
    }
}
