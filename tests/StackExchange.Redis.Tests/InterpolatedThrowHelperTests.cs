using System;
using System.Buffers;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// A guard that throws mid-build must hand the pooled buffer back <b>itself</b>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing else will. The handler is a <c>ref struct</c> living in the caller's frame, and the compiler
/// generates no <c>try</c>/<c>finally</c> around an interpolated string - so an exception out of
/// <c>AppendFormatted</c> abandons the rented array unless the throw path returns it. That is why every
/// throw helper on the builder is an instance method that calls <c>Dispose</c> first, rather than the
/// house <c>static void Throw()</c> local function, which could not reach it.
/// </para>
/// <para>
/// The tests below therefore <b>do not</b> dispose the builder after catching: disposing would hide the
/// bug they exist to catch.
/// </para>
/// </remarks>
public class InterpolatedThrowHelperTests
{
    /// <summary>
    /// Big enough that the pool bucket is unambiguous.
    /// </summary>
    /// <remarks>
    /// The builder rents <c>HeaderMax + 64 + literalLength + (formattedCount * 24)</c>, and those constants
    /// are private. At this size the overhead is a rounding error inside a bucket that spans 64KiB, so the
    /// probe and the builder land in the same one without the test needing to know the arithmetic.
    /// </remarks>
    private const int BigEnough = 100_000;

    /// <summary>
    /// Run <paramref name="build"/>, expecting it to throw, and report whether the array came back.
    /// </summary>
    /// <remarks>
    /// <c>ArrayPool&lt;byte&gt;.Shared</c> keeps a per-thread slot per bucket, so on one thread the array
    /// most recently returned is the next one handed out. Priming the slot therefore makes the builder's
    /// rent take a known instance, and asking for it again afterwards says whether it was given back.
    /// </remarks>
    private static bool BufferWasReturned<T>(Action build) where T : Exception
    {
        var primed = ArrayPool<byte>.Shared.Rent(BigEnough);
        ArrayPool<byte>.Shared.Return(primed);

        Assert.Throws<T>(build);

        var next = ArrayPool<byte>.Shared.Rent(BigEnough);
        ArrayPool<byte>.Shared.Return(next);
        return ReferenceEquals(primed, next);
    }

    /// <summary>A null reference of a type the single-value generic overload accepts.</summary>
    private static readonly NullArgument NeverWritten = null!;

    private sealed class NullArgument : IRespArgument
    {
        public void WriteTo(scoped ref RespRequestBuilder handler) => throw new NotSupportedException();
    }

    private static RespRequestBuilder Fresh() => new(BigEnough, 0, new RespContext());

    /// <summary>The case that is not hypothetical: a value hole before any command.</summary>
    /// <remarks>
    /// The command-as-a-hole constructor rents and <i>then</i> records that no command has been written, so
    /// this guard is reached with a live array every single time it fires.
    /// </remarks>
    [Fact]
    public void AnArgumentBeforeTheCommandReturnsTheBuffer()
    {
        Assert.True(BufferWasReturned<InvalidOperationException>(() =>
        {
            var b = Fresh();
            b.AppendFormatted((RedisValue)"v"); // no command yet
            b.Complete().Dispose();
        }));
    }

    [Fact]
    public void ASecondCommandReturnsTheBuffer()
    {
        Assert.True(BufferWasReturned<InvalidOperationException>(() =>
        {
            var b = Fresh();
            b.AppendFormatted(RedisCommand.GET);
            b.AppendFormatted(RedisCommand.SET); // only the first argument may be the command
            b.Complete().Dispose();
        }));
    }

    [Fact]
    public void AnEmptyCommandReturnsTheBuffer()
    {
        Assert.True(BufferWasReturned<ArgumentException>(() =>
        {
            var b = Fresh();
            b.AppendFormatted(default(RespCommand));
            b.Complete().Dispose();
        }));
    }

    [Fact]
    public void ANullArgumentReturnsTheBuffer()
    {
        Assert.True(BufferWasReturned<ArgumentNullException>(() =>
        {
            var b = Fresh();
            b.AppendFormatted(RedisCommand.GET);
            b.AppendFormatted(NeverWritten); // a typed null: `null` alone would also fit the span overload
            b.Complete().Dispose();
        }));
    }

    /// <summary>And <c>Complete</c> itself, which had this behaviour before the others did.</summary>
    [Fact]
    public void CompletingWithNoCommandReturnsTheBuffer()
    {
        Assert.True(BufferWasReturned<InvalidOperationException>(() =>
        {
            var b = Fresh();
            b.Complete().Dispose();
        }));
    }

    /// <summary>
    /// The control: the same harness reports <see langword="false"/> when nothing hands the array back.
    /// </summary>
    /// <remarks>
    /// Without this, every assertion above would pass just as well if <c>BufferWasReturned</c> were
    /// hard-wired to <see langword="true"/> - the pool gives no direct way to ask, so the measurement
    /// itself needs a negative case.
    /// </remarks>
    [Fact]
    public void TheHarnessCanTellTheDifference()
    {
        Assert.False(BufferWasReturned<InvalidOperationException>(() =>
        {
            var leaked = ArrayPool<byte>.Shared.Rent(BigEnough);
            GC.KeepAlive(leaked);
            throw new InvalidOperationException("never returned");
        }));
    }
}
