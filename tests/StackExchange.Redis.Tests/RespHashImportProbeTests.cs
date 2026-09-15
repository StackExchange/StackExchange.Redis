using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Interpolated;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The third seam: a command needing a <b>connection-local</b> preamble, injected once the connection is
/// finally known. <c>HIMPORT SET</c> references a field-set that must have been <c>PREPARE</c>d on that
/// same physical connection, and the multiplexer does not promise which connection a command lands on.
/// </summary>
/// <remarks>
/// <para>
/// The queue called this "bridge-level", a third mechanism after compose (<c>EVALSHA</c>) and accumulate
/// (<c>MULTI</c>), on the grounds that the existing implementation injects from inside the bridge's write
/// lock via a hard-coded type test on <c>HashImportSetMessage</c>. The probe asks whether the frame surface
/// needs anything new for it, or whether <see cref="IRespPreambleGate"/> - built for <c>SCRIPT LOAD</c> -
/// already covers it with a different scope.
/// </para>
/// <para>
/// These run against a real server. The library gates <c>HashImport</c> at 8.10, but the command exists
/// earlier, so the probe asks the server what it supports rather than asking the version.
/// </para>
/// </remarks>
public partial class RespHashImportProbeTests(ITestOutputHelper output, SharedConnectionFixture fixture) : TestBase(output, fixture)
{
    // the tokens, framed once at compile time rather than encoded per call - the same route the command
    // groups take, and the first thing to check when asking whether a new command family fits
    internal static partial class Tokens
    {
        /// <summary>The <c>PREPARE</c> subcommand of <c>HIMPORT</c>.</summary>
        [Resp("PREPARE")]
        internal static partial RespFragment Prepare { get; }

        /// <summary>The <c>SET</c> subcommand of <c>HIMPORT</c>.</summary>
        [Resp("SET")]
        internal static partial RespFragment Set { get; }

        /// <summary>The two field names this probe's field-set declares.</summary>
        [Resp("name", "age")]
        internal static partial RespFragment NameAndAge { get; }
    }

    private static RespContext NewContext(IConnectionMultiplexer conn, int db)
    {
        var database = (RedisBase)conn.GetDatabase(db);
        return new RespContext(database.multiplexer.CommandMap, database: db)
            .WithExecutor(new RespMessageExecutor(database, db));
    }

    private async Task<IConnectionMultiplexer> RequireHashImportAsync()
    {
        var conn = Create();
        var server = conn.GetServers()[0];
        var info = await server.ExecuteAsync("COMMAND", "INFO", "HIMPORT");
        // an unknown command still answers, with a nil entry - which is the "not supported" signal here
        if (info.Length != 1 || info[0].IsNull)
        {
            await conn.DisposeAsync();
            Assert.Skip("Server does not support HIMPORT");
        }

        return conn;
    }

    /// <summary>
    /// A gate whose scope is the <b>connection</b>, not the endpoint - the difference between a field-set
    /// (session-local) and a loaded script (server-wide).
    /// </summary>
    private sealed class FieldSetGate : IRespPreambleGate
    {
        // one table per gate: the gate IS the field-set's identity, so membership means "this field-set is
        // prepared on that connection". A dead connection is collected with its entry, which is exactly the
        // reconnect behaviour wanted - a fresh connection has prepared nothing.
        private readonly ConditionalWeakTable<PhysicalConnection, object> _prepared = new();
        private static readonly object Marker = new();

        internal int Injections;
        internal int Established;

        internal readonly bool ClaimOnWrite;

        internal FieldSetGate(bool claimOnWrite) => ClaimOnWrite = claimOnWrite;

        public bool IsNeeded(PhysicalConnection connection)
        {
            if (!ClaimOnWrite) return Claimed(connection) ? false : Count();
            if (Claimed(connection)) return false;
            Claim(connection);
            return Count();
        }

        private bool Count()
        {
            Interlocked.Increment(ref Injections);
            return true;
        }

        private bool Claimed(PhysicalConnection connection)
        {
            lock (_prepared) { return _prepared.TryGetValue(connection, out _); }
        }

        private void Claim(PhysicalConnection connection)
        {
            lock (_prepared)
            {
                if (!_prepared.TryGetValue(connection, out _)) _prepared.Add(connection, Marker);
            }
        }

        public void OnEstablished(PhysicalConnection connection)
        {
            Interlocked.Increment(ref Established);
            if (!ClaimOnWrite) Claim(connection);
        }
    }

