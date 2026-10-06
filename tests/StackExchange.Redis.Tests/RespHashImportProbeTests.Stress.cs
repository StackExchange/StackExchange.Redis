using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

public partial class RespHashImportProbeTests
{
    /// <summary>
    /// Preamble pairs in a burst, sharing one connection with ordinary traffic: every ordinary reply must still be
    /// its own, and nothing may stall.
    /// </summary>
    /// <remarks>
    /// <b>A reproduction tool for an open defect, so it is long-running only.</b> It fails roughly 1 run in 30
    /// (2 of 69 locally), with a GET timed out at "0ms elapsed" - an operation whose age was never stamped,
    /// failed by something other than the heartbeat sweep (which needs an age of the full timeout) - and once
    /// with PREPARE at the head of the line and nothing inbound, the CI stall's signature. See the v4 ledger.
    /// </remarks>
    [Fact]
    public async Task PreamblePairsUnderLoadLeaveTheConnectionUsable()
    {
        Skip.UnlessLongRunning();
        await using var conn = await RequireHashImportAsync();
        var db = conn.GetDatabase();
        var ctx = NewContext(conn, db.Database);
        var prefix = Me();
        var watch = Stopwatch.StartNew();
        var workers = new List<Task>();

        for (var w = 0; w < 8; w++)
        {
            var worker = w;
            workers.Add(Task.Run(async () =>
            {
                var gate = new FieldSetGate(claimOnWrite: worker % 2 == 0);
                var fieldSet = (RedisValue)$"{prefix}:fs{worker}";
                for (var i = 0; watch.ElapsedMilliseconds < 3000; i++)
                {
                    var preamble = ctx.Raw.Render($"{RedisCommand.HIMPORT}{Tokens.Prepare}{fieldSet}{Tokens.NameAndAge}");
                    var request = ctx.Raw.Render($"{RedisCommand.HIMPORT}{Tokens.Set}{(RedisKey)$"{prefix}:{worker}:{i % 16}"}{fieldSet}{(RedisValue)("u" + i)}{(RedisValue)i}");
                    ValueTask<bool> sent;
                    try
                    {
                        sent = ctx.Raw.SendWithPreambleAsync(ref preamble, ref request, CommandFlags.None, RespHandlers.Boolean, gate);
                    }
                    finally
                    {
                        preamble.Dispose();
                        request.Dispose();
                    }
                    Assert.True(await sent);
                }
            }));
        }

        for (var w = 0; w < 8; w++)
        {
            var worker = w;
            workers.Add(Task.Run(async () =>
            {
                for (var i = 0; watch.ElapsedMilliseconds < 3000; i++)
                {
                    RedisKey key = $"{prefix}:plain:{worker}";
                    var value = $"{worker}:{i}";
                    await db.StringSetAsync(key, value);
                    Assert.Equal(value, (string?)await db.StringGetAsync(key));
                }
            }));
        }

        await Task.WhenAll(workers);
    }
}
