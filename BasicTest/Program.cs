using System;
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
        static void Main()
        {
            // tell BenchmarkDotNet not to force GC.Collect after benchmark iteration 
            // (single iteration contains of multiple (usually millions) of invocations)
            // it can influence the allocation-heavy Task<T> benchmarks
            var gcMode = new GcMode { Force = false };

            var customConfig = ManualConfig
                .Create(DefaultConfig.Instance) // copies all exporters, loggers and basic stuff
                .With(JitOptimizationsValidator.FailOnError) // Fail if not release mode
                .With(MemoryDiagnoser.Default) // use memory diagnoser
                .With(StatisticColumn.OperationsPerSecond) // add ops/s
                .With(Job.Default.With(gcMode));

            var summary = BenchmarkRunner.Run<Benchmark>(customConfig);
            Console.WriteLine(summary);
        }
    }
    /// <summary>
    /// The tests
    /// </summary>
    public class Benchmark : IDisposable
    {
        ConnectionMultiplexer connection;
        IDatabase db;
        /// <summary>
        /// Create
        /// </summary>
        public Benchmark()
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
