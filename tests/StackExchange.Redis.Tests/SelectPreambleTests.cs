using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The memoised <c>SELECT n</c> renderings that a multi-database connection writes as a preamble.
/// </summary>
/// <remarks>
/// Worth its own tests because the sharing is what makes it cheap, and sharing a rendering is only safe
/// under a condition that is invisible at the call site: the buffer must not be poolable. A frame that
/// owned a pooled array would be handed back by the first sender to release it, while a second was still
/// writing the same bytes - the class of bug whose symptom is somebody else's reply arriving for your
/// command.
/// </remarks>
public class SelectPreambleTests
{
    private static SelectPreamble New() => new(new RespContext());

    [Fact]
    public void RendersTheCommand()
    {
        var select = New();
        Assert.Equal("*2|$6|SELECT|$1|3|", select.For(3).ToString());
        Assert.Equal("*2|$6|SELECT|$2|11|", select.For(11).ToString());
    }

    /// <summary>The same index hands back the same rendering rather than building another.</summary>
    [Fact]
    public void TheSameDatabaseIsRenderedOnce()
    {
        var select = New();

        // equality is over the bytes, so it cannot tell a reused rendering from an identical new one;
        // the buffer identity can, and that is the property being asserted
        Assert.True(select.For(4).Span == select.For(4).Span);
    }

    /// <summary>
    /// It owns nothing poolable, which is the precondition for handing one rendering to several concurrent
    /// senders.
    /// </summary>
    [Fact]
    public void TheRenderingOwnsNoPooledBuffer()
    {
        var request = New().For(2);
        Assert.False(request.IsOwned);

        // and releasing it is therefore a no-op rather than a hand-back: still readable afterwards
        request.Dispose();
        Assert.Equal("*2|$6|SELECT|$1|2|", request.ToString());
    }

    /// <summary>
    /// Outside the memoised range it still renders correctly, just without remembering - so a hostile or
    /// merely unusual index cannot grow an unbounded table.
    /// </summary>
    [Fact]
    public void AnIndexBeyondTheTableStillRenders()
    {
        var select = New();
        Assert.Equal("*2|$6|SELECT|$3|400|", select.For(400).ToString());
        Assert.False(select.For(400).Span == select.For(400).Span); // rendered fresh each time
    }

    /// <summary>A frame rendered through a context stamps that context's database.</summary>
    [Fact]
    public void ARenderedFrameCarriesTheContextDatabase()
    {
        var context = new RespContext(database: 3);
        var frame = context.Render($"{RedisCommand.GET}{(RedisKey)"k"}");
        try
        {
            Assert.Equal(3, frame.Database);
            Assert.Equal(3, frame.Detach().Database);
        }
        finally
        {
            frame.Dispose();
        }
    }

    /// <summary>The database travels on the request, because the bytes alone cannot say which one it is.</summary>
    [Fact]
    public void TheRequestCarriesItsDatabase()
    {
        Assert.Equal(7, New().For(7).Database);
        Assert.Equal(RedisCommand.SELECT, New().For(7).Command);
    }
}
