using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// Negatives that are not about one rule but about the shape the analyzer is willing to reason about at all;
/// they suppress SER300 and SER301 alike, so they do not belong in either ID's file.
/// </summary>
/// <remarks>
/// These matter more than the positive cases. Every one of them is correct code that a keener analyzer would
/// "helpfully" suggest breaking, in a diagnostic shipped to every consumer of the package.
/// </remarks>
public class DetectionShape : Verifier<TransactionAnalyzer>
{
    [Fact]
    // two conditions is a genuine multi-guard transaction; no single command takes both
    public Task TwoConditions_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.KeyNotExists(key));
                tran.AddCondition(Condition.HashNotExists(key, "field"));
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // two queued writes need the transaction for atomicity even though the condition maps cleanly
    public Task TwoOperations_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.KeyNotExists(key));
                _ = tran.StringSetAsync(key, "value");
                _ = tran.KeyExpireAsync(key, System.TimeSpan.FromMinutes(1));
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // no condition at all: this is family D territory (a compound command), not a conditional rewrite
    public Task NoCondition_IsNotFlagged() => VerifyAsync(
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
        """);

    [Fact]
    // one call site, N queued commands. Counting syntax says "one operation"; the runtime says otherwise, and
    // a suggestion to collapse would be flatly wrong
    public Task OperationInLoop_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, RedisValue[] values)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.KeyNotExists(key));
                foreach (var value in values)
                {
                    _ = tran.StringSetAsync(key, value);
                }

                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // the helper may queue anything at all; our counts describe only the part we can see
    public Task TransactionPassedToAnotherMethod_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.KeyNotExists(key));
                _ = tran.StringSetAsync(key, "value");
                QueueMore(tran, key);
                await tran.ExecuteAsync();
            }

            private static void QueueMore(ITransaction tran, RedisKey key)
                => _ = tran.KeyExpireAsync(key, System.TimeSpan.FromMinutes(1));
        }
        """);

    [Fact]
    // stored away, so the queueing is unbounded in both time and place
    public Task TransactionStoredInField_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            private ITransaction? _pending;

            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.KeyNotExists(key));
                _ = tran.StringSetAsync(key, "value");
                _pending = tran;
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // the condition comes from somewhere we cannot inspect, so we do not know what it tests
    public Task ConditionFromVariable_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, Condition condition)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(condition);
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // same key, spelled differently. A miss, not a false positive - deliberately the safe direction, and
    // pinned here so that "improving" the key comparison is a conscious decision
    public Task SameKeyDifferentSpelling_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db)
            {
                RedisKey key = "k";
                var alias = key;
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.KeyNotExists(key));
                _ = tran.StringSetAsync(alias, "value");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // The same unsoundness as SER304's reassignment case, on the guarded shape: the condition names key "a" and
    // the write lands on "b", so the transaction is a real guard and collapsing it would change behaviour.
    public Task ConditionKeyReassignedBeforeOperation_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db)
            {
                RedisKey key = "a";
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.KeyNotExists(key));
                key = "b";
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // and on the compound-pair shape
    public Task PairKeyReassignedBetweenOperations_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db)
            {
                RedisKey key = "a";
                var tran = db.CreateTransaction();
                _ = tran.StringGetAsync(key);
                key = "b";
                _ = tran.KeyDeleteAsync(key);
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // A local that is reassigned but plays no part in any key or member expression must not suppress anything -
    // ordinary methods are full of counters and accumulators.
    public Task UnrelatedLocalReassigned_IsStillFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task<int> M(IDatabase db, RedisKey key)
            {
                var count = 0;
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.KeyNotExists(key))|};
                _ = tran.StringSetAsync(key, "value");
                count = 1;
                await tran.ExecuteAsync();
                return count;
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyNotExists",
            "StringSetAsync",
            "StringSet(key, value, When.NotExists)"));

    [Fact]
    // two independent transactions in one method must be tracked separately, not pooled into one set of counts
    public Task TwoIndependentTransactions_AreFlaggedIndependently() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey a, RedisKey b)
            {
                var first = db.CreateTransaction();
                {|#0:first.AddCondition(Condition.KeyNotExists(a))|};
                _ = first.StringSetAsync(a, "value");
                await first.ExecuteAsync();

                var second = db.CreateTransaction();
                {|#1:second.AddCondition(Condition.StringEqual(b, "old"))|};
                _ = second.StringSetAsync(b, "new");
                await second.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyNotExists",
            "StringSetAsync",
            "StringSet(key, value, When.NotExists)"),
        Diagnostic("SER301").WithLocation(1).WithArguments(
            "Condition.StringEqual",
            "StringSetAsync",
            "StringSet(key, value, ValueCondition.Equal(expected))",
            "8.4"));
}
