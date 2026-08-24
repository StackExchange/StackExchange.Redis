using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// The library's own blocking helpers - <c>Wait</c>, <c>WaitAll</c>, <c>TryWait</c>.
/// </summary>
/// <remarks>
/// A rule rather than <c>[Obsolete]</c> so that silencing it does not mean silencing <c>CS0618</c>, and with
/// it every obsoletion from every source. The two declaring interfaces are unrelated - <c>IRedisAsync</c> and
/// <c>IConnectionMultiplexer</c> - so both are covered here; testing one would have left half the surface
/// unguarded, which is how the first attempt at this missed the multiplexer entirely.
/// </remarks>
public class SER308 : Verifier<QueuedResultAnalyzer>
{
    [Fact]
    public Task WaitOnDatabase_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public void M(IDatabase db, Task task)
            {
                {|#0:db.Wait(task)|};
            }
        }
        """,
        Diagnostic("SER308").WithLocation(0).WithArguments("Wait"));

    [Fact]
    public Task GenericWaitOnDatabase_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public void M(IDatabase db, Task<int> task)
            {
                var value = {|#0:db.Wait(task)|};
            }
        }
        """,
        Diagnostic("SER308").WithLocation(0).WithArguments("Wait"));

    [Fact]
    public Task TryWait_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public void M(IDatabase db, Task task)
            {
                var ok = {|#0:db.TryWait(task)|};
            }
        }
        """,
        Diagnostic("SER308").WithLocation(0).WithArguments("TryWait"));

    /// <summary>
    /// <c>IConnectionMultiplexer</c> declares its own Wait family, unrelated to <c>IRedisAsync</c>'s.
    /// </summary>
    [Fact]
    public Task WaitAllOnMultiplexer_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public void M(IConnectionMultiplexer conn, Task[] tasks)
            {
                {|#0:conn.WaitAll(tasks)|};
            }
        }
        """,
        Diagnostic("SER308").WithLocation(0).WithArguments("WaitAll"));

    /// <summary>
    /// And on the concrete class, which is what <c>Connect</c> returns - so this is the common shape, and the
    /// member's containing type is the class rather than the interface.
    /// </summary>
    [Fact]
    public Task WaitOnConcreteMultiplexer_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public void M(ConnectionMultiplexer conn, Task task)
            {
                {|#0:conn.Wait(task)|};
            }
        }
        """,
        Diagnostic("SER308").WithLocation(0).WithArguments("Wait"));

    [Fact]
    public Task WaitOnSubscriber_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public void M(ISubscriber sub, Task task)
            {
                {|#0:sub.Wait(task)|};
            }
        }
        """,
        Diagnostic("SER308").WithLocation(0).WithArguments("Wait"));

    // ---- negative cases ----

    /// <summary>Awaiting is the answer the rule points at, and must not itself be flagged.</summary>
    [Fact]
    public Task Awaited_IsClean() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                await db.StringGetAsync(key);
            }
        }
        """);

    /// <summary>
    /// Something else entirely called <c>Wait</c> is not ours; the rule matches the interface members rather
    /// than the name, which is the whole reason it can be a warning rather than a guess.
    /// </summary>
    [Fact]
    public Task UnrelatedWait_IsClean() => VerifyAsync(
        """
        using System.Threading;
        using System.Threading.Tasks;
        class Waiter
        {
            public void Wait(Task task) { }
            public bool TryWait(Task task) => true;
        }
        class C
        {
            public void M(Waiter waiter, Task task, ManualResetEventSlim gate)
            {
                waiter.Wait(task);
                waiter.TryWait(task);
                gate.Wait();
                task.Wait();
            }
        }
        """);
}
