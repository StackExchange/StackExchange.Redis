using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

public class FormatTests(ITestOutputHelper output) : TestBase(output)
{
    // System.Version is awkward: unspecified parts are -1, so "7.4" is (7, 4, -1, -1) and "7.4.0" is (7, 4, 0, -1),
    // and the inbuilt operators order them differently; the UTF-8 overload (what the HELLO handler uses) must
    // produce exactly the same raw parts as the char/string overloads, and the version gates must then treat the
    // unspecified parts as zero (see VersionExtensions.IsAtLeast).
    [Theory]
    [InlineData("7", 7, 0, -1, -1)] // major-only is our extension; System.Version rejects it
    [InlineData("7.4", 7, 4, -1, -1)]
    [InlineData("7.4.0", 7, 4, 0, -1)]
    [InlineData("7.4.3", 7, 4, 3, -1)]
    [InlineData("7.4.3.0", 7, 4, 3, 0)]
    [InlineData("9.1.2", 9, 1, 2, -1)]
    [InlineData("7.4.3.1", 7, 4, 3, 1)]
    [InlineData("255.255.65535.65535", 255, 255, 65535, 65535)]
    [InlineData("0.0", 0, 0, -1, -1)]
    [InlineData("007.040", 7, 40, -1, -1)]
    [InlineData("", null, null, null, null)]
    [InlineData(".", null, null, null, null)]
    [InlineData("7.", null, null, null, null)]
    [InlineData(".7", null, null, null, null)]
    [InlineData("7..4", null, null, null, null)]
    [InlineData("1.2.3.4.5", null, null, null, null)]
    [InlineData("9.1.2-rc1", null, null, null, null)]
    [InlineData("-1.2", null, null, null, null)]
    [InlineData("a.b", null, null, null, null)]
    [InlineData("1.2.3-preview4", null, null, null, null)]
    public void TryParseVersionUtf8MatchesChars(string input, int? major, int? minor, int? build, int? revision)
    {
        bool success = Format.TryParseVersion(Encoding.UTF8.GetBytes(input), out var fromBytes);
        Assert.Equal(major is not null, success);
        Assert.Equal(success, Format.TryParseVersion(input.AsSpan(), out var fromChars));
        Assert.Equal(success, Format.TryParseVersion(input, out var fromString));

        if (success)
        {
            Assert.NotNull(fromBytes);
            Assert.Equal((major, minor, build, revision), (fromBytes.Major, fromBytes.Minor, fromBytes.Build, fromBytes.Revision));
            Assert.Equal(fromBytes, fromChars); // Version.Equals compares all four raw parts, -1 included
            Assert.Equal(fromBytes, fromString);
        }
        else
        {
            Assert.Null(fromBytes);
            Assert.Null(fromChars);
            Assert.Null(fromString);
        }
    }

    [Theory]
    [InlineData("7.4", "7.4.0", true, true)] // the unspecified parts count as zero for the gates
    [InlineData("7.4.0", "7.4", true, true)]
    [InlineData("7.4", "7.4.0.0", true, true)]
    [InlineData("7.4.3", "7.4", true, false)]
    [InlineData("7.4", "7.4.3", false, false)]
    [InlineData("8", "7.4.3", true, false)]
    [InlineData("7.4.3", "8", false, false)]
    public void ParsedVersionGatesIgnoreUnspecifiedParts(string server, string required, bool atLeast, bool equal)
    {
        Assert.True(Format.TryParseVersion(Encoding.UTF8.GetBytes(server), out var x));
        Assert.True(Format.TryParseVersion(Encoding.UTF8.GetBytes(required), out var y));
        Assert.Equal(atLeast, x.IsAtLeast(y));
        Assert.Equal(equal, x.IsEqual(y));
    }

