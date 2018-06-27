using System;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using StackExchange.Redis;

namespace BasicTest
{

    static class Program
    {
        static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);

    }
    internal class CustomConfig : ManualConfig
    {
        public CustomConfig()
        {
            Job Get(Job j) => j
                .With(new GcMode { Force = true });

            Add(new MemoryDiagnoser());
            Add(StatisticColumn.OperationsPerSecond);
            Add(JitOptimizationsValidator.FailOnError);
            Add(Get(Job.Clr));
            Add(Get(Job.Core));
        }
    }
    /// <summary>
    /// The tests
    /// </summary>
    [Config(typeof(CustomConfig))]
    public class RedisBenchmarks : IDisposable
    {
        ConnectionMultiplexer connection;
        IDatabase db;
        /// <summary>
        /// Create
        /// </summary>
        public RedisBenchmarks()
        {
            connection = ConnectionMultiplexer.Connect("127.0.0.1:6379,syncTimeout=200000");
            db = connection.GetDatabase(3);
        }
        void IDisposable.Dispose()
        {
            connection?.Dispose();
            db = null;
            connection = null;
        }

        
        const int COUNT = 10000;

        /// <summary>
        /// Run INCRBY lots of times
        /// </summary>
#if TEST_BASELINE
        [Benchmark(Description = "INCRBY:v1", OperationsPerInvoke = COUNT)]
#else
        [Benchmark(Description = "INCRBY:v2", OperationsPerInvoke = COUNT)]
#endif
        public int Execute()
        {   
            var rand = new Random(12345);
            RedisKey counter = "counter";
            db.KeyDelete(counter, CommandFlags.FireAndForget);
            int expected = 0;
            for (int i = 0; i < COUNT; i++)
            {
                int x = rand.Next(50);
                expected += x;
                db.StringIncrement(counter, x, CommandFlags.FireAndForget);
            }
            int actual = (int)db.StringGet(counter);
            if (actual != expected) throw new InvalidOperationException(
                $"expected: {expected}, actual: {actual}");
            return actual;
        }
    }
}
