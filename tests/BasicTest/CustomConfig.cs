using System.Runtime.InteropServices;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Validators;

namespace BasicTest;

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
