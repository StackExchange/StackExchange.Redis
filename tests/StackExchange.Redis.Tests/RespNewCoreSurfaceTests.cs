namespace StackExchange.Redis.Tests;

/// <summary>
/// Helpers for tests that assert about the new core directly.
/// </summary>
/// <remarks>
/// This once built a second core beside each multiplexer, and re-ran the existing suites through it while the
/// multiplexer itself still sent through <c>Message</c>. The new core is now the multiplexer's only core, so
/// those suites test it as they stand; the re-runs went, and these helpers answer with the multiplexer's own.
/// </remarks>
public static class RespNewCoreFixture
{
    /// <summary>The core behind a multiplexer, for tests that assert about connections rather than commands.</summary>
    internal static RespNewCore CoreFor(IConnectionMultiplexer conn) => TestMultiplexer.Unwrap(conn).NewCore;

    /// <summary>The multiplexer's database, which is the new core's.</summary>
    internal static IDatabase Wrap(IConnectionMultiplexer conn, int db, object? asyncState)
        => conn.GetDatabase(db, asyncState);
}
