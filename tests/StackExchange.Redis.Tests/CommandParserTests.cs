/*
#if NET8_0_OR_GREATER
using StackExchange.Redis;
using Xunit;

namespace StackExchange.Redis.Tests;

public class CommandParserTests
{
    [Theory]
    [InlineData(@"ping", "ping")]
    [InlineData(@"""ping", "ping")]
    [InlineData(@"ping pong", "ping", "pong")]
    [InlineData("ping pong \tpang", "ping", "pong", "pang")]
    [InlineData(@"ping ""pong pang""", "ping", "pong pang")]
    public static void Parse(string input, string command, params object[] args)
    {
        var actualCommand = CommandParser.Parse(input, out var actualArgs);
        Assert.Equal(command, actualCommand);
        Assert.Equal(args, actualArgs);
    }
}

#endif
*/
