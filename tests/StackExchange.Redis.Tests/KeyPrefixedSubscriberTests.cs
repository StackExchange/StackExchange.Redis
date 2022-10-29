using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class KeyPrefixedSubscriberTests : TestBase
    {
        public KeyPrefixedSubscriberTests(ITestOutputHelper output) : base(output)
        {}

        [Fact]
        public async Task UsePrefixForChannel()
        {
            using (var client = Create(allowAdmin: true))
            {
                const string prefix1 = "(p1)-";
                const string prefix2 = "(p2)-";

                var s1 = client.GetSubscriber().WithChannelPrefix(prefix1);
                var s12 = client.GetSubscriber().WithChannelPrefix(prefix1);
                var s2 = client.GetSubscriber().WithChannelPrefix(prefix2);
                var s = client.GetSubscriber();

                var l1 = new List<string>();
                var l12 = new List<string>();
                var l2 = new List<string>();
                var l = new List<string>();
                var lAll = new List<string>();
                var lT1 = new List<string>();
                var c1 = new List<string>();

                const string channelName = "test-channel";
                s1.Subscribe(channelName, (channel, value) =>
                {
                    c1.Add(channel);
                    l1.Add(value);
                });
                s12.Subscribe(channelName, (channel, value) => l12.Add(value));
                s2.Subscribe(channelName, (channel, value) => l2.Add(value));
                s.Subscribe(channelName, (channel, value) => l.Add(value));
                s.Subscribe("*" + channelName, (channel, value) => lAll.Add(value));
                s.Subscribe(prefix1 + channelName, (channel, value) => lT1.Add(value));

                s1.Publish(channelName, "value1");
                s.Publish(channelName, "value");

                // Give some time to pub-sub
                await Task.Delay(500);

                Assert.Single(l1);
                Assert.Equal("value1",l1[0]);

                Assert.Single(l12);
                Assert.Equal("value1",l12[0]);
            
                Assert.Empty(l2);

                Assert.Single(l);
                Assert.Equal("value",l[0]);

                Assert.Equal(2, lAll.Count);
                Assert.Contains("value", lAll);
                Assert.Contains("value1", lAll);

                Assert.Single(lT1);
                Assert.Equal("value1",lT1[0]);

                Assert.Single(c1);
                Assert.Equal(channelName,c1[0]);
            }
        }
    }
}
