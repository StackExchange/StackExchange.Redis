using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    [Collection(NonParallelCollection.Name)]
    public class PubSubNonParallel : TestBase
    {
        public PubSubNonParallel(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SubscriptionsSurviveMasterSwitchAsync(bool useSharedSocketManager)
        {
            using (var a = Create(allowAdmin: true, useSharedSocketManager: useSharedSocketManager))
            using (var b = Create(allowAdmin: true, useSharedSocketManager: useSharedSocketManager))
            {
                RedisChannel channel = Me();
                var subA = a.GetSubscriber();
                var subB = b.GetSubscriber();

                long masterChanged = 0, aCount = 0, bCount = 0;
                a.ConfigurationChangedBroadcast += delegate
                {
                    Output.WriteLine("a noticed config broadcast: " + Interlocked.Increment(ref masterChanged));
                };
                b.ConfigurationChangedBroadcast += delegate
                {
                    Output.WriteLine("b noticed config broadcast: " + Interlocked.Increment(ref masterChanged));
                };
                subA.Subscribe(channel, (ch, message) =>
                {
                    Output.WriteLine("a got message: " + message);
                    Interlocked.Increment(ref aCount);
                });
                subB.Subscribe(channel, (ch, message) =>
                {
                    Output.WriteLine("b got message: " + message);
                    Interlocked.Increment(ref bCount);
                });

                Assert.False(a.GetServer(TestConfig.Current.MasterServerAndPort).IsSlave, $"{TestConfig.Current.MasterServerAndPort} should be master via a");
                Assert.True(a.GetServer(TestConfig.Current.SlaveServerAndPort).IsSlave, $"{TestConfig.Current.SlaveServerAndPort} should be slave via a");
                Assert.False(b.GetServer(TestConfig.Current.MasterServerAndPort).IsSlave, $"{TestConfig.Current.MasterServerAndPort} should be master via b");
                Assert.True(b.GetServer(TestConfig.Current.SlaveServerAndPort).IsSlave, $"{TestConfig.Current.SlaveServerAndPort} should be slave via b");

                var epA = subA.SubscribedEndpoint(channel);
                var epB = subB.SubscribedEndpoint(channel);
                Output.WriteLine("a: " + EndPointCollection.ToString(epA));
                Output.WriteLine("b: " + EndPointCollection.ToString(epB));
                subA.Publish(channel, "a1");
                subB.Publish(channel, "b1");
                subA.Ping();
                subB.Ping();

                Assert.Equal(2, Interlocked.Read(ref aCount));
                Assert.Equal(2, Interlocked.Read(ref bCount));
                Assert.Equal(0, Interlocked.Read(ref masterChanged));

                try
                {
                    Interlocked.Exchange(ref masterChanged, 0);
                    Interlocked.Exchange(ref aCount, 0);
                    Interlocked.Exchange(ref bCount, 0);
                    Output.WriteLine("Changing master...");
                    using (var sw = new StringWriter())
                    {
                        a.GetServer(TestConfig.Current.SlaveServerAndPort).MakeMaster(ReplicationChangeOptions.All, sw);
                        Output.WriteLine(sw.ToString());
                    }
                    subA.Ping();
                    subB.Ping();
                    Output.WriteLine("Pausing...");
                    await Task.Delay(6000).ForAwait();

                    Assert.True(a.GetServer(TestConfig.Current.MasterServerAndPort).IsSlave, $"{TestConfig.Current.MasterServerAndPort} should be a slave via a");
                    Assert.False(a.GetServer(TestConfig.Current.SlaveServerAndPort).IsSlave, $"{TestConfig.Current.SlaveServerAndPort} should be a master via a");
                    Assert.True(b.GetServer(TestConfig.Current.MasterServerAndPort).IsSlave, $"{TestConfig.Current.MasterServerAndPort} should be a slave via b");
                    Assert.False(b.GetServer(TestConfig.Current.SlaveServerAndPort).IsSlave, $"{TestConfig.Current.SlaveServerAndPort} should be a master via b");

                    Output.WriteLine("Pause complete");
                    var counters = a.GetCounters();
                    Output.WriteLine("a outstanding: " + counters.TotalOutstanding);
                    counters = b.GetCounters();
                    Output.WriteLine("b outstanding: " + counters.TotalOutstanding);
                    subA.Ping();
                    subB.Ping();
                    await Task.Delay(2000).ForAwait();
                    epA = subA.SubscribedEndpoint(channel);
                    epB = subB.SubscribedEndpoint(channel);
                    Output.WriteLine("a: " + EndPointCollection.ToString(epA));
                    Output.WriteLine("b: " + EndPointCollection.ToString(epB));
                    Output.WriteLine("a2 sent to: " + subA.Publish(channel, "a2"));
                    Output.WriteLine("b2 sent to: " + subB.Publish(channel, "b2"));
                    subA.Ping();
                    subB.Ping();
                    Output.WriteLine("Checking...");

                    Assert.Equal(2, Interlocked.Read(ref aCount));
                    Assert.Equal(2, Interlocked.Read(ref bCount));
                    // Expect 6, because a sees a, but b sees a and b due to replication
                    Assert.Equal(6, Interlocked.CompareExchange(ref masterChanged, 0, 0));
                }
                finally
                {
                    Output.WriteLine("Restoring configuration...");
                    try
                    {
                        a.GetServer(TestConfig.Current.MasterServerAndPort).MakeMaster(ReplicationChangeOptions.All);
                    }
                    catch
                    { }
                }
            }
        }
    }
}
