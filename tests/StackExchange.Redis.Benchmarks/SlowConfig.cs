using BenchmarkDotNet.Jobs;

namespace StackExchange.Redis.Benchmarks
{
    internal class SlowConfig : CustomConfig
    {
        protected override Job Configure(Job j)
            => j.WithLaunchCount(1)
                .WithWarmupCount(1)
                .WithIterationCount(5);
    }
}
