using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Commands that look cacheable and are not.
/// </summary>
/// <remarks>
/// <para>
/// Client-side caching is opt-<b>out</b>: declaring a read-only retry category and naming a key is enough
/// to be cached. That is right for the overwhelming majority and wrong for a handful, and the handful is
/// the dangerous part - a command cached that should not have been serves a wrong answer forever, with no
/// error and no log. These are the ones that have to say so.
/// </para>
/// <para>
/// Asserted through the <b>cache</b> rather than by reading flags back, because the flag is a means: what
/// matters is that nothing is stored and nothing is served, whatever mechanism gets us there.
/// </para>
/// </remarks>
public class RespCacheExclusionTests
{
    private sealed class FakeExecutor(string reply) : IRespExecutor
    {
        public int Sent { get; private set; }

        public int Database => 0;

        public RespPayload Send(in RespRequest request)
        {
            Sent++;
            return RespPayload.Create(Encoding.UTF8.GetBytes(reply));
        }

        public ValueTask<RespPayload> SendAsync(RespRequest request, CancellationToken cancellationToken = default)
            => new(Send(request));
    }

    /// <summary>Run the same command twice and report whether the second one was served locally.</summary>
    private static async Task<(bool Cached, long Refused)> RunTwice(
        string reply,
        Func<RespDatabaseContext, ValueTask> command)
    {
        using var cache = new RespClientCache();
        var executor = new FakeExecutor(reply);
        var context = new RespDatabaseContext(new RespContext().WithExecutor(executor).WithCache(cache));

        await command(context);
        await command(context);

        return (executor.Sent == 1, cache.RefusedByFlags);
    }

    /// <summary>
    /// The non-deterministic readers are never cached.
    /// </summary>
    /// <remarks>
    /// These are asked precisely <i>because</i> the answer should differ each time, so caching would defeat
    /// the command rather than accelerate it - and nothing would ever invalidate it, because nothing
    /// changed. They sit in the same retry category as GET, which is why they have to opt out explicitly.
    /// </remarks>
    [Fact]
    public async Task RandomMemberCommandsAreNeverCached()
    {
        var cases = new (string Name, string Reply, Func<RespDatabaseContext, ValueTask> Run)[]
        {
            ("SRANDMEMBER", "$1\r\na\r\n", static c => Discard(c.Sets.RandomMemberAsync("k"))),
            ("SRANDMEMBER count", "*1\r\n$1\r\na\r\n", static c => DiscardLease(c.Sets.RandomMembersAsync("k", 2))),
            ("HRANDFIELD", "$1\r\na\r\n", static c => Discard(c.Hashes.RandomFieldAsync("k"))),
            ("HRANDFIELD count", "*1\r\n$1\r\na\r\n", static c => DiscardLease(c.Hashes.RandomFieldsAsync("k", 2))),
            ("HRANDFIELD WITHVALUES", "*2\r\n$1\r\na\r\n$1\r\nb\r\n", static c => DiscardLease(c.Hashes.RandomFieldsWithValuesAsync("k", 2))),
            ("ZRANDMEMBER", "$1\r\na\r\n", static c => Discard(c.SortedSets.RandomMemberAsync("k"))),
            ("ZRANDMEMBER count", "*1\r\n$1\r\na\r\n", static c => DiscardLease(c.SortedSets.RandomMembersAsync("k", 2))),
            ("ZRANDMEMBER WITHSCORES", "*2\r\n$1\r\na\r\n$1\r\n1\r\n", static c => DiscardLease(c.SortedSets.RandomMembersWithScoresAsync("k", 2))),
        };

        foreach (var (name, reply, run) in cases)
        {
            var (cached, refused) = await RunTwice(reply, run);
            Assert.False(cached, $"{name} was served from cache");
            Assert.True(refused > 0, $"{name} was not refused by flags");
        }
    }

    /// <summary>
    /// A reply that counts down is never cached; one that names a fixed instant may be.
    /// </summary>
    /// <remarks>
    /// <c>HPTTL</c> is different a millisecond later, so it is stale the moment it is stored - and no
    /// correction is coming, because the server announces expiry to nobody (measured; design notes 6.13).
    /// <c>HPEXPIRETIME</c> returns an instant, which does not drift; it only becomes wrong once the field
    /// actually expires, which is the exposure every cached read of a volatile key already has and which
    /// the entry lifetime exists to bound.
    /// </remarks>
    [Fact]
    public async Task RelativeExpiryIsNotCachedButAbsoluteIs()
    {
        var (ttlCached, ttlRefused) = await RunTwice(
            "*1\r\n:1000\r\n",
            static c => DiscardLease(c.Hashes.GetTimeToLiveAsync("k", ["f"])));
        Assert.False(ttlCached, "HPTTL was served from cache");
        Assert.True(ttlRefused > 0);

        var (whenCached, _) = await RunTwice(
            "*1\r\n:1700000000000\r\n",
            static c => DiscardLease(c.Hashes.GetExpireDateTimeAsync("k", ["f"])));
        Assert.True(whenCached, "HPEXPIRETIME should be cacheable: an instant does not drift");
    }

    /// <summary>An ordinary read of the same shape still caches - so the tests above prove a rule, not a bug.</summary>
    /// <remarks>
    /// Without this the assertions above would pass just as well if caching were broken outright, which is
    /// the failure mode a suite of "is not cached" tests invites.
    /// </remarks>
    [Fact]
    public async Task AnOrdinaryReadOfTheSameShapeIsStillCached()
    {
        var (cached, refused) = await RunTwice(
            "*1\r\n$1\r\na\r\n",
            static c => DiscardLease(c.Sets.MembersAsync("k")));

        Assert.True(cached, "SMEMBERS should be cached");
        Assert.Equal(0, refused);
    }

    private static async ValueTask Discard<T>(ValueTask<T> pending) => await pending;

    private static async ValueTask DiscardLease<T>(ValueTask<ReadOnlyLease<T>> pending) => (await pending).Dispose();
}
