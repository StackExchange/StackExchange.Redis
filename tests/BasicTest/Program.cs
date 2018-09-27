using System;
using System.Reflection;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess;
using BenchmarkDotNet.Validators;
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
            => j.With(new GcMode { Force = true })
                //.With(InProcessToolchain.Instance)
                ;

        public CustomConfig()
        {
            Add(new MemoryDiagnoser());
            Add(StatisticColumn.OperationsPerSecond);
            Add(JitOptimizationsValidator.FailOnError);

            Add(Configure(Job.Clr));
            Add(Configure(Job.Core));
        }
    }
    internal class SlowConfig : CustomConfig
    {
        protected override Job Configure(Job j)
            => j.WithLaunchCount(1)
                .WithWarmupCount(1)
                .WithIterationCount(5);
        public SlowConfig() { }
    }
    /// <summary>
    /// The tests
    /// </summary>
    [Config(typeof(CustomConfig))]
    public class RedisBenchmarks : IDisposable
    {
        private SocketManager mgr;
        private ConnectionMultiplexer connection;
        private IDatabase db;

        /// <summary>
        /// Create
        /// </summary>
        [GlobalSetup]
        public void Setup()
        {
            // Pipelines.Sockets.Unofficial.SocketConnection.AssertDependencies();

            var options = ConfigurationOptions.Parse("127.0.0.1:6379");
            connection = ConnectionMultiplexer.Connect(options);
            db = connection.GetDatabase(3);

            db.KeyDelete(GeoKey, CommandFlags.FireAndForget);
            db.GeoAdd(GeoKey, 13.361389, 38.115556, "Palermo ");
            db.GeoAdd(GeoKey, 15.087269, 37.502669, "Catania");
        }
        private static readonly RedisKey GeoKey = "GeoTest", IncrByKey = "counter", StringKey = "string";
        void IDisposable.Dispose()
        {
            mgr?.Dispose();
            connection?.Dispose();
            mgr = null;
            db = null;
            connection = null;
        }

        private const int COUNT = 500;

        /// <summary>
        /// Run INCRBY lots of times
        /// </summary>
#if TEST_BASELINE
//        [Benchmark(Description = "INCRBY:v1/s", OperationsPerInvoke = COUNT)]
#else
//        [Benchmark(Description = "INCRBY:v2/s", OperationsPerInvoke = COUNT)]
#endif
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
        /// Run INCRBY lots of times
        /// </summary>
#if TEST_BASELINE
 //       [Benchmark(Description = "INCRBY:v1/a", OperationsPerInvoke = COUNT)]
#else
 //       [Benchmark(Description = "INCRBY:v2/a", OperationsPerInvoke = COUNT)]
#endif
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
        /// Run GEORADIUS lots of times
        /// </summary>
#if TEST_BASELINE
//        [Benchmark(Description = "GEORADIUS:v1/s", OperationsPerInvoke = COUNT)]
#else
//        [Benchmark(Description = "GEORADIUS:v2/s", OperationsPerInvoke = COUNT)]
#endif
        public int ExecuteGeoRadius()
        {
            int total = 0;
            for (int i = 0; i < COUNT; i++)
            {
                var results = db.GeoRadius(GeoKey, 15, 37, 200, GeoUnit.Kilometers,
                    options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithGeoHash);
                total += results.Length;
            }
            return total;
        }

        /// <summary>
        /// Run GEORADIUS lots of times
        /// </summary>
#if TEST_BASELINE
//        [Benchmark(Description = "GEORADIUS:v1/a", OperationsPerInvoke = COUNT)]
#else
//        [Benchmark(Description = "GEORADIUS:v2/a", OperationsPerInvoke = COUNT)]
#endif
        public async Task<int> ExecuteGeoRadiusAsync()
        {
            int total = 0;
            for (int i = 0; i < COUNT; i++)
            {
                var results = await db.GeoRadiusAsync(GeoKey, 15, 37, 200, GeoUnit.Kilometers,
                    options: GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithGeoHash).ConfigureAwait(false);
                total += results.Length;
            }
            return total;
        }

        /// <summary>
        /// Run StringSet lots of times
        /// </summary>
#if TEST_BASELINE
        [Benchmark(Description = "StringSet:v1/a", OperationsPerInvoke = COUNT)]
#else
        [Benchmark(Description = "StringSet:v2/a", OperationsPerInvoke = COUNT)]
#endif
        public void StringSet()
        {
            for (int i = 0; i < COUNT; i++)
            {
                db.StringSet(StringKey, "hey");
            }
        }


        /// <summary>
        /// Run StringGet lots of times
        /// </summary>
#if TEST_BASELINE
        [Benchmark(Description = "StringGet:v1/a", OperationsPerInvoke = COUNT)]
#else
        [Benchmark(Description = "StringGet:v2/a", OperationsPerInvoke = COUNT)]
#endif
        public void StringGet()
        {
            for (int i = 0; i < COUNT; i++)
            {
                db.StringGet(StringKey);
            }
        }
    }
#pragma warning disable CS1591

    [Config(typeof(SlowConfig))]
    public class Issue898 : IDisposable
    {
        private readonly ConnectionMultiplexer mux;
        private readonly IDatabase db;

        public void Dispose() => mux?.Dispose();
        public Issue898()
        {
            mux = ConnectionMultiplexer.Connect("127.0.0.1:6379");
            db = mux.GetDatabase();
        }

        private const int max = 100000;
        [Benchmark(OperationsPerInvoke = max)]
        public void Load()
        {
            for (int i = 0; i < max; ++i)
            {
                db.StringSet(i.ToString(), i);
            }
        }
        [Benchmark(OperationsPerInvoke = max)]
        public async Task LoadAsync()
        {
            for (int i = 0; i < max; ++i)
            {
                await db.StringSetAsync(i.ToString(), i).ConfigureAwait(false);
            }
        }
        [Benchmark(OperationsPerInvoke = max)]
        public void Sample()
        {
            var rnd = new Random();

            for (int i = 0; i < max; ++i)
            {
                var r = rnd.Next(0, max - 1);

                var rv = db.StringGet(r.ToString());
                if (rv != r)
                {
                    throw new Exception($"Unexpected {rv}, expected {r}");
                }
            }
        }

        [Benchmark(OperationsPerInvoke = max)]
        public async Task SampleAsync()
        {
            var rnd = new Random();

            for (int i = 0; i < max; ++i)
            {
                var r = rnd.Next(0, max - 1);

                var rv = await db.StringGetAsync(r.ToString()).ConfigureAwait(false);
                if (rv != r)
                {
                    throw new Exception($"Unexpected {rv}, expected {r}");
                }
            }
        }
    }
}
