using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

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
        Diagnostic("SER301").WithLocation(0));

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
