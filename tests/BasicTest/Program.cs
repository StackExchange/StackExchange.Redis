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
using Resp;
using Resp.RedisCommands;
using StackExchange.Redis;

namespace BasicTest
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            int port = RespBenchmark.DefaultPort,
                clients = RespBenchmark.DefaultClients,
                requests = RespBenchmark.DefaultRequests,
                pipelineDepth = RespBenchmark.DefaultPipelineDepth;
            string tests = RespBenchmark.DefaultTests;
            bool multiplexed = RespBenchmark.DefaultMultiplexed,
                cancel = RespBenchmark.DefaultCancel,
                loop = false,
                quiet = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-p" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                        port = tmp;
                        break;
                    case "-c" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                        clients = tmp;
                        break;
                    case "-n" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                        requests = tmp;
                        break;
                    case "-P" when i != args.Length - 1 && int.TryParse(args[++i], out int tmp) && tmp > 0:
                        pipelineDepth = tmp;
                        break;
                    case "+m":
                        multiplexed = true;
                        break;
                    case "-m":
                        multiplexed = false;
                        break;
                    case "+x":
                        cancel = true;
                        break;
                    case "-c":
                        cancel = false;
                        break;
                    case "-l":
                        loop = true;
                        break;
                    case "-q":
                        quiet = true;
                        break;
                    case "-t" when i != args.Length - 1:
                        tests = args[++i];
                        break;
                }
            }

            using var bench = new RespBenchmark(
                port: port,
                clients: clients,
                requests: requests,
                pipelineDepth: pipelineDepth,
                multiplexed: multiplexed,
                cancel: cancel,
                tests: tests,
                quiet: quiet);
            await bench.RunAll(loop);
        }
        // private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
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
        private Resp.RespConnectionPool pool, customPool;

        [GlobalSetup]
        public void Setup()
        {
            // Pipelines.Sockets.Unofficial.SocketConnection.AssertDependencies();
            pool = new();
#pragma warning disable CS0618 // Type or member is obsolete
            customPool = new() { UseCustomNetworkStream = true };
#pragma warning restore CS0618 // Type or member is obsolete
            // var options = ConfigurationOptions.Parse("127.0.0.1:6379");
            // connection = ConnectionMultiplexer.Connect(options);
            // db = connection.GetDatabase(3);
            //
            // db.KeyDelete(GeoKey);
            // db.KeyDelete(StringKey_K);
            // db.StringSet(StringKey_K, StringValue_S);
            // db.GeoAdd(GeoKey, 13.361389, 38.115556, "Palermo ");
            // db.GeoAdd(GeoKey, 15.087269, 37.502669, "Catania");
            //
            // db.KeyDelete(HashKey);
            // for (int i = 0; i < 1000; i++)
            // {
            //     db.HashSet(HashKey, i, i);
            // }
        }

        public const string StringKey_S = "string", StringValue_S = "some suitably non-trivial value";

        public static readonly RedisKey GeoKey = "GeoTest",
            IncrByKey = "counter",
            StringKey_K = StringKey_S,
            HashKey = "hash";

        public static readonly RedisValue StringValue_V = StringValue_S;

        void IDisposable.Dispose()
        {
            pool?.Dispose();
            customPool?.Dispose();
            mgr?.Dispose();
            connection?.Dispose();
            mgr = null;
            db = null;
            connection = null;
            GC.SuppressFinalize(this);
        }

        public const int OperationsPerInvoke = 128;

        /// <summary>
        /// Run INCRBY lots of times.
        /// </summary>
        // [Benchmark(Description = "INCRBY/s", OperationsPerInvoke = COUNT)]
        public int ExecuteIncrBy()
        {
            var rand = new Random(12345);

            db.KeyDelete(IncrByKey, CommandFlags.FireAndForget);
            int expected = 0;
            for (int i = 0; i < OperationsPerInvoke; i++)
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
            for (int i = 0; i < OperationsPerInvoke; i++)
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
            const GeoRadiusOptions options = GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance |
                                             GeoRadiusOptions.WithGeoHash;
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                var results = db.GeoRadius(
                    GeoKey,
                    15,
                    37,
                    200,
                    GeoUnit.Kilometers,
                    options: options);
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
            var options = GeoRadiusOptions.WithCoordinates | GeoRadiusOptions.WithDistance |
                          GeoRadiusOptions.WithGeoHash;
            int total = 0;
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                var results = await db.GeoRadiusAsync(
                        GeoKey,
                        15,
                        37,
                        200,
                        GeoUnit.Kilometers,
                        options: options)
                    .ConfigureAwait(false);
                total += results.Length;
            }

            return total;
        }

        /// <summary>
        /// Run StringSet lots of times.
        /// </summary>
        // [Benchmark(Description = "StringSet/s", OperationsPerInvoke = COUNT)]
        public void StringSet()
        {
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                db.StringSet(StringKey_K, StringValue_V);
            }
        }

        /// <summary>
        /// Run StringGet lots of times.
        /// </summary>
        // [Benchmark(Description = "StringGet/s", OperationsPerInvoke = COUNT)]
        public void StringGet()
        {
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                db.StringGet(StringKey_K);
            }
        }

        /// <summary>
        /// Run StringSet lots of times.
        /// </summary>
        // [Benchmark(Description = "C StringSet/s", OperationsPerInvoke = COUNT)]
        public void StringSet_Core()
        {
            using var conn = pool.GetConnection();
            var s = conn.Strings();
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                s.Set(StringKey_S, StringValue_S);
            }
        }

        /// <summary>
        /// Run StringGet lots of times.
        /// </summary>
        // [Benchmark(Description = "C StringGet/s", OperationsPerInvoke = COUNT)]
        public void StringGet_Core()
        {
            using var conn = pool.GetConnection();
            var s = conn.Strings();
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                s.Get(StringKey_S);
            }
        }

        /// <summary>
        /// Run StringSet lots of times.
        /// </summary>
        // [Benchmark(Description = "PC StringSet/s", OperationsPerInvoke = COUNT)]
        public void StringSet_Pipelined_Core()
        {
            using var conn = pool.GetConnection().ForPipeline();
            var s = conn.Strings();
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                s.Set(StringKey_S, StringValue_S);
            }
        }

        /// <summary>
        /// Run StringSet lots of times.
        /// </summary>
        // [Benchmark(Description = "PCA StringSet/s", OperationsPerInvoke = COUNT)]
        public async Task StringSet_Pipelined_Core_Async()
        {
            using var conn = pool.GetConnection().ForPipeline();
            var s = conn.Strings();
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                await s.SetAsync(StringKey_S, StringValue_S);
            }
        }

        /// <summary>
        /// Run StringGet lots of times.
        /// </summary>
        // [Benchmark(Description = "PC StringGet/s", OperationsPerInvoke = COUNT)]
        public void StringGet_Pipelined_Core()
        {
            using var conn = pool.GetConnection().ForPipeline();
            var s = conn.Strings();
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                s.Get(StringKey_S);
            }
        }

        /// <summary>
        /// Run StringGet lots of times.
        /// </summary>
        // [Benchmark(Description = "PCA StringGet/s", OperationsPerInvoke = COUNT)]
        public async Task StringGet_Pipelined_Core_Async()
        {
            using var conn = pool.GetConnection().ForPipeline();
            var s = conn.Strings();
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                await s.GetAsync(StringKey_S);
            }
        }

        /// <summary>
        /// Run HashGetAll lots of times.
        /// </summary>
        // [Benchmark(Description = "HashGetAll F+F/s", OperationsPerInvoke = COUNT)]
        public void HashGetAll_FAF()
        {
            for (int i = 0; i < OperationsPerInvoke; i++)
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
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                await db.HashGetAllAsync(HashKey, CommandFlags.FireAndForget);
                await db.PingAsync(); // to wait for response
            }
        }

        /// <summary>
        /// Run incr lots of times.
        /// </summary>
        // [Benchmark(Description = "old incr", OperationsPerInvoke = OperationsPerInvoke)]
        public int IncrBy_Old()
        {
            RedisValue value = 0;
            db.StringSet(StringKey_K, value);
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                value = db.StringIncrement(StringKey_K);
            }

            return (int)value;
        }

        /// <summary>
        /// Run incr lots of times.
        /// </summary>
        [Benchmark(Description = "new incr /p", OperationsPerInvoke = OperationsPerInvoke)]
        public int IncrBy_New_Pipelined()
        {
            using var conn = pool.GetConnection().ForPipeline();
            var s = conn.Strings();
            int value = 0;
            s.Set(StringKey_S, value);
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                value = s.Incr(StringKey_K);
            }

            return value;
        }

        /// <summary>
        /// Run incr lots of times.
        /// </summary>
        [Benchmark(Description = "new incr /p/a", OperationsPerInvoke = OperationsPerInvoke)]
        public async Task<int> IncrBy_New_Pipelined_Async()
        {
            using var conn = pool.GetConnection().ForPipeline();
            var s = conn.Strings();
            int value = 0;
            s.Set(StringKey_S, value);
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                value = await s.IncrAsync(StringKey_K);
            }

            return value;
        }

        /// <summary>
        /// Run incr lots of times.
        /// </summary>
        [Benchmark(Description = "new incr", OperationsPerInvoke = OperationsPerInvoke)]
        public int IncrBy_New()
        {
            using var conn = pool.GetConnection();
            var s = conn.Strings();
            int value = 0;
            s.Set(StringKey_S, value);
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                value = s.Incr(StringKey_K);
            }

            return value;
        }

        /// <summary>
        /// Run incr lots of times.
        /// </summary>
        // [Benchmark(Description = "new incr /pc", OperationsPerInvoke = OperationsPerInvoke)]
        public int IncrBy_New_Pipelined_Custom()
        {
            using var conn = customPool.GetConnection().ForPipeline();
            var s = conn.Strings();
            int value = 0;
            s.Set(StringKey_S, value);
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                value = s.Incr(StringKey_K);
            }

            return value;
        }

        /// <summary>
        /// Run incr lots of times.
        /// </summary>
        // [Benchmark(Description = "new incr /c", OperationsPerInvoke = OperationsPerInvoke)]
        public int IncrBy_New_Custom()
        {
            using var conn = customPool.GetConnection();
            var s = conn.Strings();
            int value = 0;
            s.Set(StringKey_S, value);
            for (int i = 0; i < OperationsPerInvoke; i++)
            {
                value = s.Incr(StringKey_K);
            }

            return value;
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
