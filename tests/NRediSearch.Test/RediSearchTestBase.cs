using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using StackExchange.Redis;
using Xunit.Abstractions;

namespace NRediSearch.Test
{
    public abstract class RediSearchTestBase : IDisposable
    {
        protected readonly ITestOutputHelper Output;
        protected RediSearchTestBase(ITestOutputHelper output)
        {
            muxer = GetWithFT(output);
            Output = output;
            Db = muxer.GetDatabase();
        }
        private ConnectionMultiplexer muxer;
        protected IDatabase Db { get; private set; }

        public void Dispose()
        {
            muxer?.Dispose();
            muxer = null;
            Db = null;
        }

        protected Client GetClient([CallerMemberName] string caller = null)
            => Reset(new Client(GetType().Name + ":" + caller, Db));

        protected static Client Reset(Client client)
        {
            try
            {
                client.DropIndex(); // tests create them
            }
            catch (RedisServerException ex)
            {
                if (ex.Message != "Unknown Index name") throw;
            }
            return client;
        }

        internal static ConnectionMultiplexer GetWithFT(ITestOutputHelper output)
        {
            const string ep = "127.0.0.1:6379";
            var options = new ConfigurationOptions
            {
                EndPoints = { ep },
                AllowAdmin = true,
                SyncTimeout = 15000,
            };
            var conn = ConnectionMultiplexer.Connect(options);
            conn.MessageFaulted += (msg, ex, origin) => output.WriteLine($"Faulted from '{origin}': '{msg}' - '{(ex == null ? "(null)" : ex.Message)}'");
            conn.Connecting += (e, t) => output.WriteLine($"Connecting to {Format.ToString(e)} as {t}");
            conn.Closing += complete => output.WriteLine(complete ? "Closed" : "Closing...");

            var server = conn.GetServer(ep);
            var arr = (RedisResult[])server.Execute("module", "list");
            bool found = false;
            foreach (var module in arr)
            {
                var parsed = Parse(module);
                if (parsed.TryGetValue("name", out var val) && val == "ft")
                {
                    found = true;
                    if (parsed.TryGetValue("ver", out val))
                        output?.WriteLine($"Version: {val}");
                    break;
                }
            }

            if (!found)
            {
                output?.WriteLine("Module not found; attempting to load...");
                var config = server.Info("server").SelectMany(_ => _).FirstOrDefault(x => x.Key == "config_file").Value;
                if (!string.IsNullOrEmpty(config))
                {
                    var i = config.LastIndexOf('/');
                    var modulePath = config.Substring(0, i + 1) + "redisearch.so";
                    try
                    {
                        var result = server.Execute("module", "load", modulePath);
                        output?.WriteLine((string)result);
                    } catch(RedisServerException err)
                    {
                        // *probably* duplicate load; we'll try the tests anyways!
                        output?.WriteLine(err.Message);
                    }
                }
            }
            return conn;
        }

        private static Dictionary<string, RedisValue> Parse(RedisResult module)
        {
            var data = new Dictionary<string, RedisValue>();
            var lines = (RedisResult[])module;
            for (int i = 0; i < lines.Length;)
            {
                var key = (string)lines[i++];
                var value = (RedisValue)lines[i++];
                data[key] = value;
            }
            return data;
        }
    }
}
