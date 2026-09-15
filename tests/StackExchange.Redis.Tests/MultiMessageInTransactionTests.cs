using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// What each <c>IMultiMessage</c> does when it becomes an inner operation of a transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mechanism is a convention, not a structure.</b> <c>QueuedMessage</c> wraps each inner operation
/// and is not itself an <c>IMultiMessage</c>, so an inner one's expansion is never asked for - it is
/// silently discarded and only <c>WriteImpl</c> runs. Every type that cannot survive that has a
/// hand-written <c>this is IBatch</c> / <c>this is ITransaction</c> guard, and each guard lives somewhere
/// different. The safety is "someone remembered", four times.
/// </para>
/// <para>
/// These pin the whole map, because the interesting half is the types that are <i>allowed</i> through:
/// a script sent as a body degrades correctly, and a script sent as a hash fails honestly inside the EXEC
/// array. Anything new that composes has to land in one of those two columns deliberately.
/// </para>
/// </remarks>
public class MultiMessageInTransactionTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    /// <summary>A nested MULTI is refused up front rather than flattened.</summary>
    [Fact]
    public async Task NestedTransactionsAreRefused()
    {
        await using var muxer = Create();
        var outer = muxer.GetDatabase().CreateTransaction();

        // ITransaction does not expose CreateTransaction, but the implementation is a RedisDatabase, so
        // the cast reaches it - and the guard is on the method rather than on the interface shape
        var ex = Assert.Throws<NotSupportedException>(() => ((IDatabase)outer).CreateTransaction());
        Assert.Contains("Nested transactions are not supported", ex.Message);
    }

    /// <summary>
    /// <c>StringGetWithExpiry</c> is refused, because it is the case that genuinely needs both messages.
    /// </summary>
    /// <remarks>
    /// Its TTL result box is created inside <c>GetMessages</c>, so a silent discard would leave it null and
    /// the expiry unreadable. Refusing - and naming the two commands to issue instead - beats returning a
    /// value with a missing half.
    /// </remarks>
    [Fact]
    public async Task StringGetWithExpiryIsRefused()
    {
        await using var muxer = Create();
        var tran = muxer.GetDatabase().CreateTransaction();

        // synchronous Assert.Throws deliberately: a transaction op that does NOT throw returns a task
        // that only completes on Execute, so ThrowsAsync would hang rather than fail
        var ex = Assert.Throws<NotSupportedException>(() => { _ = tran.StringGetWithExpiryAsync(Me()); });
        Assert.Contains("not possible inside a transaction", ex.Message);
    }

    /// <summary>A script given as a body runs: the expansion is dropped, and the body-carrying spelling is right.</summary>
    /// <remarks>
    /// The one case where discarding the expansion is not merely survivable but correct. Composing
    /// <c>SCRIPT LOAD</c> in front would be wrong here - inside the <c>MULTI</c> its reply joins the
    /// <c>EXEC</c> array and shifts every result position.
    /// </remarks>
    [Fact]
    public async Task AScriptGivenAsABodyRuns()
    {
        await using var muxer = Create();
        var db = muxer.GetDatabase();
        var tran = db.CreateTransaction();

        var pending = tran.ScriptEvaluateAsync($"return '{Me()}'");
        Assert.True(await tran.ExecuteAsync());
        Assert.Equal(Me(), (await pending).ToString());
    }

    /// <summary>
    /// A script given as an unknown <b>hash</b> reaches the server and fails as an element of the EXEC array.
    /// </summary>
    /// <remarks>
    /// Not a bug, and the precedent worth knowing: the caller supplied a hash, so there is no body to fall
    /// back to. The transaction still executes - <c>EXEC</c> succeeds - and the failure is positioned on
    /// the one operation that failed, which is the most honest answer available. Redis queues without
    /// executing, so an unknown hash is accepted with <c>+QUEUED</c> and can only fail at <c>EXEC</c>, by
    /// which time reissuing as <c>EVAL</c> is impossible.
    /// </remarks>
    [Fact]
    public async Task AScriptGivenAsAnUnknownHashFailsInsideExec()
    {
        await using var muxer = Create();
        var tran = muxer.GetDatabase().CreateTransaction();

        var pending = tran.ScriptEvaluateAsync(new byte[20]); // a hash no server will know
        Assert.True(await tran.ExecuteAsync());               // the transaction itself is fine

        var ex = await Assert.ThrowsAsync<RedisServerException>(() => pending);
        Assert.Contains("NOSCRIPT", ex.Message);
    }

    /// <summary>
    /// <c>HashImport</c> is refused, and its message states the general rule.
    /// </summary>
    /// <remarks>
    /// The clearest statement of the constraint anything composing has to satisfy: a connection-local
    /// preamble cannot be injected into a MULTI/EXEC without desyncing the result array. That is the same
    /// reason a composed <c>SCRIPT LOAD</c> may not go inside.
    /// </remarks>
    [Fact]
    public async Task HashImportIsRefused()
    {
        await using var muxer = Create();
        var tran = muxer.GetDatabase().CreateTransaction();

        using var fieldSet = HashImport.Create("f");
        var ex = Assert.Throws<NotSupportedException>(
            () => { _ = tran.HashImportAsync(Me(), fieldSet, new RedisValue[] { "v" }); });
        Assert.Contains("not supported inside a transaction", ex.Message);
    }
}
