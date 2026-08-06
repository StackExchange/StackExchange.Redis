using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// Family D: no condition at all - a transaction used purely to make two commands atomic, where one compound
/// command already does both.
/// </summary>
public class SER303 : Verifier<TransactionAnalyzer>
{
    [Fact]
    public Task StringGetThenKeyDelete_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.StringGetAsync(key)|};
                _ = tran.KeyDeleteAsync(key);
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
    public Task StringGetThenKeyExpire_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.StringGetAsync(key)|};
                _ = tran.KeyExpireAsync(key, TimeSpan.FromMinutes(1));
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER303").WithLocation(0).WithArguments(
            "StringGetAsync",
            "KeyExpireAsync",
            "StringGetSetExpiry[Async](key, expiry)",
            " (requires server 6.2 or later)"));

    [Fact]
    // SET ... EX. No version clause on this one: setting a value and its lifetime in one command is as old
    // as SET's options, so naming a version would be noise.
    public Task StringSetThenKeyExpire_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.StringSetAsync(key, "value")|};
                _ = tran.KeyExpireAsync(key, TimeSpan.FromMinutes(1));
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER303").WithLocation(0).WithArguments(
            "StringSetAsync",
            "KeyExpireAsync",
            "StringSet[Async](key, value, expiry)",
            ""));

    [Fact]
    // an absolute expiry works the same way: Expiration converts implicitly from DateTime as well as TimeSpan
    public Task StringSetThenKeyExpireAtDateTime_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key, DateTime when)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.StringSetAsync(key, "value")|};
                _ = tran.KeyExpireAsync(key, when);
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER303").WithLocation(0).WithArguments(
            "StringSetAsync",
            "KeyExpireAsync",
            "StringSet[Async](key, value, expiry)",
            ""));

    [Fact]
    // the other order is a different program: SET clears any TTL, so EXPIRE-then-SET leaves no expiry at all
    public Task KeyExpireThenStringSet_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.KeyExpireAsync(key, TimeSpan.FromMinutes(1));
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // two expiries, one of which overrides the other: which of them the single command should carry is a
    // guess, and this rule does not guess
    public Task StringSetWithExpiryThenKeyExpire_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(key, "value", TimeSpan.FromMinutes(1));
                _ = tran.KeyExpireAsync(key, TimeSpan.FromMinutes(5));
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    public Task StringGetThenStringSet_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.StringGetAsync(key)|};
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER303").WithLocation(0).WithArguments(
            "StringGetAsync",
            "StringSetAsync",
            "StringSetAndGet[Async](key, value)",
            " (requires server 6.2 or later)"));

    [Fact]
    public Task HashGetThenHashDelete_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.HashGetAsync(key, "field")|};
                _ = tran.HashDeleteAsync(key, "field");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER303").WithLocation(0).WithArguments(
            "HashGetAsync",
            "HashDeleteAsync",
            "HashFieldGetAndDelete[Async](key, field)",
            " (requires server 8.0 or later)"));

    [Fact]
    // SMOVE is as old as sets, so this one carries no version clause at all - which is why the clause is built
    // per-mapping rather than baked into the message format
    public Task SetRemoveThenSetAdd_IsFlaggedWithoutVersion() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey source, RedisKey destination)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.SetRemoveAsync(source, "member")|};
                _ = tran.SetAddAsync(destination, "member");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER303").WithLocation(0).WithArguments(
            "SetRemoveAsync",
            "SetAddAsync",
            "SetMove[Async](source, destination, value)",
            ""));

    [Fact]
    // the effects are order-independent within a transaction, so the reverse order is the same move
    public Task SetAddThenSetRemove_IsFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey source, RedisKey destination)
            {
                var tran = db.CreateTransaction();
                _ = {|#0:tran.SetAddAsync(destination, "member")|};
                _ = tran.SetRemoveAsync(source, "member");
                await tran.ExecuteAsync();
            }
        }
        """,
        Diagnostic("SER303").WithLocation(0).WithArguments(
            "SetAddAsync",
            "SetRemoveAsync",
            "SetMove[Async](source, destination, value)",
            ""));

    [Fact]
    // SMOVE moves one member; two different members is not one move
    public Task SetMoveWithDifferentMembers_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey source, RedisKey destination)
            {
                var tran = db.CreateTransaction();
                _ = tran.SetRemoveAsync(source, "a");
                _ = tran.SetAddAsync(destination, "b");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // SET ... GET returns the value from *before* the write, so it matches get-then-set. Set-then-get asks for
    // the value *after* the write, which is a different thing, and must not be collapsed.
    public Task StringSetThenStringGet_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringSetAsync(key, "value");
                _ = tran.StringGetAsync(key);
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // Tempting but wrong: LMOVE moves the element it popped, and inside a transaction the pop's result is an
    // unresolved Task the caller cannot pass to the push - so whatever is being pushed is some other value.
    public Task ListRightPopThenListLeftPush_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey source, RedisKey destination)
            {
                var tran = db.CreateTransaction();
                _ = tran.ListRightPopAsync(source);
                _ = tran.ListLeftPushAsync(destination, "value");
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // different keys: two unrelated commands that genuinely want the transaction
    public Task DifferentKeys_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey a, RedisKey b)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringGetAsync(a);
                _ = tran.KeyDeleteAsync(b);
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // a condition present means this is families A-C's territory, not a compound collapse
    public Task WithCondition_IsNotFlaggedAsCompound() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                tran.AddCondition(Condition.KeyExists(key));
                _ = tran.StringGetAsync(key);
                _ = tran.KeyDeleteAsync(key);
                await tran.ExecuteAsync();
            }
        }
        """);

    [Fact]
    // three commands is not a pair
    public Task ThreeOperations_IsNotFlagged() => VerifyAsync(
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                _ = tran.StringGetAsync(key);
                _ = tran.KeyDeleteAsync(key);
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            }
        }
        """);
}
