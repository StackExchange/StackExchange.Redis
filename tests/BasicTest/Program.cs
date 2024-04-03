using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using System;
using System.Threading.Tasks;

namespace BasicTest
{
    internal static class Program
    {
#if DEBUG
        private static async Task Main()
        {
            var obj = new RESPiteBenchmarks();
            obj.Setup();
            obj.SERedis_Set();
            await obj.SERedis_Set_Async();
            obj.RESPite_Set();
            await obj.RESPite_Set_Async();


            Console.WriteLine(obj.SERedis_Get());
            Console.WriteLine(await obj.SERedis_Get_Async());
            Console.WriteLine(obj.RESPite_Get());
            Console.WriteLine(await obj.RESPite_Get_Async());

            obj.RESPite_Set();
            long i = 0, snapshotCount = 0;
            var snapshotTime = DateTime.Now;
            while (true)
            {
                obj.RESPite_Get();
                if ((++i % 1000) == 0)
                {
                    var now = DateTime.Now;
                    var delta = now - snapshotTime;
                    if (delta.TotalSeconds != 0)
                    {
                        Console.WriteLine($"{i}: {(i - snapshotCount) / delta.TotalSeconds:###,##0} ops/s");
                        snapshotTime = now;
                        snapshotCount = i;
                    }
                }
            }
        }

#else
        //private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);


        private static void Main(string[] args) => BenchmarkRunner.Run<RESPiteBenchmarks>(args: args);
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
