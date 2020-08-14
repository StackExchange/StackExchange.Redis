using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Roles : TestBase
    {
        public Roles(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void MasterRole()
        {
            var connectionString = $"{TestConfig.Current.MasterServer}:{TestConfig.Current.MasterPort},allowAdmin=true";
            var conn = ConnectionMultiplexer.Connect(connectionString);
            var server = conn.GetServer(TestConfig.Current.MasterServerAndPort);

            var role = server.Role();
            Assert.NotNull(role);
            Assert.Equal(role.Value, RedisLiterals.master);
            var masterRole = role as Role.Master;
            Assert.NotNull(masterRole);
        }
    }
}
