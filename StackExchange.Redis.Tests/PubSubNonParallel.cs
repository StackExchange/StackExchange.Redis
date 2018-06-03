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
        private async Task EnsureMasterSlaveSetupAsync(ConnectionMultiplexer mutex)
        {
            var shouldBeMaster = mutex.GetServer(TestConfig.Current.MasterServerAndPort);
            if (shouldBeMaster.IsSlave)
            {
                Output.WriteLine(shouldBeMaster.EndPoint + " should be master, fixing...");
                shouldBeMaster.MakeMaster(ReplicationChangeOptions.SetTiebreaker);
            }

            Output.WriteLine("Flushing all databases...");
            shouldBeMaster.FlushAllDatabases(CommandFlags.FireAndForget);

            var shouldBeReplica = mutex.GetServer(TestConfig.Current.SlaveServerAndPort);
            if (!shouldBeReplica.IsSlave)
            {
                Output.WriteLine(shouldBeReplica.EndPoint + " should be a slave, fixing...");
                shouldBeReplica.SlaveOf(shouldBeMaster.EndPoint);
                await Task.Delay(1000).ForAwait();
            }
        }

        public PubSubNonParallel(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SubscriptionsSurviveMasterSwitchAsync(bool useSharedSocketManager)
        {
            using (var a = Create(allowAdmin: true, useSharedSocketManager: useSharedSocketManager))
            using (var b = Create(allowAdmin: true, useSharedSocketManager: useSharedSocketManager))
            {
                try
                {
                    // Ensure config setup
                    await EnsureMasterSlaveSetupAsync(a).ForAwait();
                    RedisChannel channel = Me();
                    var subA = a.GetSubscriber();
                    var subB = b.GetSubscriber();

                    long masterChanged = 0, aCount = 0, bCount = 0;
                    a.ConfigurationChangedBroadcast += delegate
                    {
                        Output.WriteLine("A noticed config broadcast: " + Interlocked.Increment(ref masterChanged));
                    };
                    b.ConfigurationChangedBroadcast += delegate
                    {
                        Output.WriteLine("B noticed config broadcast: " + Interlocked.Increment(ref masterChanged));
                    };
                    subA.Subscribe(channel, (_, message) =>
                    {
                        Output.WriteLine("A got message: " + message);
                        Interlocked.Increment(ref aCount);
                    });
                    subB.Subscribe(channel, (_, message) =>
                    {
                        Output.WriteLine("B got message: " + message);
                        Interlocked.Increment(ref bCount);
                    });

                    Assert.False(a.GetServer(TestConfig.Current.MasterServerAndPort).IsSlave, $"A Connection: {TestConfig.Current.MasterServerAndPort} should be a master");
                    Assert.True(a.GetServer(TestConfig.Current.SlaveServerAndPort).IsSlave, $"A Connection: {TestConfig.Current.SlaveServerAndPort} should be a slave");
                    Assert.False(b.GetServer(TestConfig.Current.MasterServerAndPort).IsSlave, $"B Connection: {TestConfig.Current.MasterServerAndPort} should be a master");
                    Assert.True(b.GetServer(TestConfig.Current.SlaveServerAndPort).IsSlave, $"B Connection: {TestConfig.Current.SlaveServerAndPort} should be a slave");

                    var epA = subA.SubscribedEndpoint(channel);
                    var epB = subB.SubscribedEndpoint(channel);
                    Output.WriteLine("A: " + EndPointCollection.ToString(epA));
                    Output.WriteLine("B: " + EndPointCollection.ToString(epB));
                    subA.Publish(channel, "A1");
                    subB.Publish(channel, "B1");
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
                        await Task.Delay(5000).ForAwait();
                        subA.Ping();
                        subB.Ping();
                        Output.WriteLine("Pausing...");
                        Output.WriteLine("A " + TestConfig.Current.MasterServerAndPort + " status: " + (a.GetServer(TestConfig.Current.MasterServerAndPort).IsSlave ? "Slave" : "Master"));
                        Output.WriteLine("A " + TestConfig.Current.SlaveServerAndPort + " status: " + (a.GetServer(TestConfig.Current.SlaveServerAndPort).IsSlave ? "Slave" : "Master"));
                        Output.WriteLine("B " + TestConfig.Current.MasterServerAndPort + " status: " + (b.GetServer(TestConfig.Current.MasterServerAndPort).IsSlave ? "Slave" : "Master"));
                        Output.WriteLine("B " + TestConfig.Current.SlaveServerAndPort + " status: " + (b.GetServer(TestConfig.Current.SlaveServerAndPort).IsSlave ? "Slave" : "Master"));

                        Assert.True(a.GetServer(TestConfig.Current.MasterServerAndPort).IsSlave, $"A Connection: {TestConfig.Current.MasterServerAndPort} should be a slave");
                        Assert.False(a.GetServer(TestConfig.Current.SlaveServerAndPort).IsSlave, $"A Connection: {TestConfig.Current.SlaveServerAndPort} should be a master");
                        Assert.True(b.GetServer(TestConfig.Current.MasterServerAndPort).IsSlave, $"B Connection: {TestConfig.Current.MasterServerAndPort} should be a slave");
                        Assert.False(b.GetServer(TestConfig.Current.SlaveServerAndPort).IsSlave, $"B Connection: {TestConfig.Current.SlaveServerAndPort} should be a master");

                        Output.WriteLine("Pause complete");
                        Output.WriteLine("A outstanding: " + a.GetCounters().TotalOutstanding);
                        Output.WriteLine("B outstanding: " + b.GetCounters().TotalOutstanding);
                        subA.Ping();
                        subB.Ping();
                        await Task.Delay(2000).ForAwait();
                        epA = subA.SubscribedEndpoint(channel);
                        epB = subB.SubscribedEndpoint(channel);
                        Output.WriteLine("A: " + EndPointCollection.ToString(epA));
                        Output.WriteLine("B: " + EndPointCollection.ToString(epB));
                        Output.WriteLine("A2 sent to: " + subA.Publish(channel, "A2"));
                        Output.WriteLine("B2 sent to: " + subB.Publish(channel, "B2"));
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
                            await Task.Delay(1000).ForAwait();
                        }
                        catch { }
                    }
                }
                finally
                {
                    // Put it back, even if we fail...
                    await EnsureMasterSlaveSetupAsync(a);
                }
            }
        }
    }
}
