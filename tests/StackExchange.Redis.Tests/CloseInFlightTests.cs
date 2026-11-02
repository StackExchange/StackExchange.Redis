using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class CloseInFlightTests(ITestOutputHelper output) : TestBase(output)
{
    // #3329: CloseAsync(allowCommandsToComplete: false) with commands in flight races the read loop, still
    // matching replies already in the buffer, against the failure path draining the sent-awaiting-reply
    // queue one message at a time. Each reply the read loop matched after the drain had taken a message was
    // handed to the message *after* the one it belonged to: GET key N completed, successfully, with key N-1's
    // value. The only acceptable outcomes are the right value or a connection exception.
    [Fact]
    public async Task CloseWithCommandsInFlight_NeverCompletesACallWithAnotherCallsReply()
    {
        const int Rounds = 20, PerRound = 500;
        SetExpectedAmbientFailureCount(-1); // every round tears a connection down with work in flight
        await using var admin = Create();
        var adminDb = admin.GetDatabase();
        var prefix = Me();
        long ok = 0, wrong = 0, failed = 0;

        for (int round = 0; round < Rounds; round++)
        {
            var keys = Enumerable.Range(0, PerRound).Select(i => (RedisKey)$"{prefix}:{round}:{i}").ToArray();
            await Task.WhenAll(keys.Select(k => adminDb.StringSetAsync(k, k.ToString(), TimeSpan.FromMinutes(1))));

            var conn = Create(shared: false);
            var db = conn.GetDatabase();
            var calls = keys.Select(k => db.StringGetAsync(k)).ToArray();
            await conn.CloseAsync(allowCommandsToComplete: false);

            for (int i = 0; i < calls.Length; i++)
            {
                string? value;
                try
                {
                    value = await calls[i];
                }
                catch (Exception)
                {
                    // v2 lets the read loop's own exception reach the caller when the pipe is torn down under it
                    // (InvalidOperationException from the completed PipeReader, ArgumentOutOfRangeException from
                    // a slice) - v3 wraps those as RedisConnectionException (#3283). Any failure is fine here;
                    // the bug under test is a *success* carrying someone else's value.
                    failed++;
                    continue;
                }

                if (value == keys[i].ToString())
                {
                    ok++;
                }
                else if (wrong++ < 5)
                {
                    Log($"round {round}: GET {keys[i]} returned '{value}'");
                }
            }

            await conn.DisposeAsync();
            await adminDb.KeyDeleteAsync(keys);
        }

        Log($"ok={ok} wrong={wrong} failed={failed}");
        Assert.Equal(0, wrong);
    }
}
