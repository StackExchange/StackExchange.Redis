namespace NRediSearch.Test
{
    // This is only to make the namespace more-local and not need a using at the top of every test file that's easy to forget
    public class FactAttribute : StackExchange.Redis.Tests.FactAttribute { }

    public class TheoryAttribute : StackExchange.Redis.Tests.TheoryAttribute { }
}
