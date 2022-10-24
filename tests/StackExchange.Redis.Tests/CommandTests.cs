using System.Net;
using Xunit;

namespace StackExchange.Redis.Tests;

public class CommandTests
{
    [Fact]
    public void CommandByteLength()
    {
        Assert.Equal(31, CommandBytes.MaxLength);
    }

    [Fact]
    public void CheckCommandContents()
    {
        for (int len = 0; len <= CommandBytes.MaxLength; len++)
        {
            var s = new string('A', len);
            CommandBytes b = s;
            Assert.Equal(len, b.Length);

            var t = b.ToString();
            Assert.Equal(s, t);

            CommandBytes b2 = t;
            Assert.Equal(b, b2);

            Assert.Equal(len == 0, ReferenceEquals(s, t));
        }
    }

    [Fact]
    public void Basic()
    {
        var config = ConfigurationOptions.Parse(".,$PING=p");
        Assert.Single(config.EndPoints);
        config.SetDefaultPorts();
        Assert.Contains(new DnsEndPoint(".", 6379), config.EndPoints);
        var map = config.CommandMap;
        Assert.Equal("$PING=P", map.ToString());
        Assert.Equal(".:6379,$PING=P", config.ToString());
    }

    [Theory]
    [InlineData("redisql.CREATE_STATEMENT")]
    [InlineData("INSERTINTOTABLE1STMT")]
    public void CanHandleNonTrivialCommands(string command)
    {
        var cmd = new CommandBytes(command);
        Assert.Equal(command.Length, cmd.Length);
        Assert.Equal(command.ToUpperInvariant(), cmd.ToString());

        Assert.Equal(31, CommandBytes.MaxLength);
    }
}
