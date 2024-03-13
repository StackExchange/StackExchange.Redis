using System;
using System.Reflection;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using Perfolizer.Mathematics.Selectors;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;

namespace BasicTest
{
    internal static class Program
    {
#if DEBUG
        private static async Task Main()
        {
            var obj = new RedisBenchmarks();
            Console.WriteLine(await obj.LegacyParser());
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine(await obj.NewParser());
        }

#else
        private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
#endif
    }
    internal class CustomConfig : ManualConfig
    {
        protected virtual Job Configure(Job j)
            => j.WithGcMode(new GcMode { Force = true })
                //.With(InProcessToolchain.Instance)
                ;

        public CustomConfig()
        {
            AddDiagnoser(MemoryDiagnoser.Default);
            AddColumn(StatisticColumn.OperationsPerSecond);
            AddValidator(JitOptimizationsValidator.FailOnError);

            AddJob(Configure(Job.Default.WithRuntime(ClrRuntime.Net472)));
            AddJob(Configure(Job.Default.WithRuntime(CoreRuntime.Core50)));
        }
    }
    internal class SlowConfig : CustomConfig
    {
        protected override Job Configure(Job j)
            => j.WithLaunchCount(1)
                .WithWarmupCount(1)
                .WithIterationCount(5);
    }
#pragma warning disable SERED001, SERED002 // Type is for evaluation purposes only
    /// <summary>
    /// The tests
    /// </summary>
    [Config(typeof(CustomConfig)), MemoryDiagnoser]
    public class RedisBenchmarks
    {

        [Benchmark(Baseline = true)]
        public async Task<int> LegacyParser()
        {
            int count = 0;
            Action<RedisResult, RedisResult> callback = (req, resp) =>
            {
                if (req is not null) Console.WriteLine("> " + LoggingTunnel.DefaultFormatCommand(req));
                Console.WriteLine("< " + LoggingTunnel.DefaultFormatCommand(resp));
                count++;
            };

            var total = await LoggingTunnel.ReplayAsync(@"C:\Code\RedisLog", callback);
            if (total != count) throw new InvalidOperationException();
            return count;
        }

        [Benchmark]
        public async Task<int> NewParser()
        {
            int count = 0;
            LoggingTunnel.MessagePair callback = (req, resp) =>
            {
                if (req.HasValue) Console.WriteLine("> " + LoggingTunnel.DefaultFormatCommand(ref req));
                Console.WriteLine("< " + LoggingTunnel.DefaultFormatCommand(ref resp));
                count++;
            };

            var total = await LoggingTunnel.ReplayAsync(@"C:\Code\RedisLog", callback);
            if (total != count) throw new InvalidOperationException();
            return count;
        }
    }
}
