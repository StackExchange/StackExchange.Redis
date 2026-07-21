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

        await db.HashImportAsync(Fields, entries);

        Assert.Equal("alice", await db.HashGetAsync(k1, "name"));
        Assert.Equal("a@example.com", await db.HashGetAsync(k1, "email"));
        Assert.Equal(30, (int)await db.HashGetAsync(k1, "age"));
        Assert.Equal("bob", await db.HashGetAsync(k2, "name"));
        Assert.Equal("carol", await db.HashGetAsync(k3, "name"));
        Assert.Equal(3, await db.HashLengthAsync(k3));
    }

    [Fact]
    public async Task SingleEntry_UsesPlainHset()
    {
        await using var conn = Create(require: RedisFeatures.v8_10_0);
        var db = conn.GetDatabase();
        RedisKey key = Me();
        await db.KeyDeleteAsync(key);

        HashImportEntry[] entries = [new(key, new RedisValue[] { "alice", "a@example.com", 30 })];
        await db.HashImportAsync(Fields, entries);

        Assert.Equal("alice", await db.HashGetAsync(key, "name"));
        Assert.Equal(3, await db.HashLengthAsync(key));
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
        await importTask;

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
        await importTask;

        Assert.Equal("alice", await db.HashGetAsync(key, "name"));
        Assert.Equal(3, await db.HashLengthAsync(key));
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
