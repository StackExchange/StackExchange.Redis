using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Failover : TestBase, IAsyncLifetime
    {
        protected override string GetConfiguration() => GetPrimaryReplicaConfig().ToString();

        public Failover(ITestOutputHelper output) : base(output)
        {
        }

        public Task DisposeAsync() => Task.CompletedTask;

        public async Task InitializeAsync()
        {
            using (var mutex = Create())
            {
                var shouldBePrimary = mutex.GetServer(TestConfig.Current.FailoverMasterServerAndPort);
                if (shouldBePrimary.IsReplica)
                {
                    Log(shouldBePrimary.EndPoint + " should be primary, fixing...");
                    await shouldBePrimary.MakePrimaryAsync(ReplicationChangeOptions.SetTiebreaker);
                }

                var shouldBeReplica = mutex.GetServer(TestConfig.Current.FailoverReplicaServerAndPort);
                if (!shouldBeReplica.IsReplica)
                {
                    Log(shouldBeReplica.EndPoint + " should be a replica, fixing...");
                    await shouldBeReplica.ReplicaOfAsync(shouldBePrimary.EndPoint);
                    await Task.Delay(2000).ForAwait();
                }
            }
        }

        private static ConfigurationOptions GetPrimaryReplicaConfig()
        {
            return new ConfigurationOptions
            {
                AllowAdmin = true,
                SyncTimeout = 100000,
                EndPoints =
                {
                    { TestConfig.Current.FailoverMasterServer, TestConfig.Current.FailoverMasterPort },
                    { TestConfig.Current.FailoverReplicaServer, TestConfig.Current.FailoverReplicaPort },
                }
            };
        }

        [Fact]
        public async Task ConfigureAsync()
        {
            using (var muxer = Create())
            {
                await Task.Delay(1000).ForAwait();
                Log("About to reconfigure.....");
                await muxer.ConfigureAsync().ForAwait();
                Log("Reconfigured");
            }
        }

        [Fact]
        public async Task ConfigureSync()
        {
            using (var muxer = Create())
            {
                await Task.Delay(1000).ForAwait();
                Log("About to reconfigure.....");
                muxer.Configure();
                Log("Reconfigured");
            }
        }

        [Fact]
        public async Task ConfigVerifyReceiveConfigChangeBroadcast()
        {
            _ = GetConfiguration();
            using (var sender = Create(allowAdmin: true))
            using (var receiver = Create(syncTimeout: 2000))
            {
                int total = 0;
                receiver.ConfigurationChangedBroadcast += (s, a) =>
                {
                    Log("Config changed: " + (a.EndPoint == null ? "(none)" : a.EndPoint.ToString()));
                    Interlocked.Increment(ref total);
                };
                // send a reconfigure/reconnect message
                long count = sender.PublishReconfigure();
                GetServer(receiver).Ping();
                GetServer(receiver).Ping();
                await Task.Delay(1000).ConfigureAwait(false);
                Assert.True(count == -1 || count >= 2, "subscribers");
                Assert.True(Interlocked.CompareExchange(ref total, 0, 0) >= 1, "total (1st)");

                Interlocked.Exchange(ref total, 0);

                // and send a second time via a re-primary operation
                var server = GetServer(sender);
                if (server.IsReplica) Skip.Inconclusive("didn't expect a replica");
                await server.MakePrimaryAsync(ReplicationChangeOptions.Broadcast);
                await Task.Delay(1000).ConfigureAwait(false);
                GetServer(receiver).Ping();
                GetServer(receiver).Ping();
                Assert.True(Interlocked.CompareExchange(ref total, 0, 0) >= 1, "total (2nd)");
            }
        }

        [Fact]
        public async Task DereplicateGoesToPrimary()
        {
            ConfigurationOptions config = GetPrimaryReplicaConfig();
            config.ConfigCheckSeconds = 5;
            using (var conn = ConnectionMultiplexer.Connect(config))
            {
                var primary = conn.GetServer(TestConfig.Current.FailoverMasterServerAndPort);
                var secondary = conn.GetServer(TestConfig.Current.FailoverReplicaServerAndPort);

                primary.Ping();
                secondary.Ping();

                await primary.MakePrimaryAsync(ReplicationChangeOptions.SetTiebreaker);
                await secondary.MakePrimaryAsync(ReplicationChangeOptions.None);

                await Task.Delay(100).ConfigureAwait(false);

                primary.Ping();
                secondary.Ping();

                using (var writer = new StringWriter())
                {
                    conn.Configure(writer);
                    string log = writer.ToString();
                    Writer.WriteLine(log);
                    bool isUnanimous = log.Contains("tie-break is unanimous at " + TestConfig.Current.FailoverMasterServerAndPort);
                    if (!isUnanimous) Skip.Inconclusive("this is timing sensitive; unable to verify this time");
                }
                // k, so we know everyone loves 6379; is that what we get?

                var db = conn.GetDatabase();
                RedisKey key = Me();

                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferMaster));
                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandMaster));
                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferReplica));

                var ex = Assert.Throws<RedisConnectionException>(() => db.IdentifyEndpoint(key, CommandFlags.DemandReplica));
                Assert.StartsWith("No connection is active/available to service this operation: EXISTS " + Me(), ex.Message);
                Writer.WriteLine("Invoking MakePrimaryAsync()...");
                await primary.MakePrimaryAsync(ReplicationChangeOptions.Broadcast | ReplicationChangeOptions.ReplicateToOtherEndpoints | ReplicationChangeOptions.SetTiebreaker, Writer);
                Writer.WriteLine("Finished MakePrimaryAsync() call.");

                await Task.Delay(100).ConfigureAwait(false);

                Writer.WriteLine("Invoking Ping() (post-primary)");
                primary.Ping();
                secondary.Ping();
                Writer.WriteLine("Finished Ping() (post-primary)");

                Assert.True(primary.IsConnected, $"{primary.EndPoint} is not connected.");
                Assert.True(secondary.IsConnected, $"{secondary.EndPoint} is not connected.");

                Writer.WriteLine($"{primary.EndPoint}: {primary.ServerType}, Mode: {(primary.IsReplica ? "Replica" : "Primary")}");
                Writer.WriteLine($"{secondary.EndPoint}: {secondary.ServerType}, Mode: {(secondary.IsReplica ? "Replica" : "Primary")}");

                // Create a separate multiplexer with a valid view of the world to distinguish between failures of
                // server topology changes from failures to recognize those changes
                Writer.WriteLine("Connecting to secondary validation connection.");
                using (var conn2 = ConnectionMultiplexer.Connect(config))
                {
                    var primary2 = conn2.GetServer(TestConfig.Current.FailoverMasterServerAndPort);
                    var secondary2 = conn2.GetServer(TestConfig.Current.FailoverReplicaServerAndPort);

                    Writer.WriteLine($"Check: {primary2.EndPoint}: {primary2.ServerType}, Mode: {(primary2.IsReplica ? "Replica" : "Primary")}");
                    Writer.WriteLine($"Check: {secondary2.EndPoint}: {secondary2.ServerType}, Mode: {(secondary2.IsReplica ? "Replica" : "Primary")}");

                    Assert.False(primary2.IsReplica, $"{primary2.EndPoint} should be a primary (verification connection).");
                    Assert.True(secondary2.IsReplica, $"{secondary2.EndPoint} should be a replica (verification connection).");

                    var db2 = conn2.GetDatabase();

                    Assert.Equal(primary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.PreferMaster));
                    Assert.Equal(primary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.DemandMaster));
                    Assert.Equal(secondary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.PreferReplica));
                    Assert.Equal(secondary2.EndPoint, db2.IdentifyEndpoint(key, CommandFlags.DemandReplica));
                }

                await UntilConditionAsync(TimeSpan.FromSeconds(20), () => !primary.IsReplica && secondary.IsReplica);

                Assert.False(primary.IsReplica, $"{primary.EndPoint} should be a primary.");
                Assert.True(secondary.IsReplica, $"{secondary.EndPoint} should be a replica.");

                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferMaster));
                Assert.Equal(primary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandMaster));
                Assert.Equal(secondary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.PreferReplica));
                Assert.Equal(secondary.EndPoint, db.IdentifyEndpoint(key, CommandFlags.DemandReplica));
            }
        }

