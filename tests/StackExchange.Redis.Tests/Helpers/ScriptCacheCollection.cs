using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Suites that depend on the server's script cache, which is <b>server-wide</b>.
/// </summary>
/// <remarks>
/// <para>
/// <c>SCRIPT FLUSH</c> empties it for every client at once, and a test that loads a script and then
/// evaluates it by hash fails with <c>NOSCRIPT</c> if somebody flushes in between. That is not a defect
/// in either test - it is two correct tests sharing one piece of server state.
/// </para>
/// <para>
/// <b>Its own collection rather than the non-parallel one</b>, so these serialise against each other and
/// still run alongside everything else: making them globally non-parallel would cost far more wall-clock
/// than the problem is worth, and would not be more correct.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class ScriptCacheCollection
{
    public const string Name = "ScriptCache";
}
