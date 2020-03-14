using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using StackExchange.Redis;
using StackExchange.Redis.Tests;
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

        protected Client GetClient([CallerFilePath] string filePath = null, [CallerMemberName] string caller = null)
        {
            // Remove all that extra pathing
            var offset = filePath?.IndexOf("NRediSearch.Test");
            if (offset > -1)
            {
                filePath = filePath.Substring(offset.Value + "NRediSearch.Test".Length + 1);
            }

            var indexName = $"{filePath}:{caller}";
            Output.WriteLine("Using Index: " + indexName);
            var exists = Db.KeyExists("idx:" + indexName);
            Output.WriteLine("Key existed: " + exists);

            var client = new Client(indexName, Db);
            var wasReset = Reset(client);
            Output.WriteLine("Index was reset?: " + wasReset);
            return client;
        }

        protected bool Reset(Client client)
        {
            Output.WriteLine("Resetting index");
            try
            {
                var result = client.DropIndex(); // tests create them
                Output.WriteLine("  Result: " + result);
                return result;
            }
            catch (RedisServerException ex)
            {
                if (string.Equals("Unknown Index name", ex.Message, StringComparison.InvariantCultureIgnoreCase))
                {
                    Output.WriteLine("  Unknown index name");
                    return true;
                }
                if (string.Equals("no such index", ex.Message, StringComparison.InvariantCultureIgnoreCase))
                {
                    Output.WriteLine("  No such index");
                    return true;
                }
                else
                {
                    throw;
                }
            }
        }

        internal static ConnectionMultiplexer GetWithFT(ITestOutputHelper output)
        {
            var options = new ConfigurationOptions
            {
                EndPoints = { TestConfig.Current.MasterServerAndPort },
                AllowAdmin = true,
                SyncTimeout = 15000,
            };
            var conn = ConnectionMultiplexer.Connect(options);
            conn.MessageFaulted += (msg, ex, origin) => output.WriteLine($"Faulted from '{origin}': '{msg}' - '{(ex == null ? "(null)" : ex.Message)}'");
            conn.Connecting += (e, t) => output.WriteLine($"Connecting to {Format.ToString(e)} as {t}");
            conn.Closing += complete => output.WriteLine(complete ? "Closed" : "Closing...");

            // If say we're on a 3.x Redis server...bomb out.
            Skip.IfMissingFeature(conn, nameof(RedisFeatures.Module), r => r.Module);

            var server = conn.GetServer(TestConfig.Current.MasterServerAndPort);
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
                    }
                    catch (RedisServerException err)
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

        protected bool IsMissingIndexException(Exception ex)
        {
            if (ex.Message == null)
            {
                return false;
            }
            return ex.Message.Contains("Unknown Index name", StringComparison.InvariantCultureIgnoreCase)
                || ex.Message.Contains("no such index", StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
