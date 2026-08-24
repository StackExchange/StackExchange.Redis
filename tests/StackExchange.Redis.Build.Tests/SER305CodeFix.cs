using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StackExchange.Redis.CodeFixes;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// The two fixes for SER305/SER306: discard the queued result, or capture it and await after Execute.
/// </summary>
public class SER305CodeFix : CodeFixVerifier<QueuedResultAnalyzer, QueuedResultCodeFixProvider>
{
    [Fact]
    public Task Discard_RewritesToDiscardAssignment() => VerifyFixAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:await {|#1:tran.StringSetAsync(key, "value")|}|};
                await tran.ExecuteAsync();
            }
        }
        """,
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            }
        }
        """,
        DiscardFix,
        Diagnostic("SER305", DiagnosticSeverity.Error).WithLocation(0).WithLocation(1).WithArguments("StringSetAsync", "a transaction"));

    [Fact]
    public Task Capture_MovesTheAwaitAfterExecute() => VerifyFixAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:await {|#1:tran.StringSetAsync(key, "value")|}|};
                await tran.ExecuteAsync();
            }
        }
        """,
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                var pending = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
                await pending;
            }
        }
        """,
        CaptureFix,
        Diagnostic("SER305", DiagnosticSeverity.Error).WithLocation(0).WithLocation(1).WithArguments("StringSetAsync", "a transaction"));

    /// <summary>
    /// The value-returning shape: the declaration moves with the await, so the variable is still there.
    /// </summary>
    [Fact]
    public Task Capture_MovesADeclarationAfterExecute() => VerifyFixAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task<RedisValue> M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                var value = {|#0:await {|#1:tran.StringGetAsync(key)|}|};
                await tran.ExecuteAsync();
                return value;
            }
        }
        """,
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task<RedisValue> M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                var pending = tran.StringGetAsync(key);
                await tran.ExecuteAsync();
                var value = await pending;
                return value;
            }
        }
        """,
        CaptureFix,
        Diagnostic("SER305", DiagnosticSeverity.Error).WithLocation(0).WithLocation(1).WithArguments("StringGetAsync", "a transaction"));

    /// <summary>A name already in scope is stepped over rather than shadowed.</summary>
    [Fact]
    public Task Capture_AvoidsAnExistingName() => VerifyFixAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var pending = 1;
                var tran = db.CreateTransaction();
                {|#0:await {|#1:tran.StringSetAsync(key, "value")|}|};
                await tran.ExecuteAsync();
            }
        }
        """,
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var pending = 1;
                var tran = db.CreateTransaction();
                var pending2 = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
                await pending2;
            }
        }
        """,
        CaptureFix,
        Diagnostic("SER305", DiagnosticSeverity.Error).WithLocation(0).WithLocation(1).WithArguments("StringSetAsync", "a transaction"));

    /// <summary>
    /// Fire-and-forget gets the discard fix only: awaiting later would still read the default value, so
    /// "capture it and await after Execute" is not offered - index 1 does not exist here.
    /// </summary>
    [Fact]
    public Task FireAndForget_OffersDiscardOnly() => VerifyFixAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:await {|#1:tran.StringSetAsync(key, "value", flags: CommandFlags.FireAndForget)|}|};
                await tran.ExecuteAsync();
            }
        }
        """,
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(key, "value", flags: CommandFlags.FireAndForget);
                await tran.ExecuteAsync();
            }
        }
        """,
        DiscardFix,
        Diagnostic("SER306").WithLocation(0).WithLocation(1).WithArguments("StringSetAsync"));
}
