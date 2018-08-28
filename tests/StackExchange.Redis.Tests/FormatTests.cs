using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class FormatTests : TestBase
    {
        public FormatTests(ITestOutputHelper output) : base(output) { }

        public static IEnumerable<object[]> EndpointData()
        {
            // DNS
            yield return new object[] { "localhost", new DnsEndPoint("localhost", 0) };
            yield return new object[] { "localhost:6390", new DnsEndPoint("localhost", 6390) };
            yield return new object[] { "bob.the.builder.com", new DnsEndPoint("bob.the.builder.com", 0) };
            yield return new object[] { "bob.the.builder.com:6390", new DnsEndPoint("bob.the.builder.com", 6390) };
            // IPv4
            yield return new object[] { "0.0.0.0", new IPEndPoint(IPAddress.Parse("0.0.0.0"), 0) };
            yield return new object[] { "127.0.0.1", new IPEndPoint(IPAddress.Parse("127.0.0.1"), 0) };
            yield return new object[] { "127.1", new IPEndPoint(IPAddress.Parse("127.1"), 0) };
            yield return new object[] { "127.1:6389", new IPEndPoint(IPAddress.Parse("127.1"), 6389) };
            yield return new object[] { "127.0.0.1:6389", new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6389) };
            yield return new object[] { "127.0.0.1:1", new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1) };
            yield return new object[] { "127.0.0.1:2", new IPEndPoint(IPAddress.Parse("127.0.0.1"), 2) };
            yield return new object[] { "10.10.9.18:2", new IPEndPoint(IPAddress.Parse("10.10.9.18"), 2) };
            // IPv6
            yield return new object[] { "::1", new IPEndPoint(IPAddress.Parse("::1"), 0) };
            yield return new object[] { "::1:6379", new IPEndPoint(IPAddress.Parse("::0.1.99.121"), 0) }; // remember your brackets!
            yield return new object[] { "[::1]:6379", new IPEndPoint(IPAddress.Parse("::1"), 6379) };
            yield return new object[] { "[::1]", new IPEndPoint(IPAddress.Parse("::1"), 0) };
            yield return new object[] { "[::1]:1000", new IPEndPoint(IPAddress.Parse("::1"), 1000) };
            yield return new object[] { "[2001:db7:85a3:8d2:1319:8a2e:370:7348]", new IPEndPoint(IPAddress.Parse("2001:db7:85a3:8d2:1319:8a2e:370:7348"), 0) };
            yield return new object[] { "[2001:db7:85a3:8d2:1319:8a2e:370:7348]:1000", new IPEndPoint(IPAddress.Parse("2001:db7:85a3:8d2:1319:8a2e:370:7348"), 1000) };
        }

        [Theory]
        [MemberData(nameof(EndpointData))]
        public void ParseEndPoint(string data, EndPoint expected)
        {
            var result = Format.TryParseEndPoint(data);
            Assert.Equal(expected, result);
        }
    }
}
