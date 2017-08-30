using Xunit;

namespace StackExchange.Redis.Tests
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public static class NonParallelCollection
    {
        public const string Name = "NonParallel";
    }
}
