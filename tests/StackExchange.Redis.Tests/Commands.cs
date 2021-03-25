using System.Net;
using Xunit;

namespace StackExchange.Redis.Tests
{
    public class Commands
    {
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
}
