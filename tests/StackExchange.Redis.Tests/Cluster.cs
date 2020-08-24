using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Profiling;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Cluster : TestBase
    {
        public Cluster(ITestOutputHelper output) : base (output) { }

        protected override string GetConfiguration()
        {
            var server = TestConfig.Current.ClusterServer;
            return string.Join(",",
                Enumerable.Range(TestConfig.Current.ClusterStartPort, TestConfig.Current.ClusterServerCount).Select(port => server + ":" + port)
            ) + ",connectTimeout=10000";
        }

        [Fact]
        public void ExportConfiguration()
        {
            if (File.Exists("cluster.zip")) File.Delete("cluster.zip");
            Assert.False(File.Exists("cluster.zip"));
            using (var muxer = Create(allowAdmin: true))
            using (var file = File.Create("cluster.zip"))
            {
                muxer.ExportConfiguration(file);
            }
            Assert.True(File.Exists("cluster.zip"));
        }

        [Fact]
        public void ConnectUsesSingleSocket()
        {
            using (var sw = new StringWriter())
            {
                try
                {
                    for (int i = 0; i < 5; i++)
                    {
                        using (var muxer = Create(failMessage: i + ": ", log: sw))
                        {
                            foreach (var ep in muxer.GetEndPoints())
                            {
                                var srv = muxer.GetServer(ep);
                                var counters = srv.GetCounters();
                                Log($"{i}; interactive, {ep}, count: {counters.Interactive.SocketCount}");
                                Log($"{i}; subscription, {ep}, count: {counters.Subscription.SocketCount}");
                            }
                            foreach (var ep in muxer.GetEndPoints())
                            {
                                var srv = muxer.GetServer(ep);
                                var counters = srv.GetCounters();
                                Assert.Equal(1, counters.Interactive.SocketCount);
                                Assert.Equal(1, counters.Subscription.SocketCount);
                            }
                        }
                    }
                }
                finally
                {
                    // Connection info goes at the end...
                    Log(sw.ToString());
                }
            }
        }

        [Fact]
        public void CanGetTotalStats()
        {
            using (var muxer = Create())
            {
                var counters = muxer.GetCounters();
                Log(counters.ToString());
            }
        }

        private void PrintEndpoints(EndPoint[] endpoints)
        {
            Log($"Endpoints Expected: {TestConfig.Current.ClusterStartPort}+{TestConfig.Current.ClusterServerCount}");
            Log("Endpoints Found:");
            foreach (var endpoint in endpoints)
            {
                Log("  Endpoint: " + endpoint);
            }
        }

        [Fact]
        public void Connect()
        {
            var expectedPorts = new HashSet<int>(Enumerable.Range(TestConfig.Current.ClusterStartPort, TestConfig.Current.ClusterServerCount));
            using (var sw = new StringWriter())
            using (var muxer = Create(log: sw))
            {
                var endpoints = muxer.GetEndPoints();
                if (TestConfig.Current.ClusterServerCount != endpoints.Length)
                {
                    PrintEndpoints(endpoints);
                }
                else
                {
                    Log(sw.ToString());
                }

                Assert.Equal(TestConfig.Current.ClusterServerCount, endpoints.Length);
                int masters = 0, replicas = 0;
                var failed = new List<EndPoint>();
                foreach (var endpoint in endpoints)
                {
                    var server = muxer.GetServer(endpoint);
                    if (!server.IsConnected)
                    {
                        failed.Add(endpoint);
                    }
                    Log("endpoint:" + endpoint);
                    Assert.Equal(endpoint, server.EndPoint);

                    Log("endpoint-type:" + endpoint);
                    Assert.IsType<IPEndPoint>(endpoint);

                    Log("port:" + endpoint);
                    Assert.True(expectedPorts.Remove(((IPEndPoint)endpoint).Port));

                    Log("server-type:" + endpoint);
                    Assert.Equal(ServerType.Cluster, server.ServerType);

                    if (server.IsReplica) replicas++;
                    else masters++;
                }
                if (failed.Count != 0)
                {
                    Log("{0} failues", failed.Count);
                    foreach (var fail in failed)
                    {
                        Log(fail.ToString());
                    }
                    Assert.True(false, "not all servers connected");
                }

                Assert.Equal(TestConfig.Current.ClusterServerCount / 2, replicas);
                Assert.Equal(TestConfig.Current.ClusterServerCount / 2, masters);
            }
        }

        [Fact]
        public void TestIdentity()
        {
            using (var conn = Create())
            {
                RedisKey key = Guid.NewGuid().ToByteArray();
                var ep = conn.GetDatabase().IdentifyEndpoint(key);
                Assert.Equal(ep, conn.GetServer(ep).ClusterConfiguration.GetBySlot(key).EndPoint);
            }
        }

        [Fact]
        public void IntentionalWrongServer()
        {
            string StringGet(IServer server, RedisKey key, CommandFlags flags = CommandFlags.None)
                => (string)server.Execute("GET", new object[] { key }, flags);

            using (var conn = Create())
            {
                var endpoints = conn.GetEndPoints();
                var servers = endpoints.Select(e => conn.GetServer(e)).ToList();

                var key = Me();
                const string value = "abc";
                var db = conn.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);
                db.StringSet(key, value, flags: CommandFlags.FireAndForget);
                servers.First().Ping();
                var config = servers.First().ClusterConfiguration;
                Assert.NotNull(config);
                int slot = conn.HashSlot(key);
                var rightMasterNode = config.GetBySlot(key);
                Assert.NotNull(rightMasterNode);
                Log("Right Master: {0} {1}", rightMasterNode.EndPoint, rightMasterNode.NodeId);

                string a = StringGet(conn.GetServer(rightMasterNode.EndPoint), key);
                Assert.Equal(value, a); // right master

                var node = config.Nodes.FirstOrDefault(x => !x.IsReplica && x.NodeId != rightMasterNode.NodeId);
                Assert.NotNull(node);
                Log("Using Master: {0}", node.EndPoint, node.NodeId);
                {
                    string b = StringGet(conn.GetServer(node.EndPoint), key);
                    Assert.Equal(value, b); // wrong master, allow redirect

                    var ex = Assert.Throws<RedisServerException>(() => StringGet(conn.GetServer(node.EndPoint), key, CommandFlags.NoRedirect));
                    Assert.StartsWith($"Key has MOVED to Endpoint {rightMasterNode.EndPoint} and hashslot {slot}", ex.Message);
                }

                node = config.Nodes.FirstOrDefault(x => x.IsReplica && x.ParentNodeId == rightMasterNode.NodeId);
                Assert.NotNull(node);
                {
                    string d = StringGet(conn.GetServer(node.EndPoint), key);
                    Assert.Equal(value, d); // right replica
                }

                node = config.Nodes.FirstOrDefault(x => x.IsReplica && x.ParentNodeId != rightMasterNode.NodeId);
                Assert.NotNull(node);
                {
                    string e = StringGet(conn.GetServer(node.EndPoint), key);
                    Assert.Equal(value, e); // wrong replica, allow redirect

                    var ex = Assert.Throws<RedisServerException>(() => StringGet(conn.GetServer(node.EndPoint), key, CommandFlags.NoRedirect));
                    Assert.StartsWith($"Key has MOVED to Endpoint {rightMasterNode.EndPoint} and hashslot {slot}", ex.Message);
                }
            }
        }

        [Fact]
        public void TransactionWithMultiServerKeys()
        {
            var ex = Assert.Throws<RedisCommandException>(() =>
            {
                using (var muxer = Create())
                {
                    // connect
                    var cluster = muxer.GetDatabase();
                    var anyServer = muxer.GetServer(muxer.GetEndPoints()[0]);
                    anyServer.Ping();
                    Assert.Equal(ServerType.Cluster, anyServer.ServerType);
                    var config = anyServer.ClusterConfiguration;
                    Assert.NotNull(config);

                    // invent 2 keys that we believe are served by different nodes
                    string x = Guid.NewGuid().ToString(), y;
                    var xNode = config.GetBySlot(x);
                    int abort = 1000;
                    do
                    {
                        y = Guid.NewGuid().ToString();
                    } while (--abort > 0 && config.GetBySlot(y) == xNode);
                    if (abort == 0) Skip.Inconclusive("failed to find a different node to use");
                    var yNode = config.GetBySlot(y);
                    Log("x={0}, served by {1}", x, xNode.NodeId);
                    Log("y={0}, served by {1}", y, yNode.NodeId);
                    Assert.NotEqual(xNode.NodeId, yNode.NodeId);

                    // wipe those keys
                    cluster.KeyDelete(x, CommandFlags.FireAndForget);
                    cluster.KeyDelete(y, CommandFlags.FireAndForget);

                    // create a transaction that attempts to assign both keys
                    var tran = cluster.CreateTransaction();
                    tran.AddCondition(Condition.KeyNotExists(x));
                    tran.AddCondition(Condition.KeyNotExists(y));
                    _ = tran.StringSetAsync(x, "x-val");
                    _ = tran.StringSetAsync(y, "y-val");
                    tran.Execute();

                    Assert.True(false, "Expected single-slot rules to apply");
                    // the rest no longer applies while we are following single-slot rules

                    //// check that everything was aborted
                    //Assert.False(success, "tran aborted");
                    //Assert.True(setX.IsCanceled, "set x cancelled");
                    //Assert.True(setY.IsCanceled, "set y cancelled");
                    //var existsX = cluster.KeyExistsAsync(x);
                    //var existsY = cluster.KeyExistsAsync(y);
                    //Assert.False(cluster.Wait(existsX), "x exists");
                    //Assert.False(cluster.Wait(existsY), "y exists");
                }
            });
            Assert.Equal("Multi-key operations must involve a single slot; keys can use 'hash tags' to help this, i.e. '{/users/12345}/account' and '{/users/12345}/contacts' will always be in the same slot", ex.Message);
        }

        [Fact]
        public void TransactionWithSameServerKeys()
        {
            var ex = Assert.Throws<RedisCommandException>(() =>
            {
                using (var muxer = Create())
                {
                    // connect
                    var cluster = muxer.GetDatabase();
                    var anyServer = muxer.GetServer(muxer.GetEndPoints()[0]);
                    anyServer.Ping();
                    var config = anyServer.ClusterConfiguration;
                    Assert.NotNull(config);

                    // invent 2 keys that we believe are served by different nodes
                    string x = Guid.NewGuid().ToString(), y;
                    var xNode = config.GetBySlot(x);
                    int abort = 1000;
                    do
                    {
                        y = Guid.NewGuid().ToString();
                    } while (--abort > 0 && config.GetBySlot(y) != xNode);
                    if (abort == 0) Skip.Inconclusive("failed to find a key with the same node to use");
                    var yNode = config.GetBySlot(y);
                    Log("x={0}, served by {1}", x, xNode.NodeId);
                    Log("y={0}, served by {1}", y, yNode.NodeId);
                    Assert.Equal(xNode.NodeId, yNode.NodeId);

                    // wipe those keys
                    cluster.KeyDelete(x, CommandFlags.FireAndForget);
                    cluster.KeyDelete(y, CommandFlags.FireAndForget);

                    // create a transaction that attempts to assign both keys
                    var tran = cluster.CreateTransaction();
                    tran.AddCondition(Condition.KeyNotExists(x));
                    tran.AddCondition(Condition.KeyNotExists(y));
                    _ = tran.StringSetAsync(x, "x-val");
                    _ = tran.StringSetAsync(y, "y-val");
                    tran.Execute();

                    Assert.True(false, "Expected single-slot rules to apply");
                    // the rest no longer applies while we are following single-slot rules

                    //// check that everything was aborted
                    //Assert.True(success, "tran aborted");
                    //Assert.False(setX.IsCanceled, "set x cancelled");
                    //Assert.False(setY.IsCanceled, "set y cancelled");
                    //var existsX = cluster.KeyExistsAsync(x);
                    //var existsY = cluster.KeyExistsAsync(y);
                    //Assert.True(cluster.Wait(existsX), "x exists");
                    //Assert.True(cluster.Wait(existsY), "y exists");
                }
            });
            Assert.Equal("Multi-key operations must involve a single slot; keys can use 'hash tags' to help this, i.e. '{/users/12345}/account' and '{/users/12345}/contacts' will always be in the same slot", ex.Message);
        }

        [Fact]
        public void TransactionWithSameSlotKeys()
        {
            using (var muxer = Create())
            {
                // connect
                var cluster = muxer.GetDatabase();
                var anyServer = muxer.GetServer(muxer.GetEndPoints()[0]);
                anyServer.Ping();
                var config = anyServer.ClusterConfiguration;
                Assert.NotNull(config);

                // invent 2 keys that we believe are in the same slot
                var guid = Guid.NewGuid().ToString();
                string x = "/{" + guid + "}/foo", y = "/{" + guid + "}/bar";

                Assert.Equal(muxer.HashSlot(x), muxer.HashSlot(y));
                var xNode = config.GetBySlot(x);
                var yNode = config.GetBySlot(y);
                Log("x={0}, served by {1}", x, xNode.NodeId);
                Log("y={0}, served by {1}", y, yNode.NodeId);
                Assert.Equal(xNode.NodeId, yNode.NodeId);

                // wipe those keys
                cluster.KeyDelete(x, CommandFlags.FireAndForget);
                cluster.KeyDelete(y, CommandFlags.FireAndForget);

                // create a transaction that attempts to assign both keys
                var tran = cluster.CreateTransaction();
                tran.AddCondition(Condition.KeyNotExists(x));
                tran.AddCondition(Condition.KeyNotExists(y));
                var setX = tran.StringSetAsync(x, "x-val");
                var setY = tran.StringSetAsync(y, "y-val");
                bool success = tran.Execute();

                // check that everything was aborted
                Assert.True(success, "tran aborted");
                Assert.False(setX.IsCanceled, "set x cancelled");
                Assert.False(setY.IsCanceled, "set y cancelled");
                var existsX = cluster.KeyExistsAsync(x);
                var existsY = cluster.KeyExistsAsync(y);
                Assert.True(cluster.Wait(existsX), "x exists");
                Assert.True(cluster.Wait(existsY), "y exists");
            }
        }

        [Theory]
        [InlineData(null, 10)]
        [InlineData(null, 100)]
        [InlineData("abc", 10)]
        [InlineData("abc", 100)]

        public void Keys(string pattern, int pageSize)
        {
            using (var conn = Create(allowAdmin: true))
            {
                _ = conn.GetDatabase();
                var server = conn.GetEndPoints().Select(x => conn.GetServer(x)).First(x => !x.IsReplica);
                server.FlushAllDatabases();
                try
                {
                    Assert.False(server.Keys(pattern: pattern, pageSize: pageSize).Any());
                    Log("Complete: '{0}' / {1}", pattern, pageSize);
                }
                catch
                {
                    Log("Failed: '{0}' / {1}", pattern, pageSize);
                    throw;
                }
            }
        }

        [Theory]
        [InlineData("", 0)]
        [InlineData("abc", 7638)]
        [InlineData("{abc}", 7638)]
        [InlineData("abcdef", 15101)]
        [InlineData("abc{abc}def", 7638)]
        [InlineData("c", 7365)]
        [InlineData("g", 7233)]
        [InlineData("d", 11298)]

        [InlineData("user1000", 3443)]
        [InlineData("{user1000}", 3443)]
        [InlineData("abc{user1000}", 3443)]
        [InlineData("abc{user1000}def", 3443)]
        [InlineData("{user1000}.following", 3443)]
        [InlineData("{user1000}.followers", 3443)]

        [InlineData("foo{}{bar}", 8363)]

        [InlineData("foo{{bar}}zap", 4015)]
        [InlineData("{bar", 4015)]

        [InlineData("foo{bar}{zap}", 5061)]
        [InlineData("bar", 5061)]

        public void HashSlots(string key, int slot)
        {
            using (var muxer = Create(connectTimeout: 5000))
            {
                Assert.Equal(slot, muxer.HashSlot(key));
            }
        }

        [Fact]
        public void SScan()
        {
            using (var conn = Create())
            {
                RedisKey key = "a";
                var db = conn.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);

                int totalUnfiltered = 0, totalFiltered = 0;
                for (int i = 0; i < 1000; i++)
                {
                    db.SetAdd(key, i, CommandFlags.FireAndForget);
                    totalUnfiltered += i;
                    if (i.ToString().Contains("3")) totalFiltered += i;
                }
                var unfilteredActual = db.SetScan(key).Select(x => (int)x).Sum();
                var filteredActual = db.SetScan(key, "*3*").Select(x => (int)x).Sum();
                Assert.Equal(totalUnfiltered, unfilteredActual);
                Assert.Equal(totalFiltered, filteredActual);
            }
        }

        [Fact]
        public void GetConfig()
        {
            using (var sw = new StringWriter())
            using (var muxer = Create(allowAdmin: true, log: sw))
            {
                var endpoints = muxer.GetEndPoints();
                var server = muxer.GetServer(endpoints[0]);
                var nodes = server.ClusterNodes();

                Log("Endpoints:");
                foreach (var endpoint in endpoints)
                {
                    Log(endpoint.ToString());
                }
                Log("Nodes:");
                foreach (var node in nodes.Nodes.OrderBy(x => x))
                {
                    Log(node.ToString());
                }
                Log(sw.ToString());

                Assert.Equal(TestConfig.Current.ClusterServerCount, endpoints.Length);
                Assert.Equal(TestConfig.Current.ClusterServerCount, nodes.Nodes.Count);
            }
        }

        [Fact]
        public void AccessRandomKeys()
        {
            using (var conn = Create(allowAdmin: true))
            {
                var cluster = conn.GetDatabase();
                int slotMovedCount = 0;
                conn.HashSlotMoved += (s, a) =>
                {
                    Log("{0} moved from {1} to {2}", a.HashSlot, Describe(a.OldEndPoint), Describe(a.NewEndPoint));
                    Interlocked.Increment(ref slotMovedCount);
                };
                var pairs = new Dictionary<string, string>();
                const int COUNT = 500;
                int index = 0;

                var servers = conn.GetEndPoints().Select(x => conn.GetServer(x)).ToList();
                foreach (var server in servers)
                {
                    if (!server.IsReplica)
                    {
                        server.Ping();
                        server.FlushAllDatabases();
                    }
                }

                for (int i = 0; i < COUNT; i++)
                {
                    var key = Guid.NewGuid().ToString();
                    var value = Guid.NewGuid().ToString();
                    pairs.Add(key, value);
                    cluster.StringSet(key, value, flags: CommandFlags.FireAndForget);
                }

                var expected = new string[COUNT];
                var actual = new Task<RedisValue>[COUNT];
                index = 0;
                foreach (var pair in pairs)
                {
                    expected[index] = pair.Value;
                    actual[index] = cluster.StringGetAsync(pair.Key);
                    index++;
                }
                cluster.WaitAll(actual);
                for (int i = 0; i < COUNT; i++)
                {
                    Assert.Equal(expected[i], actual[i].Result);
                }

                int total = 0;
                Parallel.ForEach(servers, server =>
                {
                    if (!server.IsReplica)
                    {
                        int count = server.Keys(pageSize: 100).Count();
                        Log("{0} has {1} keys", server.EndPoint, count);
                        Interlocked.Add(ref total, count);
                    }
                });

                foreach (var server in servers)
                {
                    var counters = server.GetCounters();
                    Log(counters.ToString());
                }
                int final = Interlocked.CompareExchange(ref total, 0, 0);
                Assert.Equal(COUNT, final);
                Assert.Equal(0, Interlocked.CompareExchange(ref slotMovedCount, 0, 0));
            }
        }

        [Theory]
        [InlineData(CommandFlags.DemandMaster, false)]
        [InlineData(CommandFlags.DemandReplica, true)]
        [InlineData(CommandFlags.PreferMaster, false)]
        [InlineData(CommandFlags.PreferReplica, true)]
        public void GetFromRightNodeBasedOnFlags(CommandFlags flags, bool isReplica)
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var db = muxer.GetDatabase();
                for (int i = 0; i < 1000; i++)
                {
                    var key = Guid.NewGuid().ToString();
                    var endpoint = db.IdentifyEndpoint(key, flags);
                    var server = muxer.GetServer(endpoint);
                    Assert.Equal(isReplica, server.IsReplica);
                }
            }
        }

        private static string Describe(EndPoint endpoint) => endpoint?.ToString() ?? "(unknown)";

        [Fact]
        public void SimpleProfiling()
        {
            using (var conn = Create())
            {
                var profiler = new ProfilingSession();
                var key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key, CommandFlags.FireAndForget);

                conn.RegisterProfiler(() => profiler);
                db.StringSet(key, "world");
                var val = db.StringGet(key);
                Assert.Equal("world", val);

                var msgs = profiler.FinishProfiling().Where(m => m.Command == "GET" || m.Command == "SET").ToList();
                foreach (var msg in msgs)
                {
                    Log("Profiler Message: " + Environment.NewLine + msg);
                }
                Log("Checking GET...");
                Assert.Contains(msgs, m => m.Command == "GET");
                Log("Checking SET...");
                Assert.Contains(msgs, m => m.Command == "SET");
                Assert.Equal(2, msgs.Count(m => m.RetransmissionOf is null));

                var arr = msgs.Where(m => m.RetransmissionOf is null).ToArray();
                Assert.Equal("SET", arr[0].Command);
                Assert.Equal("GET", arr[1].Command);
            }
        }

        [Fact]
        public void MultiKeyQueryFails()
        {
            var keys = InventKeys(); // note the rules expected of this data are enforced in GroupedQueriesWork

            using (var conn = Create())
            {
                var ex = Assert.Throws<RedisCommandException>(() => conn.GetDatabase(0).StringGet(keys));
                Assert.Contains("Multi-key operations must involve a single slot", ex.Message);
            }
        }

        private static RedisKey[] InventKeys()
        {
            RedisKey[] keys = new RedisKey[256];
            Random rand = new Random(12324);
            string InventString()
            {
                const string alphabet = "abcdefghijklmnopqrstuvwxyz012345689";
                var len = rand.Next(10, 50);
                char[] chars = new char[len];
                for (int i = 0; i < len; i++)
                    chars[i] = alphabet[rand.Next(alphabet.Length)];
                return new string(chars);
            }

            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = InventString();
            }
            return keys;
        }

        [Fact]
        public void GroupedQueriesWork()
        {
            // note it doesn't matter that the data doesn't exist for this;
            // the point here is that the entire thing *won't work* otherwise,
            // as per above test

            var keys = InventKeys();
            using (var conn = Create())
            {
                var grouped = keys.GroupBy(key => conn.GetHashSlot(key)).ToList();
                Assert.True(grouped.Count > 1); // check not all a super-group
                Assert.True(grouped.Count < keys.Length); // check not all singleton groups
                Assert.Equal(keys.Length, grouped.Sum(x => x.Count())); // check they're all there
                Assert.Contains(grouped, x => x.Count() > 1); // check at least one group with multiple items (redundant from above, but... meh)

                Log($"{grouped.Count} groups, min: {grouped.Min(x => x.Count())}, max: {grouped.Max(x => x.Count())}, avg: {grouped.Average(x => x.Count())}");

                var db = conn.GetDatabase(0);
                var all = grouped.SelectMany(grp => {
                    var grpKeys = grp.ToArray();
                    var values = db.StringGet(grpKeys);
                    return grpKeys.Zip(values, (key, val) => new { key, val });
                }).ToDictionary(x => x.key, x => x.val);

                Assert.Equal(keys.Length, all.Count);
            }
        }

        [Fact]
        public void MovedProfiling()
        {
            var Key = Me();
            const string Value = "redirected-value";

            var profiler = new Profiling.PerThreadProfiler();

            using (var conn = Create())
            {
                conn.RegisterProfiler(profiler.GetSession);

                var endpoints = conn.GetEndPoints();
                var servers = endpoints.Select(e => conn.GetServer(e));

                var db = conn.GetDatabase();
                db.KeyDelete(Key);
                db.StringSet(Key, Value);
                var config = servers.First().ClusterConfiguration;
                Assert.NotNull(config);

                //int slot = conn.HashSlot(Key);
                var rightMasterNode = config.GetBySlot(Key);
                Assert.NotNull(rightMasterNode);

                string a = (string)conn.GetServer(rightMasterNode.EndPoint).Execute("GET", Key);
                Assert.Equal(Value, a); // right master

                var wrongMasterNode = config.Nodes.FirstOrDefault(x => !x.IsReplica && x.NodeId != rightMasterNode.NodeId);
                Assert.NotNull(wrongMasterNode);

                string b = (string)conn.GetServer(wrongMasterNode.EndPoint).Execute("GET", Key);
                Assert.Equal(Value, b); // wrong master, allow redirect

                var msgs = profiler.GetSession().FinishProfiling().ToList();

                // verify that things actually got recorded properly, and the retransmission profilings are connected as expected
                {
                    // expect 1 DEL, 1 SET, 1 GET (to right master), 1 GET (to wrong master) that was responded to by an ASK, and 1 GET (to right master or a replica of it)
                    Assert.Equal(5, msgs.Count);
                    Assert.Equal(1, msgs.Count(c => c.Command == "DEL" || c.Command == "UNLINK"));
                    Assert.Equal(1, msgs.Count(c => c.Command == "SET"));
                    Assert.Equal(3, msgs.Count(c => c.Command == "GET"));

                    var toRightMasterNotRetransmission = msgs.Where(m => m.Command == "GET" && m.EndPoint.Equals(rightMasterNode.EndPoint) && m.RetransmissionOf == null);
                    Assert.Single(toRightMasterNotRetransmission);

                    var toWrongMasterWithoutRetransmission = msgs.Where(m => m.Command == "GET" && m.EndPoint.Equals(wrongMasterNode.EndPoint) && m.RetransmissionOf == null).ToList();
                    Assert.Single(toWrongMasterWithoutRetransmission);

                    var toRightMasterOrReplicaAsRetransmission = msgs.Where(m => m.Command == "GET" && (m.EndPoint.Equals(rightMasterNode.EndPoint) || rightMasterNode.Children.Any(c => m.EndPoint.Equals(c.EndPoint))) && m.RetransmissionOf != null).ToList();
                    Assert.Single(toRightMasterOrReplicaAsRetransmission);

                    var originalWrongMaster = toWrongMasterWithoutRetransmission.Single();
                    var retransmissionToRight = toRightMasterOrReplicaAsRetransmission.Single();

                    Assert.True(ReferenceEquals(originalWrongMaster, retransmissionToRight.RetransmissionOf));
                }

                foreach (var msg in msgs)
                {
                    Assert.True(msg.CommandCreated != default(DateTime));
                    Assert.True(msg.CreationToEnqueued > TimeSpan.Zero);
                    Assert.True(msg.EnqueuedToSending > TimeSpan.Zero);
                    Assert.True(msg.SentToResponse > TimeSpan.Zero);
                    Assert.True(msg.ResponseToCompletion >= TimeSpan.Zero); // this can be immeasurably fast
                    Assert.True(msg.ElapsedTime > TimeSpan.Zero);

                    if (msg.RetransmissionOf != null)
                    {
                        // imprecision of DateTime.UtcNow makes this pretty approximate
                        Assert.True(msg.RetransmissionOf.CommandCreated <= msg.CommandCreated);
                        Assert.Equal(RetransmissionReasonType.Moved, msg.RetransmissionReason.Value);
                    }
                    else
                    {
                        Assert.False(msg.RetransmissionReason.HasValue);
                    }
                }
            }
        }
    }
}
