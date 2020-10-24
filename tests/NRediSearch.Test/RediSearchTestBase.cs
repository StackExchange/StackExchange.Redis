using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using StackExchange.Redis;
using StackExchange.Redis.Tests;
using Xunit;
using Xunit.Abstractions;

namespace NRediSearch.Test
{
    [Collection(nameof(NonParallelCollection))]
    public abstract class RediSearchTestBase : IDisposable
    {
        protected readonly ITestOutputHelper Output;
        protected RediSearchTestBase(ITestOutputHelper output)
        {
            muxer = GetWithFT(output);
            Output = output;
            Db = muxer.GetDatabase();
            var server = muxer.GetServer(muxer.GetEndPoints()[0]);
            server.FlushDatabase();
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

        private static bool instanceMissing;

        internal static ConnectionMultiplexer GetWithFT(ITestOutputHelper output)
        {
            var options = new ConfigurationOptions
            {
                EndPoints = { TestConfig.Current.RediSearchServerAndPort },
                AllowAdmin = true,
                ConnectTimeout = 2000,
                SyncTimeout = 15000,
            };
            static void InstanceMissing() => Skip.Inconclusive("NRedisSearch instance available at " + TestConfig.Current.RediSearchServerAndPort);
            // Don't timeout every single test - optimization
            if (instanceMissing)
            {
                InstanceMissing();
            }

            ConnectionMultiplexer conn = null;
            try
            {
                conn = ConnectionMultiplexer.Connect(options);
                conn.MessageFaulted += (msg, ex, origin) => output.WriteLine($"Faulted from '{origin}': '{msg}' - '{(ex == null ? "(null)" : ex.Message)}'");
                conn.Connecting += (e, t) => output.WriteLine($"Connecting to {Format.ToString(e)} as {t}");
                conn.Closing += complete => output.WriteLine(complete ? "Closed" : "Closing...");
            }
            catch (RedisConnectionException)
            {
                instanceMissing = true;
                InstanceMissing();
            }

            // If say we're on a 3.x Redis server...bomb out.
            Skip.IfMissingFeature(conn, nameof(RedisFeatures.Module), r => r.Module);

            var server = conn.GetServer(TestConfig.Current.RediSearchServerAndPort);
            var arr = (RedisResult[])server.Execute("module", "list");
            bool found = false;
            foreach (var module in arr)
            {
                var parsed = Parse(module);
                if (parsed.TryGetValue("name", out var val) && (val == "ft" || val == "search"))
                {
                    found = true;
                    if (parsed.TryGetValue("ver", out val))
                        output?.WriteLine($"Version: {val}");
                    break;
                }
            }

            if (!found)
            {
                output?.WriteLine("Module not found.");
                throw new RedisException("NRedisSearch module missing on " + TestConfig.Current.RediSearchServerAndPort);
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

    [CollectionDefinition(nameof(NonParallelCollection), DisableParallelization = true)]
    public class NonParallelCollection { }
}
