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
    // Opposite arms of one if/else: exactly one of these is ever queued, so there is no pair to collapse.
    // SetMove here would queue a removal the code deliberately did not.
    public Task OperationsInOppositeBranches_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey a, RedisKey b, RedisValue member, bool flag)
            {
                var tran = db.CreateTransaction();
                if (flag) { _ = tran.SetAddAsync(a, member); }
                else { _ = tran.SetRemoveAsync(b, member); }
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // the asymmetric version: the second command is queued only sometimes, so the "pair" is not always a pair
    public Task ConditionallyQueuedOperation_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, bool flag)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringGetAsync(key);
                if (flag) { _ = tran.KeyDeleteAsync(key); }
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // and a condition that guards from outside the branch its command is in
    public Task ConditionOutsideOperationBranch_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, bool flag)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.KeyNotExists(key));
                if (flag) { _ = tran.StringSetAsync(key, "value"); }
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // The control for the three above, and the reason this is branch-matching rather than a blanket "anything
    // conditional is out": two commands in the *same* branch always queue together, so the pair is real. A
    // whole transaction inside an if or a try is ordinary code and must not go silent.
    public Task OperationsInTheSameBranch_AreStillFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, bool flag)
            {
                var tran = db.CreateTransaction();
                if (flag)
                {
                    _ = {|#0:tran.StringGetAsync(key)|};
                    _ = tran.KeyDeleteAsync(key);
                    await tran.ExecuteAsync();
                }
            }
        }
        """,
        Diagnostic("SER303").WithLocation(0).WithArguments(
            "StringGetAsync",
            "KeyDeleteAsync",
            "StringGetDelete[Async](key)",
            " (requires server 6.2 or later)"));

    [Fact]
    // A lambda is the loop case wearing a hat: one call site, and no way to see how many times it runs - or
    // whether it runs at all. Three SetAdds are queued here, not the two the syntax shows.
    public Task OperationInLambda_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                System.Action add = () => { _ = tran.SetAddAsync(key, "a"); };
                add();
                add();
                _ = tran.SetAddAsync(key, "b");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // the same for a local function, which is the shape somebody actually writes
    public Task OperationInLocalFunction_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, RedisKey other)
            {
                var tran = db.CreateTransaction();
                _ = tran.KeyDeleteAsync(key);
                Queue();
                Queue();
                await tran.ExecuteAsync();

                void Queue() => _ = tran.KeyDeleteAsync(other);
            }
        }
        """);

    [Fact]
    // The control for the two above, and the reason the function boundary is not itself disqualifying: those
    // capture a transaction that outlives them, which is what makes the invocation count matter. A transaction
    // *declared* in the local function is one per invocation however often it runs, so the counts inside are
    // exact. This shape - one transaction, one helper method - is the single commonest way the guarded pattern
    // gets written, and treating it as unknowable silenced the whole analyzer for it.
    public Task TransactionDeclaredInLocalFunction_IsStillFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task<bool> M(IDatabase db, RedisKey key)
            {
                return await SetIfNotExists();

                async Task<bool> SetIfNotExists()
                {
                    var tran = db.CreateTransaction();
                    _ = tran.StringSetAsync(key, "value");
                    var cond = {|#0:tran.AddCondition(Condition.KeyNotExists(key))|};
                    await tran.ExecuteAsync();
                    return cond.WasSatisfied;
                }
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyNotExists",
            "StringSetAsync",
            "StringSet[Async](key, value, When.NotExists)"));

    [Fact]
    // and in a lambda, where the transaction is likewise per-invocation
    public Task TransactionDeclaredInLambda_IsStillFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System;
        using System.Threading.Tasks;
        class C
        {
            public Func<Task> M(IDatabase db, RedisKey key) => async () =>
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.KeyNotExists(key))|};
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            };
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyNotExists",
            "StringSetAsync",
            "StringSet[Async](key, value, When.NotExists)"));

    [Fact]
    // Calling it in a loop makes N transactions, not one transaction with N commands, so the per-transaction
    // counts still hold. The enclosing loop governs how many transactions there are, not what goes into each.
    public Task TransactionDeclaredInLocalFunctionCalledInLoop_IsStillFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey[] keys)
            {
                foreach (var key in keys)
                {
                    await SetIfNotExists(key);
                }

                async Task SetIfNotExists(RedisKey key)
                {
                    var tran = db.CreateTransaction();
                    {|#0:tran.AddCondition(Condition.KeyNotExists(key))|};
                    _ = tran.StringSetAsync(key, "value");
                    await tran.ExecuteAsync();
                }
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyNotExists",
            "StringSetAsync",
            "StringSet[Async](key, value, When.NotExists)"));

    [Fact]
    // A loop *inside* the function still disqualifies: the walk hits it before the boundary, as it must
    public Task LoopInsideLocalFunctionDeclaringTransaction_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, RedisValue[] values)
            {
                await Queue();

                async Task Queue()
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
            "StringSet[Async](key, value, When.NotExists)"));

    [Fact]
    // A raw command queued through IDatabaseAsync.ExecuteAsync(string, ...) is still a queued command. It was
    // once invisible - skipped by name alongside the transaction's own ExecuteAsync() terminator - and these
    // two queued operations were "collapsed" into GETDEL with a PERSIST silently dropped in between.
    public Task RawExecuteAsyncBetweenOperations_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringGetAsync(key);
                _ = tran.ExecuteAsync("PERSIST", key);
                _ = tran.KeyDeleteAsync(key);
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // the same, on the guarded shape: a second queued command means the transaction is doing more than the
    // condition, whether or not we have a name for what it does
    public Task RawExecuteAsyncBesideGuardedOperation_IsNotFlagged() => VerifyAsync(
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
                _ = tran.ExecuteAsync("PFADD", key, "x");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // the control for the two above: the terminator itself must still be recognised, or nothing is ever
    // flagged. Sync Execute() as well as ExecuteAsync(), since both spellings reach here.
    public Task SyncExecuteTerminator_IsStillFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        class C
        {
            public void M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.KeyNotExists(key))|};
                _ = tran.StringSetAsync(key, "value");
                tran.Execute();
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyNotExists",
            "StringSetAsync",
            "StringSet[Async](key, value, When.NotExists)"));

    [Fact]
    // The worst of the dropped-argument cases, because the damage outlives the build: MSET takes one expiry
    // for the whole batch, not one per key, so collapsing these would make both keys permanent.
    public Task VariadicWouldDropExpiry_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey a, RedisKey b)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(a, "1", TimeSpan.FromMinutes(1));
                _ = tran.StringSetAsync(b, "2", TimeSpan.FromMinutes(5));
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // HSET's variadic form has no NX
    public Task VariadicWouldDropWhen_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.HashSetAsync(key, "f1", "v1", When.NotExists);
                _ = tran.HashSetAsync(key, "f2", "v2", When.NotExists);
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // GETEX has no NX/XX, so the ExpireWhen has nowhere to go
    public Task PairWouldDropExpireWhen_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringGetAsync(key);
                _ = tran.KeyExpireAsync(key, TimeSpan.FromMinutes(1), ExpireWhen.HasNoExpiry);
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // The caller's own when: is not an argument to move but a statement to overwrite - and this pairing says
    // "only if absent, and only if present", which is code we should not be rewriting on a guess.
    public Task GuardedOperationWithItsOwnWhen_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.KeyNotExists(key));
                _ = tran.StringSetAsync(key, "value", when: When.Exists);
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // The control, and the reason this is per-mapping coverage rather than "any extra argument is out":
    // SET does take an expiry alongside NX, so the commonest lock-acquire shape there is must still be
    // flagged. CommandFlags likewise - it appears on every command, and is carried over rather than dropped.
    public Task GuardedOperationWithExpiryAndFlags_IsStillFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.KeyNotExists(key))|};
                _ = tran.StringSetAsync(key, "value", TimeSpan.FromMinutes(1), flags: CommandFlags.DemandMaster);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyNotExists",
            "StringSetAsync",
            "StringSet[Async](key, value, When.NotExists)"));

    [Fact]
    // CommandFlags is on every single command, no suggestion mentions it, and the rewrite carries it over
    // verbatim - so it is never a reason to go quiet. Deliberately the *only* extra argument in these three,
    // where GuardedOperationWithExpiryAndFlags_IsStillFlagged has an expiry beside it and so would still pass
    // if flags alone suppressed everything. One per family, because the audit runs in three separate places.
    public Task FlagsAloneOnGuardedOperation_IsStillFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.KeyNotExists(key))|};
                _ = tran.StringSetAsync(key, "value", flags: CommandFlags.DemandMaster);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyNotExists",
            "StringSetAsync",
            "StringSet[Async](key, value, When.NotExists)"));

    [Fact]
    public Task FlagsAloneOnCommandPair_IsStillFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.StringGetAsync(key, CommandFlags.DemandMaster)|};
                _ = tran.KeyDeleteAsync(key, CommandFlags.DemandMaster);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER303").WithLocation(0).WithArguments(
            "StringGetAsync",
            "KeyDeleteAsync",
            "StringGetDelete[Async](key)",
            " (requires server 6.2 or later)"));

    [Fact]
    public Task FlagsAloneOnRepeatedCommand_IsStillFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.SetAddAsync(key, "a", CommandFlags.DemandMaster)|};
                _ = tran.SetAddAsync(key, "b", CommandFlags.FireAndForget);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER304").WithLocation(0).WithArguments(
            "SetAddAsync",
            "2",
            "SetAdd[Async](key, values)",
            ""));

    [Fact]
    // family C keeps the command exactly as written, so no argument of it can be dropped and none suppresses
    public Task RedundantConditionWithExtraArguments_IsStillFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.KeyExists(key))|};
                _ = tran.KeyExpireAsync(key, TimeSpan.FromMinutes(1), ExpireWhen.HasNoExpiry);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER302").WithLocation(0).WithArguments(
            "Condition.KeyExists",
            "KeyExpireAsync",
            "KeyExpire[Async](key, expiry), which returns false if the key did not exist"));

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
            "StringSet[Async](key, value, When.NotExists)"),
        Diagnostic("SER301").WithLocation(1).WithArguments(
            "Condition.StringEqual",
            "StringSetAsync",
            "StringSet[Async](key, value, ValueCondition.Equal(expected))",
            "8.4"));
}
