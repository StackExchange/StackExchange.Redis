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
            var obj = new RespiteBenchmarks();
            obj.Setup();
            obj.SERedis_Set();
            await obj.SERedis_Set_Async();
            obj.RESpite_Set();
            await obj.RESpite_Set_Async();
        }

#else
        //private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);


        private static void Main(string[] args) => BenchmarkRunner.Run<RespiteBenchmarks>(args: args);
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
