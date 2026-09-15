using System;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Integration tests for <see cref="IDatabase.HashImport"/> / <see cref="IDatabaseAsync.HashImportAsync"/> and the
/// reusable <see cref="HashImport"/> field-set (the session-based <c>HIMPORT</c> feature, Redis 8.10+).
/// </summary>
[RunPerProtocol]
public class HashImportTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private static RedisValue[] Values(string name, string email, int age) => [name, email, age];

    [Fact]
    public async Task ImportsManyHashesReusingOneFieldSet()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        var prefix = Me();

        RedisKey k1 = prefix + ":1", k2 = prefix + ":2", k3 = prefix + ":3";
        await db.KeyDeleteAsync([k1, k2, k3]);

        await using var fieldSet = HashImport.Create("name", "email", "age");
        await db.HashImportAsync(k1, fieldSet, Values("alice", "a@example.com", 30));
        await db.HashImportAsync(k2, fieldSet, Values("bob", "b@example.com", 25));
        await db.HashImportAsync(k3, fieldSet, Values("carol", "c@example.com", 42));

        Assert.Equal("alice", await db.HashGetAsync(k1, "name"));
        Assert.Equal("a@example.com", await db.HashGetAsync(k1, "email"));
        Assert.Equal(30, (int)await db.HashGetAsync(k1, "age"));
        Assert.Equal("bob", await db.HashGetAsync(k2, "name"));
        Assert.Equal("carol", await db.HashGetAsync(k3, "name"));
        Assert.Equal(3, await db.HashLengthAsync(k3));
    }

    [Fact]
    public async Task SingleEntry_Works()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        await using var fieldSet = HashImport.Create("name", "email", "age");
        await db.HashImportAsync(key, fieldSet, Values("alice", "a@example.com", 30));

        Assert.Equal("alice", await db.HashGetAsync(key, "name"));
        Assert.Equal(3, await db.HashLengthAsync(key));
    }

    [Fact]
    public async Task ExistingHashIsReplacedNotMerged()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);
        await db.HashSetAsync(key, [new("old", 1), new("keep", 2)]);

        await using var fieldSet = HashImport.Create("name", "email", "age");
        await db.HashImportAsync(key, fieldSet, Values("alice", "a@x", 30));

        // HIMPORT SET replaces the whole hash: the pre-existing 'old'/'keep' fields are gone
        Assert.Equal(3, await db.HashLengthAsync(key));
        Assert.False(await db.HashExistsAsync(key, "old"));
        Assert.Equal("alice", await db.HashGetAsync(key, "name"));
    }

    [Fact]
    public async Task MismatchedValueCount_ThrowsBeforeSending()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        await using var fieldSet = HashImport.Create("name", "email", "age");
        // 3 fields but only 1 value -> synchronous ArgumentException, before any Task is returned
        Assert.Throws<ArgumentException>(() => { _ = db.HashImportAsync(Me(), fieldSet, new RedisValue[] { "only-one" }); });
    }

    [Fact]
    public async Task NullFieldSet_Throws()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        Assert.Throws<ArgumentNullException>(() => { _ = db.HashImportAsync(Me(), null!, new RedisValue[] { "x" }); });
    }

    [Fact]
    public async Task WrongTypeKey_ThrowsButOtherKeysSucceed()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        var prefix = Me();
        RedisKey k0 = prefix + ":0", k1 = prefix + ":1";
        await db.KeyDeleteAsync([k0, k1]);
        await db.StringSetAsync(k0, "i-am-a-string"); // wrong type for a hash import

        await using var fieldSet = HashImport.Create("name", "email", "age");

        var ex = await Assert.ThrowsAsync<RedisServerException>(() => db.HashImportAsync(k0, fieldSet, Values("alice", "a@x", 30)));
        Assert.StartsWith("WRONGTYPE", ex.Message);

        // each import is applied on its own: a later valid key still succeeds despite the earlier failure
        await db.HashImportAsync(k1, fieldSet, Values("bob", "b@x", 25));
        Assert.Equal("bob", await db.HashGetAsync(k1, "name"));
        Assert.Equal("i-am-a-string", await db.StringGetAsync(k0)); // untouched
    }

    [Fact]
    public void DuplicateFieldNames_RejectedAtCreate()
    {
        // rejected client-side: the server would reject the PREPARE, but that is injected fire-and-forget and would
        // only surface indirectly as a "no such fieldset" failure on every SET - so we fail fast at the mistake.
        var ex = Assert.Throws<ArgumentException>(() => HashImport.Create("f1", "f1"));
        Assert.Contains("Duplicate field name", ex.Message);
    }

    [Fact]
    public async Task FieldsAreSnapshotAtCreate()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        var fieldNames = new RedisValue[] { "name", "email", "age" };
        await using var fieldSet = HashImport.Create(fieldNames);
        fieldNames[0] = "MUTATED"; // mutate the caller's array after Create - must not affect the field-set

        await db.HashImportAsync(key, fieldSet, Values("alice", "a@x", 30));
        Assert.Equal("alice", await db.HashGetAsync(key, "name")); // still 'name', not 'MUTATED'
        Assert.False(await db.HashExistsAsync(key, "MUTATED"));
    }

    [Fact]
    public void NullFieldName_RejectedAtCreate()
    {
        var ex = Assert.Throws<ArgumentException>(() => HashImport.Create("a", RedisValue.Null));
        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void EmptyFieldName_Allowed()
    {
        // the server accepts an empty field name (a hash can legitimately have an empty-string field), so we do too
        using var fieldSet = HashImport.Create("", "b");
        Assert.NotNull(fieldSet);
    }

    [Fact]
    public async Task KeyPrefixIsolation_PrefixesKey()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var prefix = Me() + ":";
        var db = conn.GetDatabase().WithKeyPrefix(prefix);
        var raw = conn.GetDatabase();

        const string inner = "u1";
        string full = prefix + inner;
        await raw.KeyDeleteAsync(full);

        await using var fieldSet = HashImport.Create("name", "email", "age");
        await db.HashImportAsync(inner, fieldSet, Values("alice", "a@x", 30));

        // written under the prefixed key
        Assert.Equal("alice", await raw.HashGetAsync(full, "name"));
    }

    [Fact]
    public async Task WorksInsideBatch()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        var prefix = Me();
        RedisKey k1 = prefix + ":1", k2 = prefix + ":2";
        await db.KeyDeleteAsync([k1, k2]);

        await using var fieldSet = HashImport.Create("name", "email", "age");
        var batch = db.CreateBatch();
        var t1 = batch.HashImportAsync(k1, fieldSet, Values("alice", "a@x", 30));
        var t2 = batch.HashImportAsync(k2, fieldSet, Values("bob", "b@x", 25));
        batch.Execute();
        await Task.WhenAll(t1, t2);

        Assert.Equal("alice", await db.HashGetAsync(k1, "name"));
        Assert.Equal("bob", await db.HashGetAsync(k2, "name"));
    }

    /// <summary>
    /// The <c>PREPARE</c> is injected <b>once per connection</b>, not once per import.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other test here passes either way, because <c>PREPARE</c> is idempotent - so the dedup that is
    /// the entire point of the field-set is invisible unless something counts. The server counts it for us:
    /// <c>INFO commandstats</c> reports <c>himport|prepare</c> and <c>himport|set</c> separately.
    /// </para>
    /// <para>
    /// Worth having from the moment the injection moved off the bridge's hard-coded type test onto
    /// <c>IMultiMessage</c>: a migration whose only failure mode is "does it too often" needs a test that
    /// can see how often.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePrepareIsInjectedOncePerConnectionNotPerImport()
    {
        // allowAdmin for CONFIG RESETSTAT: the shared fixture connection has it, but the high-integrity
        // subclass cannot share and would otherwise get a connection without it
        await using var conn = Create(require: RedisFeatures.v8_10_0, allowAdmin: true);
        var db = conn.GetDatabase();
        var server = conn.GetServers().Single(s => !s.IsReplica);
        var prefix = Me();
        const int Imports = 5;

        await server.ExecuteAsync("CONFIG", "RESETSTAT");

        await using var fieldSet = HashImport.Create("name", "age");
        for (var i = 0; i < Imports; i++)
        {
            // two values, matching the two fields above; not a slice of Values(), which needs System.Range
            await db.HashImportAsync($"{prefix}:{i}", fieldSet, new RedisValue[] { $"user{i}", i });
        }

        var stats = (string?)await server.ExecuteAsync("INFO", "commandstats") ?? "";
        var sets = CallCount(stats, "himport|set");
        var prepares = CallCount(stats, "himport|prepare");

        // not an equality: commandstats is server-wide and this class runs once per protocol, so a
        // concurrent sibling inflates both numbers together. The property survives that, and it is the one
        // that matters - injected per import, the two counts are EQUAL; injected per connection, there are
        // a handful of prepares against however many imports ran.
        Assert.True(sets >= Imports, $"expected at least {Imports} HIMPORT SET, saw {sets}");
        Assert.True(
            prepares < sets,
            $"HIMPORT PREPARE was injected {prepares} time(s) for {sets} SET(s) - per import rather than per connection");
    }

    // cmdstat_<name>:calls=N,usec=...
    private static int CallCount(string commandStats, string command)
    {
        foreach (var line in commandStats.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith($"cmdstat_{command}:", StringComparison.OrdinalIgnoreCase)) continue;

            var calls = trimmed.Split(',')[0].Split('=');
            return calls.Length == 2 ? int.Parse(calls[1]) : 0;
        }

        return 0;
    }

    [Fact]
    public async Task UseAfterDispose_Throws()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        var fieldSet = HashImport.Create("name", "email", "age");
        fieldSet.Dispose();
        // rejected before anything is sent (the field-set may already have been DISCARDed on the server)
        Assert.Throws<ObjectDisposedException>(() => { _ = db.HashImportAsync(Me(), fieldSet, Values("a", "b", 1)); });
    }

    [Fact]
    public async Task DoubleDispose_IsNoOp()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        var fieldSet = HashImport.Create("name", "email", "age");
        await db.HashImportAsync(key, fieldSet, Values("alice", "a@x", 30));
        fieldSet.Dispose();
        fieldSet.Dispose(); // idempotent: no second DISCARD, no throw
        await fieldSet.DisposeAsync(); // also idempotent across the sync/async forms

        Assert.Equal("alice", await db.HashGetAsync(key, "name"));
    }

    [Fact]
    public async Task NotSupportedInsideTransaction()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var tran = conn.GetDatabase().CreateTransaction();
        await using var fieldSet = HashImport.Create("name", "email", "age");
        // the transaction guard runs synchronously
        Assert.Throws<NotSupportedException>(() => { _ = tran.HashImportAsync(Me(), fieldSet, Values("a", "b", 1)); });
    }
}
