using System;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using Resp.RedisCommands;
using StackExchange.Redis;

namespace BasicTest
{
    internal static class Program
    {
        private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
    }
    internal class CustomConfig : ManualConfig
    {
        protected virtual Job Configure(Job j)
            => j.WithGcMode(new GcMode { Force = true })
                // .With(InProcessToolchain.Instance)
                ;

        public CustomConfig()
        {
            AddDiagnoser(MemoryDiagnoser.Default);
            AddColumn(StatisticColumn.OperationsPerSecond);
            AddValidator(JitOptimizationsValidator.FailOnError);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                AddJob(Configure(Job.Default.WithRuntime(ClrRuntime.Net472)));
            }
            AddJob(Configure(Job.Default.WithRuntime(CoreRuntime.Core80)));
        }
    }
    internal class SlowConfig : CustomConfig
    {
        protected override Job Configure(Job j)
            => j.WithLaunchCount(1)
                .WithWarmupCount(1)
                .WithIterationCount(5);
    }

    [Config(typeof(CustomConfig))]
    public class RedisBenchmarks : IDisposable
    {
        private SocketManager mgr;
        private ConnectionMultiplexer connection;
        private IDatabase db;
        private Resp.RespConnectionPool pool;

        [GlobalSetup]
        public void Setup()
        {
            // Pipelines.Sockets.Unofficial.SocketConnection.AssertDependencies();
            pool = new(new IPEndPoint(IPAddress.Loopback, 6379));
            var options = ConfigurationOptions.Parse("127.0.0.1:6379");
            connection = ConnectionMultiplexer.Connect(options);
            db = connection.GetDatabase(3);

            db.KeyDelete(GeoKey);
            db.KeyDelete(StringKey_K);
            db.StringSet(StringKey_K, StringValue_S);
            db.GeoAdd(GeoKey, 13.361389, 38.115556, "Palermo ");
            db.GeoAdd(GeoKey, 15.087269, 37.502669, "Catania");

            db.KeyDelete(HashKey);
            for (int i = 0; i < 1000; i++)
            {
                db.HashSet(HashKey, i, i);
            }
        }

        private const string StringKey_S = "string", StringValue_S = "some suitably non-trivial value";
        private static readonly RedisKey GeoKey = "GeoTest",
            IncrByKey = "counter",
            StringKey_K = StringKey_S,
            HashKey = "hash";
        private static readonly RedisValue StringValue_V = StringValue_S;

        void IDisposable.Dispose()
        {
            pool?.Dispose();
            mgr?.Dispose();
            connection?.Dispose();
            mgr = null;
            db = null;
            connection = null;
            GC.SuppressFinalize(this);
        }

        private const int COUNT = 50;

        /// <summary>
        /// Run INCRBY lots of times.
        /// </summary>
        // [Benchmark(Description = "INCRBY/s", OperationsPerInvoke = COUNT)]
        public int ExecuteIncrBy()
        {
            var rand = new Random(12345);

            db.KeyDelete(IncrByKey, CommandFlags.FireAndForget);
            int expected = 0;
            for (int i = 0; i < COUNT; i++)
            {
                int x = rand.Next(50);
                expected += x;
                db.StringIncrement(IncrByKey, x, CommandFlags.FireAndForget);
            }
            int actual = (int)db.StringGet(IncrByKey);
            if (actual != expected) throw new InvalidOperationException($"expected: {expected}, actual: {actual}");
            return actual;
        }

        /// <summary>
        /// Run INCRBY lots of times.
        /// </summary>
        // [Benchmark(Description = "INCRBY/a", OperationsPerInvoke = COUNT)]
        public async Task<int> ExecuteIncrByAsync()
        {
            var rand = new Random(12345);

            db.KeyDelete(IncrByKey, CommandFlags.FireAndForget);
            int expected = 0;
            for (int i = 0; i < COUNT; i++)
            {
                int x = rand.Next(50);
                expected += x;
                await db.StringIncrementAsync(IncrByKey, x, CommandFlags.FireAndForget).ConfigureAwait(false);
            }
            int actual = (int)await db.StringGetAsync(IncrByKey).ConfigureAwait(false);
            if (actual != expected) throw new InvalidOperationException($"expected: {expected}, actual: {actual}");
            return actual;
        }

        /// <summary>
        /// Run GEORADIUS lots of times.
        /// </summary>
        // [Benchmark(Description = "GEORADIUS/s", OperationsPerInvoke = COUNT)]
        public int ExecuteGeoRadius()
        {
            int total = 0;
            for (int i = 0; i < COUNT; i++)
            {
                var results = db.GeoRadius(GeoKey, 15, 37, 200, GeoUnit.Kilometers, options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithGeoHash);
                total += results.Length;
            }
            return total;
        }

        /// <summary>
        /// Run GEORADIUS lots of times.
        /// </summary>
        // [Benchmark(Description = "GEORADIUS/a", OperationsPerInvoke = COUNT)]
        public async Task<int> ExecuteGeoRadiusAsync()
        {
            int total = 0;
            for (int i = 0; i < COUNT; i++)
            {
                var results = await db.GeoRadiusAsync(GeoKey, 15, 37, 200, GeoUnit.Kilometers, options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithGeoHash).ConfigureAwait(false);
                total += results.Length;
            }
            return total;
        }

        /// <summary>
        /// Run StringSet lots of times.
        /// </summary>
        [Benchmark(Description = "StringSet/s", OperationsPerInvoke = COUNT)]
        public void StringSet()
        {
            for (int i = 0; i < COUNT; i++)
            {
                db.StringSet(StringKey_K, StringValue_V);
            }
        }

        /// <summary>
        /// Run StringGet lots of times.
        /// </summary>
        [Benchmark(Description = "StringGet/s", OperationsPerInvoke = COUNT)]
        public void StringGet()
        {
            for (int i = 0; i < COUNT; i++)
            {
                db.StringGet(StringKey_K);
            }
        }

        /// <summary>
        /// Run StringSet lots of times.
        /// </summary>
        [Benchmark(Description = "C StringSet/s", OperationsPerInvoke = COUNT)]
        public void StringSet_Core()
        {
            using var conn = pool.GetConnection();
            var s = conn.Strings();
            for (int i = 0; i < COUNT; i++)
            {
                s.Set(StringKey_S, StringValue_S);
            }
        }

        /// <summary>
        /// Run StringGet lots of times.
        /// </summary>
        [Benchmark(Description = "C StringGet/s", OperationsPerInvoke = COUNT)]
        public void StringGet_Core()
        {
            using var conn = pool.GetConnection();
            var s = conn.Strings();
            for (int i = 0; i < COUNT; i++)
            {
                s.Get(StringKey_S);
            }
        }

        /// <summary>
        /// Run HashGetAll lots of times.
        /// </summary>
        // [Benchmark(Description = "HashGetAll F+F/s", OperationsPerInvoke = COUNT)]
        public void HashGetAll_FAF()
        {
            for (int i = 0; i < COUNT; i++)
            {
                db.HashGetAll(HashKey, CommandFlags.FireAndForget);
                db.Ping(); // to wait for response
            }
        }

        /// <summary>
        /// Run HashGetAll lots of times.
        /// </summary>
        // [Benchmark(Description = "HashGetAll F+F/a", OperationsPerInvoke = COUNT)]
        public async Task HashGetAllAsync_FAF()
        {
            for (int i = 0; i < COUNT; i++)
            {
                await db.HashGetAllAsync(HashKey, CommandFlags.FireAndForget);
                await db.PingAsync(); // to wait for response
            }
        }
    }

    [Config(typeof(SlowConfig))]
    public class Issue898 : IDisposable
    {
        private readonly ConnectionMultiplexer mux;
        private readonly IDatabase db;

        public void Dispose()
        {
            mux?.Dispose();
            GC.SuppressFinalize(this);
        }
        public Issue898()
        {
            mux = ConnectionMultiplexer.Connect("127.0.0.1:6379");
            db = mux.GetDatabase();
        }

        private const int Max = 100000;
        [Benchmark(OperationsPerInvoke = Max)]
        public void Load()
        {
            for (int i = 0; i < Max; ++i)
            {
                db.StringSet(i.ToString(), i);
            }
        }
        [Benchmark(OperationsPerInvoke = Max)]
        public async Task LoadAsync()
        {
            for (int i = 0; i < Max; ++i)
            {
                await db.StringSetAsync(i.ToString(), i).ConfigureAwait(false);
            }
        }
        [Benchmark(OperationsPerInvoke = Max)]
        public void Sample()
        {
            var rnd = new Random();

            for (int i = 0; i < Max; ++i)
            {
                var r = rnd.Next(0, Max - 1);

                var rv = db.StringGet(r.ToString());
                if (rv != r)
                {
                    throw new Exception($"Unexpected {rv}, expected {r}");
                }
            }
        }

        [Benchmark(OperationsPerInvoke = Max)]
        public async Task SampleAsync()
        {
            var rnd = new Random();

            for (int i = 0; i < Max; ++i)
            {
                var r = rnd.Next(0, Max - 1);

                var rv = await db.StringGetAsync(r.ToString()).ConfigureAwait(false);
                if (rv != r)
                {
                    throw new Exception($"Unexpected {rv}, expected {r}");
                }
            }
        }
    }
}
