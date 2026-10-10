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

    /// <summary>
    /// A legacy <see cref="Task"/>-returning method served from a group: the database's async state rides along,
    /// against a real server, so the pending path is the one exercised.
    /// </summary>
    [Fact]
    public async Task ATaskFromAGroupCarriesTheDatabasesAsyncState()
    {
        await using var conn = Create();
        var state = new object();
        var db = conn.GetDatabase(asyncState: state);
        var key = Me();
        await db.KeyDeleteAsync(key);
        await db.StringSetAsync(key, "hello world");

        // what an extender's `Task<RedisValue> SubstringAsync(...)` proxy is, in full
        Task<RedisValue> task = db.AsTask(db.Contoso().SubstringAsync(key, 0, 4));
        Assert.Same(state, task.AsyncState);
        Assert.Equal("hello", await task);

        // the non-generic form, for a group method that returns plain ValueTask
        Task done = db.AsTask(db.Context.SendAsync($"{RedisCommand.SET}{(RedisKey)key}{(RedisValue)"x"}"));
        Assert.Same(state, done.AsyncState);
        await done;

        // fire-and-forget carries no state, as the shipped surface's never has
        Task<RedisValue> fired = db.AsTask(db.Contoso().SubstringAsync(key, 0, 0, CommandFlags.FireAndForget), CommandFlags.FireAndForget);
        Assert.Null(fired.AsyncState);
    }

    /// <summary>
    /// A fault on a task nobody awaits is marked observed, as v3's always were - where <c>ValueTask.AsTask()</c>
    /// raises <see cref="TaskScheduler.UnobservedTaskException"/>. A transaction that aborts faults every
    /// discarded <c>_ = tran.SomethingAsync(...)</c>, so this is load-bearing for a wrapper library.
    /// </summary>
    [Fact]
    public void ADroppedFaultIsObserved()
    {
        IDatabaseAsync db = new RespDatabaseContext(new RespContext().WithExecutor(new FakeExecutor("+OK\r\n")))
            .AsDatabase(NSubstitute.Substitute.For<IConnectionMultiplexer>());

        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            lock (unobserved) unobserved.AddRange(e.Exception.InnerExceptions);
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            // the control: the same drop through ValueTask.AsTask(). If the runtime does not finalize it here, the
            // assertion below would pass for the wrong reason, so the test says it could not tell instead
            var control = new InvalidOperationException("control");
            Drop(pending => _ = pending.AsTask(), control);
            Collect();
            lock (unobserved)
            {
                if (!unobserved.Contains(control)) Assert.Skip("the runtime did not finalize the control task; nothing can be concluded");
            }

            var ours = new InvalidOperationException("ours");
            Drop(pending => _ = db.AsTask(pending), ours);
            Collect();
            lock (unobserved) Assert.DoesNotContain(ours, unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        // in a frame of its own, so nothing in the test's frame still references the dropped task
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Drop(Action<ValueTask<int>> convert, Exception fault)
        {
            // backed by an IValueTaskSource, as a group method's result is: AsTask() then makes a task of its own
            var source = new PendingSource();
            convert(new ValueTask<int>(source, source.Version));
            source.Fault(fault); // faults while pending: the path a dropped command's task takes
        }

        static void Collect()
        {
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }

    /// <summary>The smallest <see cref="IValueTaskSource{TResult}"/>: pending until faulted.</summary>
    private sealed class PendingSource : System.Threading.Tasks.Sources.IValueTaskSource<int>
    {
        private System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<int> _core;

        public short Version => _core.Version;

        public void Fault(Exception fault) => _core.SetException(fault);

        public int GetResult(short token) => _core.GetResult(token);

        public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token, System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)
            => _core.OnCompleted(continuation, state, token, flags);
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
        var executor = new FakeExecutor("$5\r\nhello\r\n");
        var db = new RespDatabaseContext(new RespContext().WithExecutor(executor));

        await db.Contoso().SubstringAsync("k", 0, 4);
        Assert.Equal(CommandFlags.CommandRetryNever, executor.Flags[0] & CommandFlagsInternal.MaskRetryCategory);

        await db.Contoso().SubstringAsync("k", 0, 4, CommandFlags.CommandRetryReadOnly);
        Assert.Equal(CommandFlags.CommandRetryReadOnly, executor.Flags[1] & CommandFlagsInternal.MaskRetryCategory);
    }
}

// ---------------------------------------------------------------------------------------------------
// Everything below is what the EXTENDING LIBRARY writes; it needs no change to StackExchange.Redis.
// ---------------------------------------------------------------------------------------------------

/// <summary>The command group: a context plus a name, and nothing else.</summary>
public readonly struct ContosoCommands(RespContext context)
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
    public static ContosoCommands Contoso(this RespDatabaseContext context) => new(context.Raw);

    /// <inheritdoc cref="Contoso(RespDatabaseContext)"/>
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
