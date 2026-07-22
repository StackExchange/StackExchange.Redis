using System;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Integration tests for <see cref="IDatabase.HashImport"/> / <see cref="IDatabaseAsync.HashImportAsync"/>
/// (the session-based <c>HIMPORT</c> bulk-import feature, Redis 8.10+).
/// </summary>
[RunPerProtocol]
public class HashImportTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    private static readonly RedisValue[] Fields = ["name", "email", "age"];

    [Fact]
    public async Task ImportsManyHashes()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        var prefix = Me();

        RedisKey k1 = prefix + ":1", k2 = prefix + ":2", k3 = prefix + ":3";
        await db.KeyDeleteAsync([k1, k2, k3]);

        HashImportEntry[] entries =
        [
            new(k1, new RedisValue[] { "alice", "a@example.com", 30 }),
            new(k2, new RedisValue[] { "bob", "b@example.com", 25 }),
            new(k3, new RedisValue[] { "carol", "c@example.com", 42 }),
        ];

        var result = await db.HashImportAsync(Fields, entries);
        Assert.Empty(result);

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

        HashImportEntry[] entries = [new(key, new RedisValue[] { "alice", "a@example.com", 30 })];
        var result = await db.HashImportAsync(Fields, entries);
        Assert.Empty(result);

        Assert.Equal("alice", await db.HashGetAsync(key, "name"));
        Assert.Equal(3, await db.HashLengthAsync(key));
    }

    [Fact]
    public async Task SingleEntry_ReplacesExistingHash()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);
        await db.HashSetAsync(key, [new("old", 1), new("keep", 2)]);

        // single-entry now uses HIMPORT (not HSET), so it replaces rather than merges - consistent with multi-entry
        await db.HashImportAsync(Fields, new HashImportEntry[] { new(key, new RedisValue[] { "alice", "a@x", 30 }) });

        Assert.Equal(3, await db.HashLengthAsync(key));
        Assert.False(await db.HashExistsAsync(key, "old"));
    }

    [Fact]
    public async Task ZeroEntries_IsNoOp()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        // no server round-trip expected; simply completes
        await db.HashImportAsync(Fields, Array.Empty<HashImportEntry>());
        await db.HashImportAsync(default, default); // fully empty is also a no-op
    }

    [Fact]
    public async Task KeyPrefixIsolation_PrefixesEntryKeys()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var prefix = Me() + ":";
        var db = conn.GetDatabase().WithKeyPrefix(prefix);
        var raw = conn.GetDatabase();

        const string inner = "u1";
        string full = prefix + inner;
        await raw.KeyDeleteAsync(full);

        await db.HashImportAsync(Fields, new HashImportEntry[] { new(inner, new RedisValue[] { "alice", "a@x", 30 }) });

        // written under the prefixed key
        Assert.Equal("alice", await raw.HashGetAsync(full, "name"));
    }

    [Fact]
    public async Task KeyPrefixIsolation_FailureKeyIsUnprefixed()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var prefix = Me() + ":";
        var db = conn.GetDatabase().WithKeyPrefix(prefix);
        var raw = conn.GetDatabase();

        const string inner = "u1";
        string full = prefix + inner;
        await raw.KeyDeleteAsync(full);
        await raw.StringSetAsync(full, "not-a-hash"); // wrong type under the prefixed key -> WRONGTYPE

        var failures = await db.HashImportAsync(Fields, new HashImportEntry[] { new(inner, new RedisValue[] { "a", "b", "c" }) });

        var failure = Assert.Single(failures);
        Assert.Equal(0, failure.Index);
        // reported in the caller's (un-prefixed) key-space, i.e. "u1" - NOT the prefixed key sent to the server
        Assert.Equal(inner, (string?)failure.Key);
        Assert.StartsWith("WRONGTYPE", failure.Message);
    }

    [Fact]
    public void MismatchedValueCount_ThrowsBeforeSending()
    {
        using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        // 3 fields but only 1 value in the entry -> synchronous ArgumentException
        HashImportEntry[] entries = [new(Me(), new RedisValue[] { "only-one" })];
        // validation runs synchronously, before any Task is returned
        Assert.Throws<ArgumentException>(() => { _ = db.HashImportAsync(Fields, entries); });
    }

    [Fact]
    public async Task DuplicateFieldNames_SurfacesServerError()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        RedisValue[] dupFields = ["f1", "f1"];
        HashImportEntry[] entries =
        [
            new(Me() + ":1", new RedisValue[] { "a", "b" }),
            new(Me() + ":2", new RedisValue[] { "c", "d" }),
        ];
        await Assert.ThrowsAsync<RedisServerException>(() => db.HashImportAsync(dupFields, entries));
    }

    [Fact]
    public async Task WorksInsideTransaction()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        var prefix = Me();
        RedisKey k1 = prefix + ":1", k2 = prefix + ":2";
        await db.KeyDeleteAsync([k1, k2]);

        var tran = db.CreateTransaction();
        var importTask = tran.HashImportAsync(Fields, new HashImportEntry[]
        {
            new(k1, new RedisValue[] { "alice", "a@x", 30 }),
            new(k2, new RedisValue[] { "bob", "b@x", 25 }),
        });
        // the hashes must not exist until the transaction executes
        Assert.False(await db.KeyExistsAsync(k1));

        Assert.True(await tran.ExecuteAsync());
        Assert.Empty(await importTask);

        Assert.Equal("alice", await db.HashGetAsync(k1, "name"));
        Assert.Equal("bob", await db.HashGetAsync(k2, "name"));
        Assert.Equal(3, await db.HashLengthAsync(k2));
    }

    [Fact]
    public async Task SingleEntryWorksInsideTransaction()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        var tran = db.CreateTransaction();
        var importTask = tran.HashImportAsync(Fields, new HashImportEntry[] { new(key, new RedisValue[] { "alice", "a@x", 30 }) });
        Assert.True(await tran.ExecuteAsync());
        Assert.Empty(await importTask);

        Assert.Equal("alice", await db.HashGetAsync(key, "name"));
        Assert.Equal(3, await db.HashLengthAsync(key));
    }

    [Fact]
    public async Task ExistingHashIsReplacedNotMerged()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        var prefix = Me();
        RedisKey k1 = prefix + ":1", k2 = prefix + ":2";
        await db.KeyDeleteAsync([k1, k2]);
        // pre-existing hash with extra fields that are NOT part of the import
        await db.HashSetAsync(k1, [new("old", 1), new("keep", 2)]);

        await db.HashImportAsync(Fields, new HashImportEntry[]
        {
            new(k1, new RedisValue[] { "alice", "a@x", 30 }),
            new(k2, new RedisValue[] { "bob", "b@x", 25 }),
        });

        // HIMPORT SET replaces the whole hash: the pre-existing 'old'/'keep' fields are gone
        Assert.Equal(3, await db.HashLengthAsync(k1));
        Assert.False(await db.HashExistsAsync(k1, "old"));
        Assert.Equal("alice", await db.HashGetAsync(k1, "name"));
    }

    [Fact]
    public async Task WrongTypeKey_ReportedAsPerEntryFailure()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        var prefix = Me();
        RedisKey k0 = prefix + ":0", k1 = prefix + ":1";
        await db.KeyDeleteAsync([k0, k1]);
        await db.StringSetAsync(k0, "i-am-a-string"); // wrong type for a hash import

        // per-entry failures are returned, not thrown
        var result = await db.HashImportAsync(Fields, new HashImportEntry[]
        {
            new(k0, new RedisValue[] { "alice", "a@x", 30 }), // index 0: WRONGTYPE
            new(k1, new RedisValue[] { "bob", "b@x", 25 }),   // index 1: fine
        });

        var failure = Assert.Single(result);
        Assert.Equal(0, failure.Index);
        Assert.Equal(k0, failure.Key);
        Assert.StartsWith("WRONGTYPE", failure.Message);

        // the string key is untouched...
        Assert.Equal("i-am-a-string", await db.StringGetAsync(k0));
        // ...and the other (valid) entry was still written (the import is not atomic)
        Assert.Equal("bob", await db.HashGetAsync(k1, "name"));
    }

    [Fact]
    public async Task WrongTypeKey_ReportedAsFailure_InsideTransaction()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        var prefix = Me();
        RedisKey k0 = prefix + ":0", k1 = prefix + ":1";
        await db.KeyDeleteAsync([k0, k1]);
        await db.StringSetAsync(k0, "i-am-a-string");

        var tran = db.CreateTransaction();
        var importTask = tran.HashImportAsync(Fields, new HashImportEntry[]
        {
            new(k0, new RedisValue[] { "alice", "a@x", 30 }),
            new(k1, new RedisValue[] { "bob", "b@x", 25 }),
        });
        Assert.True(await tran.ExecuteAsync());
        var result = await importTask;

        var failure = Assert.Single(result);
        Assert.Equal(0, failure.Index);
        Assert.Equal(k0, failure.Key);
        Assert.StartsWith("WRONGTYPE", failure.Message);
        Assert.Equal("bob", await db.HashGetAsync(k1, "name"));
    }

    [Fact]
    public async Task NotSupportedInsideBatch()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var batch = conn.GetDatabase().CreateBatch();
        HashImportEntry[] entries =
        [
            new(Me() + ":1", new RedisValue[] { "a", "b", "c" }),
            new(Me() + ":2", new RedisValue[] { "d", "e", "f" }),
        ];
        // the batch/transaction guard runs synchronously
        Assert.Throws<NotSupportedException>(() => { _ = batch.HashImportAsync(Fields, entries); });
    }
}
