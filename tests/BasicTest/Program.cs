using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using StackExchange.Redis;

#if RESPITE
using RESPite.Resp;
using RESPite.Resp.KeyValueStore;
using RESPite.Transports;
#endif

#pragma warning disable SA1512, SA1005 // to turn individial tests on/off

namespace BasicTest
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
#if DEBUG
            var obj = new RedisBenchmarks();
            await obj.Setup();

            obj.StringSet();
            obj.StringGet();
#if RESPITE
            // check all the ..ctor
            _ = Hashes.HLEN;
            _ = Keys.TYPE;
            _ = Lists.LINDEX;
            _ = Sets.SCARD;
            _ = SortedSets.ZCARD;
            _ = Streams.XLEN;
            _ = Strings.SET;

            //for (int i = 0; i < 1000; i++)
            {
                obj.StringGet_RESPite();
                obj.StringSet_RESPite();
            }
#endif
            Console.WriteLine("ok!");
#else
            await Task.Delay(0);
            BenchmarkSwitcher.FromAssembly(typeof(Program).GetType().Assembly).Run(args);
#endif
        }
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

            AddJob(Configure(Job.Default.WithRuntime(ClrRuntime.Net472)));
            AddJob(Configure(Job.Default.WithRuntime(CoreRuntime.Core60)));
            AddJob(Configure(Job.Default.WithRuntime(CoreRuntime.CreateForNewVersion("net9.0", ".NET 9.0"))));
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

#if RESPITE
        private IMessageTransport transport;
#endif

        [GlobalSetup]
        public async Task Setup()
        {
            // Pipelines.Sockets.Unofficial.SocketConnection.AssertDependencies();
            var options = ConfigurationOptions.Parse("127.0.0.1:6379");
            connection = await ConnectionMultiplexer.ConnectAsync(options);
            db = connection.GetDatabase(3);

#if RESPITE
            transport = await RespiteConnect.ConnectAsync("127.0.0.1", 6379, tls: false);
#endif

            db.KeyDelete(GeoKey);
            db.GeoAdd(GeoKey, 13.361389, 38.115556, "Palermo ");
            db.GeoAdd(GeoKey, 15.087269, 37.502669, "Catania");

            db.KeyDelete(HashKey);
            for (int i = 0; i < 1000; i++)
            {
                db.HashSet(HashKey, i, i);
            }
        }

        private static readonly RedisKey GeoKey = "GeoTest", IncrByKey = "counter", StringKey = "string", HashKey = "hash";

#if RESPITE
        private static readonly SimpleString SimpleStringKey = "string";
#endif
        void IDisposable.Dispose()
        {
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
                db.StringSet(StringKey, "hey");
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
                db.StringGet(StringKey);
            }
        }

#if RESPITE
        /// <summary>
        /// Run StringSet lots of times.
        /// </summary>
        [Benchmark(Description = "StringSet/s (RESPite)", OperationsPerInvoke = COUNT)]
        public void StringSet_RESPite()
        {
            for (int i = 0; i < COUNT; i++)
            {
                Strings.SET.Send(transport, (SimpleStringKey, "hey"));
            }
        }

        /// <summary>
        /// Run StringGet lots of times.
        /// </summary>
        [Benchmark(Description = "StringGet/s (RESPite)", OperationsPerInvoke = COUNT)]
        public void StringGet_RESPite()
        {
            for (int i = 0; i < COUNT; i++)
            {
                Strings.GET.Send(transport, SimpleStringKey).Dispose();
            }
        }
#endif

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
