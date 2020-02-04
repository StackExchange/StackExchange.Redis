using Xunit;

namespace NRediSearch.Test
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public static class NonParallelCollection
    {
        public const string Name = "NonParallel";
    }
}
