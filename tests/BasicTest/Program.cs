using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace BasicTest
{
    internal static class Program
    {
#if DEBUG
        private static async Task Main()
        {
            /*
            var options = ConfigurationOptions.Parse("127.0.0.1:6379,$subscribe=,");
#pragma warning disable SERED001
            LoggingTunnel.LogToDirectory(options, @"c:\Code\RedisLog");
#pragma warning restore SERED001
            using var muxer = await ConnectionMultiplexer.ConnectAsync(options);
            var db = muxer.GetDatabase();
            RedisKey key = "data";
            await db.KeyDeleteAsync(key);
            var block = new HashEntry[10000];
            for (int i = 0; i < block.Length;i++)
            {
                RedisValue name = $"field{i}", value = $"value{i}";
                await db.HashSetAsync(key, name, value);
                block[i] = new(name, value);
            }
            await db.HashSetAsync(key, block);
            _ = db.HashGetAllAsync(key);
            */
            var obj = new ParserBenchmarks();
            Console.WriteLine(await obj.LegacyParser());
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine(await obj.NewParser());
            Console.WriteLine();
            Console.WriteLine(StackExchange.Redis.Protocol.LeasedSequence<byte>.DebugTotalLeased);
            Console.WriteLine(StackExchange.Redis.Protocol.LeasedSequence<byte>.DebugOutstanding);
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
}