#if DEBUG
        [Fact]
        public async Task SubscriptionsSurvivePrimarySwitchAsync()
        {
            static void TopologyFail() => Skip.Inconclusive("Replication topology change failed...and that's both inconsistent and not what we're testing.");

            if (RunningInCI)
            {
                Skip.Inconclusive("TODO: Fix race in broadcast reconfig a zero latency.");
            }

            using (var a = Create(allowAdmin: true, shared: false))
            using (var b = Create(allowAdmin: true, shared: false))
            {
                RedisChannel channel = Me();
                Log("Using Channel: " + channel);
                var subA = a.GetSubscriber();
                var subB = b.GetSubscriber();

                long primaryChanged = 0, aCount = 0, bCount = 0;
                a.ConfigurationChangedBroadcast += delegate
                {
                    Log("A noticed config broadcast: " + Interlocked.Increment(ref primaryChanged));
                };
                b.ConfigurationChangedBroadcast += delegate
                {
                    Log("B noticed config broadcast: " + Interlocked.Increment(ref primaryChanged));
                };
                subA.Subscribe(channel, (_, message) =>
                {
                    Log("A got message: " + message);
                    Interlocked.Increment(ref aCount);
                });
                subB.Subscribe(channel, (_, message) =>
                {
                    Log("B got message: " + message);
                    Interlocked.Increment(ref bCount);
                });

                Assert.False(a.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsReplica, $"A Connection: {TestConfig.Current.FailoverMasterServerAndPort} should be a master");
                if (!a.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica)
                {
                    TopologyFail();
                }
                Assert.True(a.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica, $"A Connection: {TestConfig.Current.FailoverReplicaServerAndPort} should be a replica");
                Assert.False(b.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsReplica, $"B Connection: {TestConfig.Current.FailoverMasterServerAndPort} should be a master");
                Assert.True(b.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica, $"B Connection: {TestConfig.Current.FailoverReplicaServerAndPort} should be a replica");

                Log("Failover 1 Complete");
                var epA = subA.SubscribedEndpoint(channel);
                var epB = subB.SubscribedEndpoint(channel);
                Log("  A: " + EndPointCollection.ToString(epA));
                Log("  B: " + EndPointCollection.ToString(epB));
                subA.Publish(channel, "A1");
                subB.Publish(channel, "B1");
                Log("  SubA ping: " + subA.Ping());
                Log("  SubB ping: " + subB.Ping());
                // If redis is under load due to this suite, it may take a moment to send across.
                await UntilConditionAsync(TimeSpan.FromSeconds(5), () => Interlocked.Read(ref aCount) == 2 && Interlocked.Read(ref bCount) == 2).ForAwait();

                Assert.Equal(2, Interlocked.Read(ref aCount));
                Assert.Equal(2, Interlocked.Read(ref bCount));
                Assert.Equal(0, Interlocked.Read(ref primaryChanged));

                try
                {
                    Interlocked.Exchange(ref primaryChanged, 0);
                    Interlocked.Exchange(ref aCount, 0);
                    Interlocked.Exchange(ref bCount, 0);
                    Log("Changing primary...");
                    using (var sw = new StringWriter())
                    {
                        await a.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).MakePrimaryAsync(ReplicationChangeOptions.All, sw);
                        Log(sw.ToString());
                    }
                    Log("Waiting for connection B to detect...");
                    await UntilConditionAsync(TimeSpan.FromSeconds(10), () => b.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsReplica).ForAwait();
                    subA.Ping();
                    subB.Ping();
                    Log("Failover 2 Attempted. Pausing...");
                    Log("  A " + TestConfig.Current.FailoverMasterServerAndPort + " status: " + (a.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsReplica ? "Replica" : "Primary"));
                    Log("  A " + TestConfig.Current.FailoverReplicaServerAndPort + " status: " + (a.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica ? "Replica" : "Primary"));
                    Log("  B " + TestConfig.Current.FailoverMasterServerAndPort + " status: " + (b.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsReplica ? "Replica" : "Primary"));
                    Log("  B " + TestConfig.Current.FailoverReplicaServerAndPort + " status: " + (b.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica ? "Replica" : "Primary"));

                    if (!a.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsReplica)
                    {
                        TopologyFail();
                    }
                    Log("Failover 2 Complete.");

                    Assert.True(a.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsReplica, $"A Connection: {TestConfig.Current.FailoverMasterServerAndPort} should be a replica");
                    Assert.False(a.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica, $"A Connection: {TestConfig.Current.FailoverReplicaServerAndPort} should be a master");
                    await UntilConditionAsync(TimeSpan.FromSeconds(10), () => b.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsReplica).ForAwait();
                    var sanityCheck = b.GetServer(TestConfig.Current.FailoverMasterServerAndPort).IsReplica;
                    if (!sanityCheck)
                    {
                        Log("FAILURE: B has not detected the topology change.");
                        foreach (var server in b.GetServerSnapshot().ToArray())
                        {
                            Log("  Server" + server.EndPoint);
                            Log("    State: " + server.ConnectionState);
                            Log("    IsReplica: " + !server.IsReplica);
                            Log("    Type: " + server.ServerType);
                        }
                        //Skip.Inconclusive("Not enough latency.");
                    }
                    Assert.True(sanityCheck, $"B Connection: {TestConfig.Current.FailoverMasterServerAndPort} should be a replica");
                    Assert.False(b.GetServer(TestConfig.Current.FailoverReplicaServerAndPort).IsReplica, $"B Connection: {TestConfig.Current.FailoverReplicaServerAndPort} should be a master");

                    Log("Pause complete");
                    Log("  A outstanding: " + a.GetCounters().TotalOutstanding);
                    Log("  B outstanding: " + b.GetCounters().TotalOutstanding);
                    subA.Ping();
                    subB.Ping();
                    await Task.Delay(5000).ForAwait();
                    epA = subA.SubscribedEndpoint(channel);
                    epB = subB.SubscribedEndpoint(channel);
                    Log("Subscription complete");
                    Log("  A: " + EndPointCollection.ToString(epA));
                    Log("  B: " + EndPointCollection.ToString(epB));
                    var aSentTo = subA.Publish(channel, "A2");
                    var bSentTo = subB.Publish(channel, "B2");
                    Log("  A2 sent to: " + aSentTo);
                    Log("  B2 sent to: " + bSentTo);
                    subA.Ping();
                    subB.Ping();
                    Log("Ping Complete. Checking...");
                    await UntilConditionAsync(TimeSpan.FromSeconds(10), () => Interlocked.Read(ref aCount) == 2 && Interlocked.Read(ref bCount) == 2).ForAwait();

                    Log("Counts so far:");
                    Log("  aCount: " + Interlocked.Read(ref aCount));
                    Log("  bCount: " + Interlocked.Read(ref bCount));
                    Log("  primaryChanged: " + Interlocked.Read(ref primaryChanged));

                    Assert.Equal(2, Interlocked.Read(ref aCount));
                    Assert.Equal(2, Interlocked.Read(ref bCount));
                    // Expect 12, because a sees a, but b sees a and b due to replication
                    Assert.Equal(12, Interlocked.CompareExchange(ref primaryChanged, 0, 0));
                }
                catch
                {
                    LogNoTime("");
                    Log("ERROR: Something went bad - see above! Roooooolling back. Back it up. Baaaaaack it on up.");
                    LogNoTime("");
                    throw;
                }
                finally
                {
                    Log("Restoring configuration...");
                    try
                    {
                        await a.GetServer(TestConfig.Current.FailoverMasterServerAndPort).MakePrimaryAsync(ReplicationChangeOptions.All);
                        await Task.Delay(1000).ForAwait();
                    }
                    catch { /* Don't bomb here */ }
                }
            }
        }
#endif
    }
}
