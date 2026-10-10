using System.Net;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Reading a <c>-MOVED</c>/<c>-ASK</c> reply. A redirect is an instruction, not a failure, so it has to
/// be recognised before an error reply becomes an exception.
/// </summary>
public class RespRedirectTests
{
    private static bool TryParse(string frame, out RespRedirect redirect)
        => RespRedirect.TryParse(Encoding.UTF8.GetBytes(frame), out redirect);

    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData(":1\r\n")]
    [InlineData("$5\r\nhello\r\n")]
    [InlineData("-WRONGTYPE Operation against a key holding the wrong kind of value\r\n")]
    [InlineData("-ERR unknown command\r\n")]
    [InlineData("-NOAUTH Authentication required.\r\n")]
    public void OrdinaryRepliesAreNotRedirects(string frame)
    {
        Assert.False(TryParse(frame, out _));
    }

    [Fact]
    public void AMovedNamesTheSlotAndTheEndpoint()
    {
        Assert.True(TryParse("-MOVED 3999 127.0.0.1:6381\r\n", out var redirect));

        Assert.True(redirect.IsMoved);
        Assert.Equal(3999, redirect.Slot);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6381), redirect.Endpoint);
        Assert.False(redirect.IsUnroutable);
    }

    [Fact]
    public void AnAskIsNotAMoved()
    {
        // the difference is what it says about the FUTURE: ASK is this one command, mid-migration, and
        // the slot map must NOT be updated from it
        Assert.True(TryParse("-ASK 3999 127.0.0.1:6381\r\n", out var redirect));

        Assert.False(redirect.IsMoved);
        Assert.Equal(3999, redirect.Slot);
    }

    [Fact]
    public void AHostnameTargetIsAccepted()
    {
        Assert.True(TryParse("-MOVED 42 node-3.example.com:7002\r\n", out var redirect));

        Assert.Equal(new DnsEndPoint("node-3.example.com", 7002), redirect.Endpoint);
        Assert.False(redirect.IsUnroutable);
    }

    [Theory]
    [InlineData("-MOVED 42 ?:6379\r\n")]
    [InlineData("-MOVED 42 127.0.0.1:0\r\n")]
    [InlineData("-ASK 42 ?:0\r\n")]
    public void ATargetThatCannotBeDialledIsRecognisedRatherThanInvented(string frame)
    {
        // "?" is the documented placeholder for an unknown node, and it must NOT be read as "the node
        // that answered" - that would leave us dialling a host called "?". It is a redirect we understood
        // and cannot follow, which calls for a topology refresh rather than a connection.
        Assert.True(TryParse(frame, out var redirect));

        Assert.True(redirect.IsUnroutable);
        Assert.Null(redirect.Endpoint);
        Assert.Equal(42, redirect.Slot);
    }

    [Theory]
    [InlineData("-MOVED\r\n")]
    [InlineData("-MOVED 3999\r\n")]
    [InlineData("-MOVED notanumber 127.0.0.1:6381\r\n")]
    [InlineData("-MOVED 99999 127.0.0.1:6381\r\n")]
    [InlineData("-MOVEDX 1 127.0.0.1:6381\r\n")]
    [InlineData("-ASKING 1 127.0.0.1:6381\r\n")]
    public void MalformedOrLookalikeRepliesAreDeclined(string frame)
    {
        // declining is the safe direction: an unrecognised error stays an error, where a mis-parsed
        // redirect would send the command somewhere arbitrary
        Assert.False(TryParse(frame, out _));
    }

    [Fact]
    public void SlotsAtTheEdgesOfTheRangeAreAccepted()
    {
        Assert.True(TryParse("-MOVED 0 127.0.0.1:6379\r\n", out var first));
        Assert.Equal(0, first.Slot);

        Assert.True(TryParse("-MOVED 16383 127.0.0.1:6379\r\n", out var last));
        Assert.Equal(16383, last.Slot);

        // 16384 is one past the end, and a slot outside the range is not a slot
        Assert.False(TryParse("-MOVED 16384 127.0.0.1:6379\r\n", out _));
    }
}
