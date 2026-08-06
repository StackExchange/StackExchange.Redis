using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// Family A: the condition duplicates a <c>when:</c> argument the queued command already has. Version-free,
/// so every one of these is a pure mechanical rewrite.
/// </summary>
public class SER300 : Verifier<TransactionAnalyzer>
{
    [Fact]
    public Task KeyNotExistsGuardingStringSet_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.KeyNotExists(key))|};
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyNotExists",
            "StringSetAsync",
            "StringSet[Async](key, value, When.NotExists)"));

    [Fact]
    public Task KeyExistsGuardingStringSet_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.KeyExists(key))|};
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyExists",
            "StringSetAsync",
            "StringSet[Async](key, value, When.Exists)"));

    [Fact]
    public Task HashNotExistsGuardingHashSet_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.HashNotExists(key, "field"))|};
                _ = tran.HashSetAsync(key, "field", "value");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.HashNotExists",
            "HashSetAsync",
            "HashSet[Async](key, field, value, When.NotExists)"));

    [Fact]
    public Task SortedSetNotContainsGuardingSortedSetAdd_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.SortedSetNotContains(key, "member"))|};
                _ = tran.SortedSetAddAsync(key, "member", 1.0);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.SortedSetNotContains",
            "SortedSetAddAsync",
            "SortedSetAdd[Async](key, member, score, SortedSetWhen.NotExists)"));

    [Fact]
    public Task SortedSetContainsGuardingSortedSetAdd_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.SortedSetContains(key, "member"))|};
                _ = tran.SortedSetAddAsync(key, "member", 1.0);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.SortedSetContains",
            "SortedSetAddAsync",
            "SortedSetAdd[Async](key, member, score, SortedSetWhen.Exists)"));

    [Fact]
    // the condition is on the *destination*, which is KeyRename's first argument's counterpart - so this is
    // also the case that proves the key comparison uses the renamed-to key, not just "some key matched"
    public Task KeyNotExistsGuardingKeyRename_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, RedisKey other)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.KeyNotExists(key))|};
                _ = tran.KeyRenameAsync(key, other);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyNotExists",
            "KeyRenameAsync",
            "KeyRename[Async](key, newKey, When.NotExists)"));

    [Fact]
    // synchronous surface: ITransaction is both IDatabaseAsync and the sync-shaped queueing API, and the
    // mapping trims the Async suffix - so the non-suffixed spelling has to land on the same rule
    public Task SyncOverload_IsFlagged() => VerifyAsync(
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
    // ITransactionAsync, not ITransaction: IDatabase hides IDatabaseAsync.CreateTransaction to refine the
    // return type, so code written against IDatabaseAsync gets the async-only interface. Both are resolved by
    // the analyzer, and this is what proves the second one is actually wired rather than just mentioned.
    public Task AsyncOnlyTransactionInterface_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        // IDatabaseAsync.CreateTransaction is itself [Experimental] (SER007); opted in here rather than in the
        // shared harness, so the gate keeps working for every other case
        #pragma warning disable SER007
        class C
        {
            public async Task M(IDatabaseAsync db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.KeyNotExists(key))|};
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER300").WithLocation(0).WithArguments(
            "Condition.KeyNotExists",
            "StringSetAsync",
            "StringSet[Async](key, value, When.NotExists)"));

    [Fact]
    // family A needs the same field too, not just the same key: a condition about field "a" does not guard a
    // write to field "b", so the transaction is doing real work
    public Task DifferentHashField_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.HashNotExists(key, "a"));
                _ = tran.HashSetAsync(key, "b", "value");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // likewise a sorted-set member
    public Task DifferentSortedSetMember_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.SortedSetNotContains(key, "a"));
                _ = tran.SortedSetAddAsync(key, "b", 1.0);
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // HashExists + HashSet has no HSETXX to collapse into; the nearest thing is a different method entirely
    // (HashFieldSet with ValueCondition.Exists), so this deliberately stays quiet rather than mis-suggesting
    public Task HashExistsGuardingHashSet_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.HashExists(key, "field"));
                _ = tran.HashSetAsync(key, "field", "value");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // no server-side hash compare-and-set exists at all
    public Task HashEqualGuardingHashSet_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.HashEqual(key, "field", "old"));
                _ = tran.HashSetAsync(key, "field", "new");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // likewise for list index writes - LSET has no conditional form
    public Task ListIndexEqualGuardingListSetByIndex_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.ListIndexEqual(key, 0, "old"));
                _ = tran.ListSetByIndexAsync(key, 0, "new");
                await tran.ExecuteAsync();
            }
        }
        """);
}
