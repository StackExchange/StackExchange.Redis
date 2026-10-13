using Microsoft.CodeAnalysis;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// A redis call that cannot honour the <c>CancellationToken</c> its enclosing method accepts.
/// </summary>
/// <remarks>
/// The signal is the <b>token</b>, not the call. A method whose signature promises cancellation, calling
/// something that cannot cancel, is a promise the code cannot keep - and the failure is silent: the token
/// is cancelled, the caller waits anyway, and nothing in the source says why.
/// </remarks>
public class SER310 : Verifier<CancellationAnalyzer>
{
    [Fact]
    public Task CallWithATokenParameter_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, CancellationToken cancellationToken)
            {
                await {|#0:db.StringGetAsync("k")|};
            }
        }
        """,
        Diagnostic("SER310", DiagnosticSeverity.Info).WithLocation(0).WithArguments("StringGetAsync", "cancellationToken"));

    [Fact]
    public Task WithoutAToken_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db)
            {
                await db.StringGetAsync("k");
            }
        }
        """);

    [Fact]
    public Task ATokenInALocal_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db)
            {
                // a local may be plumbing this call was never meant to observe; only a PARAMETER is a
                // contract with the caller, and narrowing to that is what keeps this quiet enough to leave on
                var token = CancellationToken.None;
                await db.StringGetAsync("k");
            }
        }
        """);

    [Fact]
    public Task ManyCallsInOneMethod_AreReportedOnce() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, CancellationToken cancellationToken)
            {
                await {|#0:db.StringGetAsync("a")|};
                await db.StringGetAsync("b");
                await db.StringGetAsync("c");
            }
        }
        """,
        Diagnostic("SER310", DiagnosticSeverity.Info).WithLocation(0).WithArguments("StringGetAsync", "cancellationToken"));

    [Fact]
    public Task TheSubscriberSurfaceCounts_Too() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(ISubscriber sub, CancellationToken cancellationToken)
            {
                await {|#0:sub.PublishAsync(RedisChannel.Literal("c"), "v")|};
            }
        }
        """,
        Diagnostic("SER310", DiagnosticSeverity.Info).WithLocation(0).WithArguments("PublishAsync", "cancellationToken"));

    [Fact]
    public Task NonRedisCalls_AreNotFlagged() => VerifyAsync(
        """
        using System.Threading;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(CancellationToken cancellationToken)
            {
                await Task.Delay(1, cancellationToken);
            }
        }
        """);
}
