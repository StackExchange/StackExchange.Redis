using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The deferred-execution members on <see cref="IDatabase"/>, which SER352 cannot see.
/// </summary>
/// <remarks>
/// <para>
/// <c>[AutoDatabase]</c> skips every <c>IEnumerable&lt;T&gt;</c>/<c>IAsyncEnumerable&lt;T&gt;</c> member -
/// deferred execution does not fit capture-and-replay - so these are hand-written, and the generator has
/// no way to tell a real implementation from a forwarding throw. To it they are simply "declared", so the
/// SER352 count omits them entirely. This file is the tripwire in the one place that can see them.
/// </para>
/// <para>
/// It earned that description immediately: wiring the scans to the context surface made the previous
/// version of this test fail, which is exactly what a tripwire on an uncounted gap is for.
/// </para>
/// </remarks>
public class TransitionalScanGapTests
{
    private static IDatabase Target(params string[] replies)
        => new TransitionalDatabase(
            new RespDatabaseContext(new RespContext().WithExecutor(new FakeExecutor(replies))),
            null!,
            null);

    private static string Page(long cursor, params string[] items)
    {
        var sb = new StringBuilder($"*2\r\n:{cursor}\r\n*{items.Length}\r\n");
        foreach (var item in items) sb.Append($"${item.Length}\r\n{item}\r\n");
        return sb.ToString();
    }

    private static IEnumerable<MethodInfo> ScanMembers() =>
        typeof(TransitionalDatabase)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType.IsGenericType)
            .Where(m => m.ReturnType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                     || m.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));

    /// <summary>The size of the uncounted family, so implementing one forces this number to move.</summary>
    [Fact]
    public void TheScanFamilyIsThisBig() => Assert.Equal(13, ScanMembers().Count());

    /// <summary>Nothing in the uncounted family forwards to the fallback any more.</summary>
    /// <remarks>
    /// The whole reason this file exists, now asserted positively rather than by listing what still
    /// throws: every member here is invoked with its defaults against a fake executor, and reaching a
    /// <see cref="NotImplementedException"/> means it took the fallback - which has no database to
    /// forward to - rather than the context surface. The vector-set enumerates were the last two.
    /// </remarks>
    [Fact]
    public void NothingInTheScanFamilyStillForwards()
    {
        var unmoved = new List<string>();
        foreach (var method in ScanMembers())
        {
            var args = method.GetParameters()
                .Select(p => p.HasDefaultValue ? p.DefaultValue : Activator.CreateInstance(p.ParameterType))
                .ToArray();

            try
            {
                // the sequences are lazy, so this only proves the CALL does not throw - which is exactly
                // what a forwarding member does, before anything is enumerated
                method.Invoke(Target(Page(0)), args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is NotImplementedException)
            {
                unmoved.Add(method.Name);
            }
        }

        Assert.Empty(unmoved);
    }

    /// <summary>
    /// The vector-set walk is keyset paging, and pages by the last member it saw.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a cursor scan - the shipped code avoids "scan" naming <i>"in case a VSCAN command is added
    /// later"</i> - so there is no cursor in the reply to follow. The next page is asked for from the last
    /// member with the start excluded, which is why the second request carries <c>(b</c> rather than
    /// <c>[b</c>: an inclusive re-ask would yield <c>b</c> twice.
    /// </para>
    /// <para>
    /// A short page ends the walk without a further round trip, which is what the third reply being
    /// unnecessary demonstrates.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheVectorSetWalkPagesByTheLastMember()
    {
        var executor = new FakeExecutor(Flat("a", "b"), Flat("c"));
        IDatabase db = new TransitionalDatabase(
            new RespDatabaseContext(new RespContext().WithExecutor(executor)), null!, null);

        Assert.Equal(
            ["a", "b", "c"],
            db.VectorSetRangeEnumerate("k", count: 2).Select(v => v.ToString()));

        Assert.Equal(
            [
                "*5|$6|VRANGE|$1|k|$1|-|$1|+|$1|2|",   // the first page: both bounds open
                "*5|$6|VRANGE|$1|k|$2|(b|$1|+|$1|2|", // resumed from the last member, exclusively
            ],
            executor.Sent);
    }

    /// <summary>Reaching the requested end stops without asking for a page that could only be empty.</summary>
    [Fact]
    public void TheVectorSetWalkStopsAtTheEndBound()
    {
        var executor = new FakeExecutor(Flat("a", "b"));
        IDatabase db = new TransitionalDatabase(
            new RespDatabaseContext(new RespContext().WithExecutor(executor)), null!, null);

        Assert.Equal(["a", "b"], db.VectorSetRangeEnumerate("k", end: "b", count: 2).Select(v => v.ToString()));
        Assert.Single(executor.Sent); // a full page, but it ended ON the bound
    }

    /// <summary>The asynchronous face walks the same way.</summary>
    [Fact]
    public async Task TheVectorSetWalkIsAlsoAsynchronous()
    {
        var executor = new FakeExecutor(Flat("a", "b"), Flat("c"));
        IDatabase db = new TransitionalDatabase(
            new RespDatabaseContext(new RespContext().WithExecutor(executor)), null!, null);

        var seen = new List<string>();
        await foreach (var item in db.VectorSetRangeEnumerateAsync("k", count: 2)) seen.Add(item.ToString());

        Assert.Equal(["a", "b", "c"], seen);
        Assert.Equal(2, executor.Sent.Count);
    }

    /// <summary>A flat array reply, which is what <c>VRANGE</c> answers.</summary>
    private static string Flat(params string[] items)
    {
        var sb = new StringBuilder($"*{items.Length}\r\n");
        foreach (var item in items) sb.Append($"${item.Length}\r\n{item}\r\n");
        return sb.ToString();
    }

    /// <summary>And the four that have moved really do read through the context surface.</summary>
    /// <remarks>
    /// Driven through <see cref="IDatabase"/> rather than the group, because the point is the transitional
    /// adapter: the synchronous signatures block on each page using this type's own <c>Wait</c>, and
    /// nothing on the new surface blocks.
    /// </remarks>
    [Fact]
    public void TheSynchronousScansWalkTheCursor()
    {
        Assert.Equal(
            ["a", "b", "c"],
            Target(Page(7, "a", "b"), Page(0, "c")).SetScan("k").Select(v => v.ToString()));

        Assert.Equal(
            ["f1=v1", "f2=v2"],
            Target(Page(4, "f1", "v1"), Page(0, "f2", "v2")).HashScan("k").Select(e => $"{e.Name}={e.Value}"));

        Assert.Equal(
            ["f1", "f2"],
            Target(Page(4, "f1"), Page(0, "f2")).HashScanNoValues("k").Select(v => v.ToString()));

        Assert.Equal(
            ["m1:1.5", "m2:2"],
            Target(Page(3, "m1", "1.5"), Page(0, "m2", "2")).SortedSetScan("k").Select(e => $"{e.Element}:{e.Score}"));
    }

    [Fact]
    public async Task TheAsynchronousScansWalkTheCursor()
    {
        var seen = new List<string>();
        await foreach (var item in Target(Page(7, "a"), Page(0, "b")).SetScanAsync("k")) seen.Add(item.ToString());
        Assert.Equal(["a", "b"], seen);

        var fields = new List<string>();
        await foreach (var entry in Target(Page(4, "f1", "v1"), Page(0, "f2", "v2")).HashScanAsync("k"))
        {
            fields.Add($"{entry.Name}={entry.Value}");
        }

        Assert.Equal(["f1=v1", "f2=v2"], fields);
    }
}
