using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Failover : TestBase
    {
        protected override string GetConfiguration() => GetMasterSlaveConfig().ToString();

        public Failover(ITestOutputHelper output) : base(output)
        {
            using (var mutex = Create())
            {
                var shouldBeMaster = mutex.GetServer(TestConfig.Current.FailoverMasterServerAndPort);
                if (shouldBeMaster.IsSlave)
                {
                    Output.WriteLine(shouldBeMaster.EndPoint + " should be master, fixing...");
                    shouldBeMaster.MakeMaster(ReplicationChangeOptions.SetTiebreaker);
                }

                Output.WriteLine("Flushing all databases...");
                shouldBeMaster.FlushAllDatabases(CommandFlags.FireAndForget);

                var shouldBeReplica = mutex.GetServer(TestConfig.Current.FailoverSlaveServerAndPort);
                if (!shouldBeReplica.IsSlave)
                {
                    Output.WriteLine(shouldBeReplica.EndPoint + " should be a slave, fixing...");
                    shouldBeReplica.SlaveOf(shouldBeMaster.EndPoint);
                    Thread.Sleep(2000);
                }
            }
        }

        private static ConfigurationOptions GetMasterSlaveConfig()
        {
            return new ConfigurationOptions
            {
                AllowAdmin = true,
                SyncTimeout = 100000,
                EndPoints =
                {
                    { TestConfig.Current.FailoverMasterServer, TestConfig.Current.FailoverMasterPort },
                    { TestConfig.Current.FailoverSlaveServer, TestConfig.Current.FailoverSlavePort },
                }
            };
        }

        [Fact]
        public async Task ConfigVerifyReceiveConfigChangeBroadcast()
        {
            var config = GetConfiguration();
            using (var sender = Create(allowAdmin: true))
            using (var receiver = Create(syncTimeout: 2000))
            {
                int total = 0;
                receiver.ConfigurationChangedBroadcast += (s, a) =>
                {
                    Output.WriteLine("Config changed: " + (a.EndPoint == null ? "(none)" : a.EndPoint.ToString()));
                    Interlocked.Increment(ref total);
                };
                // send a reconfigure/reconnect message
                long count = sender.PublishReconfigure();
                GetServer(receiver).Ping();
                GetServer(receiver).Ping();
                await Task.Delay(10).ConfigureAwait(false);
                Assert.True(count == -1 || count >= 2, "subscribers");
                Assert.True(Interlocked.CompareExchange(ref total, 0, 0) >= 1, "total (1st)");

                Interlocked.Exchange(ref total, 0);

                // and send a second time via a re-master operation
                var server = GetServer(sender);
                if (server.IsSlave) Skip.Inconclusive("didn't expect a slave");
                server.MakeMaster(ReplicationChangeOptions.Broadcast);
                await Task.Delay(100).ConfigureAwait(false);
                GetServer(receiver).Ping();
                GetServer(receiver).Ping();
                Assert.True(Interlocked.CompareExchange(ref total, 0, 0) >= 1, "total (2nd)");
            }
        }

        [Fact]
        public async Task DeslaveGoesToPrimary()
        {
            ConfigurationOptions config = GetMasterSlaveConfig();
            using (var conn = ConnectionMultiplexer.Connect(config))
            {
                var primary = conn.GetServer(TestConfig.Current.FailoverMasterServerAndPort);
                var secondary = conn.GetServer(TestConfig.Current.FailoverSlaveServerAndPort);

                primary.Ping();
                secondary.Ping();

                primary.MakeMaster(ReplicationChangeOptions.SetTiebreaker);
                secondary.MakeMaster(ReplicationChangeOptions.None);

                await Task.Delay(100).ConfigureAwait(false);

                primary.Ping();
                secondary.Ping();

                using (var writer = new StringWriter())
                {
                    conn.Configure(writer);
                    string log = writer.ToString();

                    Assert.True(log.Contains("tie-break is unanimous at " + TestConfig.Current.FailoverMasterServerAndPort), "unanimous");
                }
                // k, so we know everyone loves 6379; is that what we get?

                var db = conn.GetDatabase();
                RedisKey key = Me();

                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferMaster));
                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandMaster));
                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferSlave));

                var ex = Assert.Throws<RedisConnectionException>(() => db.IdentifyEndpoint(key, CommandFlags.DemandSlave));
                Assert.StartsWith("No connection is available to service this operation: EXISTS " + Me(), ex.Message);
                Writer.WriteLine("Invoking MakeMaster()...");
                primary.MakeMaster(ReplicationChangeOptions.Broadcast | ReplicationChangeOptions.EnslaveSubordinates | ReplicationChangeOptions.SetTiebreaker, Writer);
                Writer.WriteLine("Finished MakeMaster() call.");

                await Task.Delay(100).ConfigureAwait(false);

                Writer.WriteLine("Invoking Ping() (post-master)");
                primary.Ping();
                secondary.Ping();
                Writer.WriteLine("Finished Ping() (post-master)");

                Assert.True(primary.IsConnected, $"{primary.EndPoint} is not connected.");
                Assert.True(secondary.IsConnected, $"{secondary.EndPoint} is not connected.");

                Writer.WriteLine($"{primary.EndPoint}: {primary.ServerType}, Mode: {(primary.IsSlave ? "Slave" : "Master")}");
                Writer.WriteLine($"{secondary.EndPoint}: {secondary.ServerType}, Mode: {(secondary.IsSlave ? "Slave" : "Master")}");

                // Create a separate multiplexer with a valid view of the world to distinguish between failures of
                // server topology changes from failures to recognize those changes
                Writer.WriteLine("Connecting to secondary validation connection.");
                using (var conn2 = ConnectionMultiplexer.Connect(config))
                {
                    var primary2 = conn.GetServer(TestConfig.Current.FailoverMasterServerAndPort);
                    var secondary2 = conn.GetServer(TestConfig.Current.FailoverSlaveServerAndPort);

                    Writer.WriteLine($"Check: {primary2.EndPoint}: {primary2.ServerType}, Mode: {(primary2.IsSlave ? "Slave" : "Master")}");
                    Writer.WriteLine($"Check: {secondary2.EndPoint}: {secondary2.ServerType}, Mode: {(secondary2.IsSlave ? "Slave" : "Master")}");

                    Assert.False(primary2.IsSlave, $"{primary2.EndPoint} should be a master (verification connection).");
                    Assert.True(secondary2.IsSlave, $"{secondary2.EndPoint} should be a slave (verification connection).");

                    var db2 = conn.GetDatabase();

                    Assert.Equal(primary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.PreferMaster));
                    Assert.Equal(primary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.DemandMaster));
                    Assert.Equal(secondary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.PreferSlave));
                    Assert.Equal(secondary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.DemandSlave));
                }

                Assert.False(primary.IsSlave, $"{primary.EndPoint} should be a master.");
                Assert.True(secondary.IsSlave, $"{secondary.EndPoint} should be a slave.");

                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferMaster));
                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandMaster));
                Assert.Equal(secondary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferSlave));
                Assert.Equal(secondary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandSlave));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SubscriptionsSurviveMasterSwitchAsync(bool useSharedSocketManager)
        {
            if (RunningInCI)
            {
                Skip.Inconclusive("TODO: Fix race in broadcast reconfig a zero latency.");
            }

            using (var a = Create(allowAdmin: true, useSharedSocketManager: useSharedSocketManager))
            using (var b = Create(allowAdmin: true, useSharedSocketManager: useSharedSocketManager))
            {
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

                Assert.False(a.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsSlave, $"A Connection: {TestConfig.Current.FailoverMasterServerAndPort} should be a master");
                Assert.True(a.GetServer(TestConfig.Current.FailoverSlaveServerAndPort).IsSlave, $"A Connection: {TestConfig.Current.FailoverSlaveServerAndPort} should be a slave");
                Assert.False(b.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsSlave, $"B Connection: {TestConfig.Current.FailoverMasterServerAndPort} should be a master");
                Assert.True(b.GetServer(TestConfig.Current.FailoverSlaveServerAndPort).IsSlave, $"B Connection: {TestConfig.Current.FailoverSlaveServerAndPort} should be a slave");

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
                        a.GetServer(TestConfig.Current.FailoverSlaveServerAndPort).MakeMaster(ReplicationChangeOptions.All, sw);
                        Output.WriteLine(sw.ToString());
                    }
                    await Task.Delay(5000).ForAwait();
                    subA.Ping();
                    subB.Ping();
                    Output.WriteLine("Pausing...");
                    Output.WriteLine("A " + TestConfig.Current.FailoverMasterServerAndPort + " status: " + (a.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsSlave ? "Slave" : "Master"));
                    Output.WriteLine("A " + TestConfig.Current.FailoverSlaveServerAndPort + " status: " + (a.GetServer(TestConfig.Current.FailoverSlaveServerAndPort).IsSlave ? "Slave" : "Master"));
                    Output.WriteLine("B " + TestConfig.Current.FailoverMasterServerAndPort + " status: " + (b.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsSlave ? "Slave" : "Master"));
                    Output.WriteLine("B " + TestConfig.Current.FailoverSlaveServerAndPort + " status: " + (b.GetServer(TestConfig.Current.FailoverSlaveServerAndPort).IsSlave ? "Slave" : "Master"));

                    Assert.True(a.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsSlave, $"A Connection: {TestConfig.Current.FailoverMasterServerAndPort} should be a slave");
                    Assert.False(a.GetServer(TestConfig.Current.FailoverSlaveServerAndPort).IsSlave, $"A Connection: {TestConfig.Current.FailoverSlaveServerAndPort} should be a master");
                    Assert.True(b.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsSlave, $"B Connection: {TestConfig.Current.FailoverMasterServerAndPort} should be a slave");
                    Assert.False(b.GetServer(TestConfig.Current.FailoverSlaveServerAndPort).IsSlave, $"B Connection: {TestConfig.Current.FailoverSlaveServerAndPort} should be a master");

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
                        a.GetServer(TestConfig.Current.FailoverMasterServerAndPort).MakeMaster(ReplicationChangeOptions.All);
                        await Task.Delay(1000).ForAwait();
                    }
                    catch { }
                }
            }
        }
    }
}