    public static IEnumerable<object?[]> EndpointData()
    {
        // note: the 3rd arg is for formatting; null means "expect the original string"

        // DNS
        yield return new object?[] { "localhost", new DnsEndPoint("localhost", 0), null };
        yield return new object?[] { "localhost:6390", new DnsEndPoint("localhost", 6390), null };
        yield return new object?[] { "bob.the.builder.com", new DnsEndPoint("bob.the.builder.com", 0), null };
        yield return new object?[] { "bob.the.builder.com:6390", new DnsEndPoint("bob.the.builder.com", 6390), null };
        // IPv4
        yield return new object?[] { "0.0.0.0", new IPEndPoint(IPAddress.Parse("0.0.0.0"), 0), null };
        yield return new object?[] { "127.0.0.1", new IPEndPoint(IPAddress.Parse("127.0.0.1"), 0), null };
        yield return new object?[] { "127.1", new IPEndPoint(IPAddress.Parse("127.1"), 0), "127.0.0.1" };
        yield return new object?[] { "127.1:6389", new IPEndPoint(IPAddress.Parse("127.1"), 6389), "127.0.0.1:6389" };
        yield return new object?[] { "127.0.0.1:6389", new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6389), null };
        yield return new object?[] { "127.0.0.1:1", new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1), null };
        yield return new object?[] { "127.0.0.1:2", new IPEndPoint(IPAddress.Parse("127.0.0.1"), 2), null };
        yield return new object?[] { "10.10.9.18:2", new IPEndPoint(IPAddress.Parse("10.10.9.18"), 2), null };
        // IPv6
        yield return new object?[] { "::1", new IPEndPoint(IPAddress.Parse("::1"), 0), null };
        yield return new object?[] { "::1:6379", new IPEndPoint(IPAddress.Parse("::0.1.99.121"), 0), "::0.1.99.121" }; // remember your brackets!
        yield return new object?[] { "[::1]:6379", new IPEndPoint(IPAddress.Parse("::1"), 6379), null };
        yield return new object?[] { "[::1]", new IPEndPoint(IPAddress.Parse("::1"), 0), "::1" };
        yield return new object?[] { "[::1]:1000", new IPEndPoint(IPAddress.Parse("::1"), 1000), null };
        yield return new object?[] { "2001:db7:85a3:8d2:1319:8a2e:370:7348", new IPEndPoint(IPAddress.Parse("2001:db7:85a3:8d2:1319:8a2e:370:7348"), 0), null };
        yield return new object?[] { "[2001:db7:85a3:8d2:1319:8a2e:370:7348]", new IPEndPoint(IPAddress.Parse("2001:db7:85a3:8d2:1319:8a2e:370:7348"), 0), "2001:db7:85a3:8d2:1319:8a2e:370:7348" };
        yield return new object?[] { "[2001:db7:85a3:8d2:1319:8a2e:370:7348]:1000", new IPEndPoint(IPAddress.Parse("2001:db7:85a3:8d2:1319:8a2e:370:7348"), 1000), null };
    }

    [Theory]
    [MemberData(nameof(EndpointData))]
    public void ParseEndPoint(string data, EndPoint expected, string? expectedFormat)
    {
        Assert.True(Format.TryParseEndPoint(data, out var result));
        Assert.Equal(expected, result);

        // and write again
        var s = Format.ToString(result);
        expectedFormat ??= data;
        Assert.Equal(expectedFormat, s);
    }

    // UDS endpoints live outside EndpointData because UnixDomainSocketEndPoint does not implement
    // value equality — these compare via ToString instead.
#if NET
    [Fact]
    public void ParseUnixDomainSocketEndPoint()
    {
        Assert.True(Format.TryParseEndPoint("!/tmp/redis.sock", out var ep));
        var uds = Assert.IsType<System.Net.Sockets.UnixDomainSocketEndPoint>(ep);
        Assert.Equal("/tmp/redis.sock", uds.ToString());
        Assert.Equal("!/tmp/redis.sock", Format.ToString(ep));
    }

    [Fact]
    public void ParseAbstractUnixDomainSocketEndPoint()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the abstract socket namespace is Linux-only");

        // "!@name": socat/systemd '@' convention for the Linux abstract namespace. The parse maps it
        // to the kernel's leading-NUL spelling; UnixDomainSocketEndPoint.ToString renders that back
        // as '@name', so the config string round-trips exactly.
        Assert.True(Format.TryParseEndPoint("!@redis-abstract", out var ep));
        var uds = Assert.IsType<System.Net.Sockets.UnixDomainSocketEndPoint>(ep);
        Assert.Equal("@redis-abstract", uds.ToString());
        Assert.Equal("!@redis-abstract", Format.ToString(ep));
    }
#endif

    [Theory]
    [InlineData(CommandFlags.None, "None")]
#if NETFRAMEWORK
    [InlineData(CommandFlags.PreferReplica, "PreferMaster, PreferReplica")] // 2-bit flag is hit-and-miss
    [InlineData(CommandFlags.DemandReplica, "PreferMaster, DemandReplica")] // 2-bit flag is hit-and-miss
#else
    [InlineData(CommandFlags.PreferReplica, "PreferReplica")] // 2-bit flag is hit-and-miss
    [InlineData(CommandFlags.DemandReplica, "DemandReplica")] // 2-bit flag is hit-and-miss
#endif

#if NET8_0_OR_GREATER
    [InlineData(CommandFlags.PreferReplica | CommandFlags.FireAndForget, "FireAndForget, PreferReplica")] // 2-bit flag is hit-and-miss
    [InlineData(CommandFlags.DemandReplica | CommandFlags.FireAndForget, "FireAndForget, DemandReplica")] // 2-bit flag is hit-and-miss
#else
    [InlineData(CommandFlags.PreferReplica | CommandFlags.FireAndForget, "PreferMaster, FireAndForget, PreferReplica")] // 2-bit flag is hit-and-miss
    [InlineData(CommandFlags.DemandReplica | CommandFlags.FireAndForget, "PreferMaster, FireAndForget, DemandReplica")] // 2-bit flag is hit-and-miss
