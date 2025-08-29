using BenchmarkDotNet.Jobs;

namespace BasicTest;

internal class SlowConfig : CustomConfig
{
    protected override Job Configure(Job j)
        => j.WithLaunchCount(1)
            .WithWarmupCount(1)
            .WithIterationCount(5);
}