    [Fact]
    public async Task AConnectionLocalPreambleIsTheScriptSeamWithADifferentScope()
    {
        await using var conn = await RequireHashImportAsync();
        var db = conn.GetDatabase();
        var ctx = NewContext(conn, db.Database);

        var prefix = Me();
        RedisKey k1 = prefix + ":1", k2 = prefix + ":2";
        await db.KeyDeleteAsync([k1, k2]);

        var fieldSet = (RedisValue)(prefix + ":fs");
        var gate = new FieldSetGate(claimOnWrite: true);

        for (var i = 0; i < 2; i++)
        {
            var key = i == 0 ? k1 : k2;
            var preamble = ctx.Render($"{RedisCommand.HIMPORT}{Tokens.Prepare}{fieldSet}{Tokens.NameAndAge}");
            var request = ctx.Render($"{RedisCommand.HIMPORT}{Tokens.Set}{(RedisKey)key}{fieldSet}{(RedisValue)("user" + i)}{(RedisValue)(30 + i)}");
            try
            {
                // Boolean rather than Result: +OK is the reply, and a typed handler is what a real command
                // group would use - the probe should not take an easier route than the thing it stands in for
                Assert.True(await ctx.SendWithPreambleAsync(
                    ref preamble, ref request, CommandFlags.None, RespHandlers.Boolean, gate));
            }
            finally
            {
                preamble.Dispose();
                request.Dispose();
            }
        }

        // the point: the preamble went once, not once per command, and the second SET still worked - so the
        // gate's belief and the server's session state agree
        Assert.Equal(1, gate.Injections);

        // and the confirm half of the contract really does run - this is the assertion an earlier draft of
        // this probe got wrong, by predicting OnEstablished was never called and "proving" it with a gate
        // that threw. It passed, which means the throw went somewhere unseen rather than that the call
        // never happened. Counting is the honest instrument; throwing on a background reply path is not.
        Assert.Equal(1, gate.Established);
        Assert.Equal("user0", await db.HashGetAsync(k1, "name"));
        Assert.Equal(30, (int)await db.HashGetAsync(k1, "age"));
        Assert.Equal("user1", await db.HashGetAsync(k2, "name"));
        Assert.Equal(31, (int)await db.HashGetAsync(k2, "age"));
    }

    /// <summary>
    /// Where the two seams actually differ: <b>when</b> the belief is recorded. A script's gate confirms on
    /// the reply, because the effect is the server's and only the reply proves it. A field-set cannot
    /// afford that - every import issued before the first <c>PREPARE</c>'s reply lands still reads "not
    /// prepared", so a burst injects one preamble per command.
    /// </summary>
    /// <remarks>
    /// Both strategies are correct - <c>PREPARE</c> is idempotent, and every import below succeeds either
    /// way - so this is a cost difference, not a correctness one, and it is invisible without counting.
    /// Measured: <b>8 preambles for 8 commands</b> confirming on the reply, <b>1</b> claiming on the write.
    /// The first attempt at this test issued the sends from one loop and saw 1 either way, because each
    /// reply landed while the next frame was still being rendered - so it was measuring the loop, not the
    /// burst. The barrier is what makes the sends contend.
    /// </remarks>
    [Fact]
    public async Task ConfirmOnReplyInjectsPerCommandUnderABurst()
    {
        await using var conn = await RequireHashImportAsync();
        var db = conn.GetDatabase();
        var ctx = NewContext(conn, db.Database);
        var prefix = Me();
        const int Burst = 8;

        var counts = new int[2];
        for (var mode = 0; mode < 2; mode++)
        {
            var claimOnWrite = mode == 1;
            var fieldSet = (RedisValue)($"{prefix}:fs{mode}");
            var gate = new FieldSetGate(claimOnWrite);
            var pending = new Task<bool>[Burst];

            // a real burst needs the sends to contend, not to take turns: issuing them from one loop lets
            // each reply land while the next frame is still being rendered, which is not the scenario
            using var gun = new Barrier(Burst);
            for (var i = 0; i < Burst; i++)
            {
                var index = i;
                pending[i] = Task.Run(() =>
                {
                    var preamble = ctx.Render($"{RedisCommand.HIMPORT}{Tokens.Prepare}{fieldSet}{Tokens.NameAndAge}");
                    var request = ctx.Render($"{RedisCommand.HIMPORT}{Tokens.Set}{(RedisKey)($"{prefix}:{mode}:{index}")}{fieldSet}{(RedisValue)("user" + index)}{(RedisValue)index}");
                    try
                    {
                        gun.SignalAndWait();
                        return ctx.SendWithPreambleAsync(
                            ref preamble, ref request, CommandFlags.None, RespHandlers.Boolean, gate).AsTask();
                    }
                    finally
                    {
                        preamble.Dispose();
                        request.Dispose();
                    }
                });
            }

            Assert.All(await Task.WhenAll(pending), Assert.True);
            counts[mode] = gate.Injections;
            Log($"{(claimOnWrite ? "claim-on-write" : "confirm-on-reply")}: {gate.Injections} preamble(s) for {Burst} commands");
        }

        // claiming inside the write lock is what collapses the burst, because that lock is the only place
        // where "has this connection prepared it?" and "write it" are one decision
        Assert.Equal(1, counts[1]);
        Assert.True(counts[0] > counts[1], $"confirm-on-reply injected {counts[0]}, expected more than {counts[1]}");
    }
}
