using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// Family D, second flavour: the same command queued over and over, where one variadic call does the lot.
/// </summary>
public class SER304 : Verifier<TransactionAnalyzer>
{
    [Fact]
    public Task RepeatedSetAddOnOneKey_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.SetAddAsync(key, "a")|};
                _ = tran.SetAddAsync(key, "b");
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
    // more than two, to prove the shape is not secretly pair-only
    public Task ThreeRepeatedHashSets_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.HashSetAsync(key, "f1", "v1")|};
                _ = tran.HashSetAsync(key, "f2", "v2");
                _ = tran.HashSetAsync(key, "f3", "v3");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER304").WithLocation(0).WithArguments(
            "HashSetAsync",
            "3",
            "HashSet[Async](key, entries)",
            ""));

    [Fact]
    public Task RepeatedListRightPush_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.ListRightPushAsync(key, "a")|};
                _ = tran.ListRightPushAsync(key, "b");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER304").WithLocation(0).WithArguments(
            "ListRightPushAsync",
            "2",
            "ListRightPush[Async](key, values)",
            ""));

    [Fact]
    // SMISMEMBER is recent enough that the version clause appears
    public Task RepeatedSetContains_IsFlaggedWithVersion() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.SetContainsAsync(key, "a")|};
                _ = tran.SetContainsAsync(key, "b");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER304").WithLocation(0).WithArguments(
            "SetContainsAsync",
            "2",
            "SetContains[Async](key, values), which returns a bool per value",
            " (requires server 6.2 or later)"));

    [Fact]
    // the many-keys direction: MSET
    public Task RepeatedStringSetAcrossKeys_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey a, RedisKey b)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.StringSetAsync(a, "1")|};
                _ = tran.StringSetAsync(b, "2");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER304").WithLocation(0).WithArguments(
            "StringSetAsync",
            "2",
            "StringSet[Async](KeyValuePair<RedisKey, RedisValue>[])",
            ""));

    [Fact]
    public Task RepeatedStringGetAcrossKeys_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey a, RedisKey b)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.StringGetAsync(a)|};
                _ = tran.StringGetAsync(b);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER304").WithLocation(0).WithArguments(
            "StringGetAsync",
            "2",
            "StringGet[Async](keys)",
            ""));

    [Fact]
    public Task RepeatedKeyDeleteAcrossKeys_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey a, RedisKey b)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.KeyDeleteAsync(a)|};
                _ = tran.KeyDeleteAsync(b);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER304").WithLocation(0).WithArguments(
            "KeyDeleteAsync",
            "2",
            "KeyDelete[Async](keys)",
            ""));

    [Fact]
    // SADD takes one key and many values, so calls on different keys have no single-command form
    public Task RepeatedSetAddAcrossKeys_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey a, RedisKey b)
            {
                var tran = db.CreateTransaction();
                _ = tran.SetAddAsync(a, "m");
                _ = tran.SetAddAsync(b, "m");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // HSET takes one key and many field/value pairs, so calls across keys have no single-command form
    public Task RepeatedHashSetAcrossKeys_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey a, RedisKey b)
            {
                var tran = db.CreateTransaction();
                _ = tran.HashSetAsync(a, "f1", "v1");
                _ = tran.HashSetAsync(b, "f2", "v2");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // The key comparison is textual, so a local that is reassigned between the calls would read as "the same
    // key" when it is nothing of the sort - collapsing these into one HashSet would write both fields to "b".
    public Task KeyLocalReassignedBetweenCalls_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db)
            {
                RedisKey key = "a";
                var tran = db.CreateTransaction();
                _ = tran.HashSetAsync(key, "f1", "v1");
                key = "b";
                _ = tran.HashSetAsync(key, "f2", "v2");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // ... and the same where the reassignment is a compound one rather than a plain assignment
    public Task KeyLocalMutatedByRef_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            private static void Change(ref RedisKey key) => key = "b";

            public async Task M(IDatabase db)
            {
                RedisKey key = "a";
                var tran = db.CreateTransaction();
                _ = tran.HashSetAsync(key, "f1", "v1");
                Change(ref key);
                _ = tran.HashSetAsync(key, "f2", "v2");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // ... and conversely MSET wants distinct keys; two writes to one key is not what this rule is about
    public Task RepeatedStringSetOnOneKey_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(key, "1");
                _ = tran.StringSetAsync(key, "2");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // Tempting but wrong: LMPOP pops from the first *non-empty* key of those given, not from each of them, so
    // it is a different operation however similar the argument list looks. Same for ZMPOP.
    public Task RepeatedListLeftPopAcrossKeys_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey a, RedisKey b)
            {
                var tran = db.CreateTransaction();
                _ = tran.ListLeftPopAsync(a);
                _ = tran.ListLeftPopAsync(b);
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // different commands: not a variadic collapse, and not one of the compound pairs either
    public Task DifferentCommands_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.SetAddAsync(key, "a");
                _ = tran.ListRightPushAsync(key, "b");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // a condition present takes this out of family D entirely
    public Task WithCondition_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.KeyExists(key));
                _ = tran.SetAddAsync(key, "a");
                _ = tran.SetAddAsync(key, "b");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // The most common way to write this in practice, and deliberately still quiet: a loop body is one call site
    // whose key expression we cannot prove is loop-invariant, so we cannot tell a same-key collapse from a
    // per-key one. Left for a later pass rather than guessed at.
    public Task RepeatedInLoop_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, RedisValue[] values)
            {
                var tran = db.CreateTransaction();
                foreach (var value in values)
                {
                    _ = tran.SetAddAsync(key, value);
                }

                await tran.ExecuteAsync();
            }
        }
        """);
}
