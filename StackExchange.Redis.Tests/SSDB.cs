﻿using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class SSDB : TestBase
    {
        public SSDB(ITestOutputHelper output) : base (output) { }

        [Fact]
        public void ConnectToSSDB()
        {
            Skip.IfNoConfig(nameof(TestConfig.Config.SSDBServer), TestConfig.Current.SSDBServer);

            var config = new ConfigurationOptions
            {
                EndPoints = { { TestConfig.Current.SSDBServer, TestConfig.Current.SSDBPort } },
                CommandMap = CommandMap.SSDB
            };
            RedisKey key = Me();
            using (var conn = ConnectionMultiplexer.Connect(config))
            {
                var db = conn.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                Assert.True(db.StringGet(key).IsNull);
                db.StringSet(key, "abc", flags: CommandFlags.FireAndForget);
                Assert.Equal("abc", db.StringGet(key));
            }
        }
    }
}
