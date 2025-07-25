using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Validators;

namespace StackExchange.Redis.Benchmarks
{
    internal class CustomConfig : ManualConfig
    {
        protected virtual Job Configure(Job j) => j;

        public CustomConfig()
        {
            AddDiagnoser(MemoryDiagnoser.Default);
            AddColumn(StatisticColumn.OperationsPerSecond);
            AddValidator(JitOptimizationsValidator.FailOnError);

            AddJob(Configure(Job.Default.WithRuntime(ClrRuntime.Net481)));
            AddJob(Configure(Job.Default.WithRuntime(CoreRuntime.Core80)));
        }
    }
}