#endif
    public void CommandFlagsFormatting(CommandFlags value, string expected)
    {
        Assert.SkipWhen(Runtime.IsMono, "Mono has different enum flag behavior");
        Assert.Equal(expected, value.ToString());
    }

    [Theory]
    [InlineData(ClientType.Normal, "Normal")]
    [InlineData(ClientType.Replica, "Replica")]
    [InlineData(ClientType.PubSub, "PubSub")]
    public void ClientTypeFormatting(ClientType value, string expected)
        => Assert.Equal(expected, value.ToString());

    [Theory]
    [InlineData(ClientFlags.None, "None")]
    [InlineData(ClientFlags.Replica | ClientFlags.Transaction, "Replica, Transaction")]
    [InlineData(ClientFlags.Transaction | ClientFlags.ReplicaMonitor | ClientFlags.UnixDomainSocket, "ReplicaMonitor, Transaction, UnixDomainSocket")]
    public void ClientFlagsFormatting(ClientFlags value, string expected)
        => Assert.Equal(expected, value.ToString());

    [Theory]
    [InlineData(ReplicationChangeOptions.None, "None")]
    [InlineData(ReplicationChangeOptions.ReplicateToOtherEndpoints, "ReplicateToOtherEndpoints")]
    [InlineData(ReplicationChangeOptions.SetTiebreaker | ReplicationChangeOptions.ReplicateToOtherEndpoints, "SetTiebreaker, ReplicateToOtherEndpoints")]
    [InlineData(ReplicationChangeOptions.Broadcast | ReplicationChangeOptions.SetTiebreaker | ReplicationChangeOptions.ReplicateToOtherEndpoints, "All")]
    public void ReplicationChangeOptionsFormatting(ReplicationChangeOptions value, string expected)
        => Assert.Equal(expected, value.ToString());

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(-1, "-1")]
    [InlineData(100, "100")]
    [InlineData(-100, "-100")]
    [InlineData(int.MaxValue, "2147483647")]
    [InlineData(int.MinValue, "-2147483648")]
    public unsafe void FormatInt32(int value, string expectedValue)
    {
        Span<byte> dest = stackalloc byte[expectedValue.Length];
        Assert.Equal(expectedValue.Length, Format.FormatInt32(value, dest));
        fixed (byte* s = dest)
        {
            Assert.Equal(expectedValue, Encoding.ASCII.GetString(s, expectedValue.Length));
        }
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(-1, "-1")]
    [InlineData(100, "100")]
    [InlineData(-100, "-100")]
    [InlineData(long.MaxValue, "9223372036854775807")]
    [InlineData(long.MinValue, "-9223372036854775808")]
    public unsafe void FormatInt64(long value, string expectedValue)
    {
        Assert.Equal(expectedValue.Length, Format.MeasureInt64(value));
        Span<byte> dest = stackalloc byte[expectedValue.Length];
        Assert.Equal(expectedValue.Length, Format.FormatInt64(value, dest));
        fixed (byte* s = dest)
        {
            Assert.Equal(expectedValue, Encoding.ASCII.GetString(s, expectedValue.Length));
        }
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(100, "100")]
    [InlineData(ulong.MaxValue, "18446744073709551615")]
    public unsafe void FormatUInt64(ulong value, string expectedValue)
    {
        Assert.Equal(expectedValue.Length, Format.MeasureUInt64(value));
        Span<byte> dest = stackalloc byte[expectedValue.Length];
        Assert.Equal(expectedValue.Length, Format.FormatUInt64(value, dest));
        fixed (byte* s = dest)
        {
            Assert.Equal(expectedValue, Encoding.ASCII.GetString(s, expectedValue.Length));
        }
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(-1, "-1")]
    [InlineData(0.5, "0.5")]
    [InlineData(0.50001, "0.50000999999999995")]
    [InlineData(Math.PI, "3.1415926535897931")]
    [InlineData(100, "100")]
    [InlineData(-100, "-100")]
    [InlineData(double.MaxValue, "1.7976931348623157E+308")]
    [InlineData(double.MinValue, "-1.7976931348623157E+308")]
    [InlineData(double.Epsilon, "4.9406564584124654E-324")]
    [InlineData(double.PositiveInfinity, "+inf")]
    [InlineData(double.NegativeInfinity, "-inf")]
    [InlineData(double.NaN, "NaN")] // never used in normal code

    public unsafe void FormatDouble(double value, string expectedValue)
    {
        Assert.Equal(expectedValue.Length, Format.MeasureDouble(value));
        Span<byte> dest = stackalloc byte[expectedValue.Length];
        Assert.Equal(expectedValue.Length, Format.FormatDouble(value, dest));
        fixed (byte* s = dest)
        {
            Assert.Equal(expectedValue, Encoding.ASCII.GetString(s, expectedValue.Length));
        }
    }
}
