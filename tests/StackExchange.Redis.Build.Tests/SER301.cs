using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// Family B: compare-and-set, where a newer single command subsumes both the condition and the write. Separate
/// from SER300 because these need an 8.4+ server and SER300 does not.
/// </summary>
public class SER301 : Verifier<TransactionAnalyzer>
{
    [Fact]
    public Task StringEqualGuardingStringSet_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.StringEqual(key, "old"))|};
                _ = tran.StringSetAsync(key, "new");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER301").WithLocation(0).WithArguments(
            "Condition.StringEqual",
            "StringSetAsync",
            "StringSet[Async](key, value, ValueCondition.Equal(expected))",
            "8.4"));

    [Fact]
    public Task StringNotEqualGuardingStringSet_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.StringNotEqual(key, "old"))|};
                _ = tran.StringSetAsync(key, "new");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER301").WithLocation(0).WithArguments(
            "Condition.StringNotEqual",
            "StringSetAsync",
            "StringSet[Async](key, value, ValueCondition.NotEqual(expected))",
            "8.4"));

    [Fact]
    // the canonical lock-release, and the highest-frequency real-world hit in this family
    public Task StringEqualGuardingKeyDelete_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.StringEqual(key, "token"))|};
                _ = tran.KeyDeleteAsync(key);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER301").WithLocation(0).WithArguments(
            "Condition.StringEqual",
            "KeyDeleteAsync",
            "StringDelete[Async](key, ValueCondition.Equal(expected)), or LockRelease[Async]",
            "8.4"));

    [Fact]
    public Task StringNotEqualGuardingKeyDelete_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.StringNotEqual(key, "token"))|};
                _ = tran.KeyDeleteAsync(key);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER301").WithLocation(0).WithArguments(
            "Condition.StringNotEqual",
            "KeyDeleteAsync",
            "StringDelete[Async](key, ValueCondition.NotEqual(expected))",
            "8.4"));

    [Fact]
    // cross-key compare-and-set genuinely needs the transaction; must never fire
    public Task DifferentKeys_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey a, RedisKey b)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.StringEqual(a, "old"));
                _ = tran.StringSetAsync(b, "new");
                await tran.ExecuteAsync();
            }
        }
        """);
}
