using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The samples in <c>docs/Extending.md</c>, compiled and run.
/// </summary>
/// <remarks>
/// <para>
/// The audience for that document is a library author adding a command this client does not have -
/// NRedisStack and friends - so the thing worth guarding is that the shape they are told to copy actually
/// works: the group, the accessor, the command, the custom reply handler.
/// </para>
/// <para>
/// <c>SUBSTR</c> stands in for the module command a real extender would be adding. It is a deliberate
/// choice: it is a genuine server command that this client has no API for and does not even have in
/// <see cref="RedisCommand"/>, so the sample exercises the unknown-command path exactly as <c>JSON.GET</c>
/// would - and, being an alias of <c>GETRANGE</c>, it comes with an oracle to check the answer against.
/// </para>
/// </remarks>
public class RespExtensionAuthorTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task TheGroupShapeWorksEndToEnd()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync(key);
        await db.StringSetAsync(key, "hello world");

        // this is the call a consumer of the extending library writes
        var actual = await db.Contoso().SubstringAsync(key, 0, 4);

        // and the C# 14 spelling the doc offers as an alternative, which drops the parentheses
        Assert.Equal(actual, await db.Contoso2.SubstringAsync(key, 0, 4));

        Assert.Equal("hello", actual);
        Assert.Equal(await db.StringGetRangeAsync(key, 0, 4), actual); // the oracle
    }

    [Fact]
    public async Task ACustomHandlerReadsAReplyTheClientDoesNotKnow()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync(key);
        await db.StringSetAsync(key, "hello world");

        var length = await db.Contoso().SubstringLengthAsync(key, 0, 4);
        Assert.Equal(5, length);
    }

    [Fact]
    public async Task KeysAreKeys_NotJustArguments()
    {
        // the whole reason the key hole is spelled differently: a prefixed context rewrites it. A library
        // that wrote the key as a value would silently miss the prefix - and the cluster slot, and
        // invalidation - which is the failure this sample exists to steer people away from.
        await using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync("p:" + key);
        await db.StringSetAsync("p:" + key, "hello world");

        var prefixed = db.Context.AppendKeyPrefix("p:");
        Assert.Equal("hello", await prefixed.Contoso().SubstringAsync(key, 0, 4));
    }

    [Fact]
    public async Task TheAdHocEscapeHatchIsStillThere()
    {
        // no group, no extension method: one call, for a command used once
        await using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync(key);
        await db.StringSetAsync(key, "hello world");

        using var reply = await db.ExecuteRespAsync("SUBSTR", new RedisKeyOrValue[] { (RedisKey)key, (RedisValue)0, (RedisValue)4 });
        Assert.Equal("hello", reply.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public async Task AnUnknownCommandIsNotRetriedUnlessTheAuthorSaysSo()
    {
        // the doc's most load-bearing claim for a library author: the client has no table entry for a
        // module command, so it assumes the worst - the command is not replayed after a reconnect, and
        // not cached. That is a safe default, not a free one; saying the category is opting IN.
        var executor = new FlagRecordingExecutor("$5\r\nhello\r\n");
        var db = new RespDatabaseContext(new RespContext().WithExecutor(executor));

        await db.Contoso().SubstringAsync("k", 0, 4);
        Assert.Equal(CommandFlags.CommandRetryNever, executor.Flags[0] & Message.MaskRetryCategory);

        await db.Contoso().SubstringAsync("k", 0, 4, CommandFlags.CommandRetryReadOnly);
        Assert.Equal(CommandFlags.CommandRetryReadOnly, executor.Flags[1] & Message.MaskRetryCategory);
    }

    /// <summary>Records the flags each request carried, so the retry-category claim can be checked.</summary>
    private sealed class FlagRecordingExecutor(params string[] replies) : IRespExecutor
    {
        private int _next;

        public List<CommandFlags> Flags { get; } = [];

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Flags.Add(request.Flags);
            return RespPayload.Create(Encoding.UTF8.GetBytes(replies[Math.Min(_next++, replies.Length - 1)]));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }
}

// ---------------------------------------------------------------------------------------------------
// Everything below is what the EXTENDING LIBRARY writes; it needs no change to StackExchange.Redis.
// ---------------------------------------------------------------------------------------------------

/// <summary>The command group: a context plus a name, and nothing else.</summary>
public readonly struct ContosoCommands(in RespContext context)
{
    /// <summary>The context these commands are sent through.</summary>
    public RespContext Context { get; } = context;
}

/// <summary>The accessor that reaches the group, and the commands themselves.</summary>
public static class ContosoExtensions
{
    // rendered once, not per call: "SUBSTR" is a constant, and preform keeps its RESP bulk string ready
    private static readonly RespCommand Substr = "SUBSTR".Command(preform: true);

    /// <summary>The Contoso commands, off a database context.</summary>
    /// <remarks>
    /// <b>The context, not the target</b> - the same shape this client's own groups use. A context is
    /// what carries the local differences (the key prefix, the database), so the commands must come off
    /// it; the target overload below is sugar that forwards to whatever context the target holds.
    /// </remarks>
    public static ContosoCommands Contoso(this in RespDatabaseContext context) => new(context.Raw);

    /// <inheritdoc cref="Contoso(in RespDatabaseContext)"/>
    public static ContosoCommands Contoso<TTarget>(this TTarget target) where TTarget : IRespKeyspaceTarget
        => target.Context.Contoso();

    /// <summary>SUBSTR: the substring between two inclusive offsets.</summary>
    public static ValueTask<RedisValue> SubstringAsync(
        this in ContosoCommands contoso,
        RedisKey key,
        long start,
        long end,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => contoso.Context.SendAsync<RedisValue>(
            $"{Substr}{key}{start}{end}", flags, cancellationToken: cancellationToken);

    /// <summary>The same command, read by a handler of the library's own.</summary>
    public static ValueTask<int> SubstringLengthAsync(
        this in ContosoCommands contoso,
        RedisKey key,
        long start,
        long end,
        CommandFlags flags = CommandFlags.None,
        CancellationToken cancellationToken = default)
        => contoso.Context.SendAsync(
            $"{Substr}{key}{start}{end}", flags, LengthHandler.Instance, cancellationToken: cancellationToken);

    /// <summary>Reads the reply without materialising it: the length of the blob, not the blob.</summary>
    private sealed class LengthHandler : IRespHandler<int>
    {
        public static readonly LengthHandler Instance = new();

        public int Parse(ref RespReader reader) => reader.ScalarLength();
    }
}

/// <summary>The same accessor as an extension PROPERTY, which is what C# 14 adds.</summary>
/// <remarks>
/// Here so that the alternative the documentation offers is known to compile, rather than assumed to.
/// A real library would pick one spelling; the name differs only to keep both in one test.
/// </remarks>
public static class ContosoPropertyExtensions
{
    extension(IRespKeyspaceTarget target)
    {
        /// <summary>The Contoso commands.</summary>
        public ContosoCommands Contoso2 => new(target.Context.Raw);
    }
}
