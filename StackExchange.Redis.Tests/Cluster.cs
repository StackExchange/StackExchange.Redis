using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
            for (int i = 0; i < 10; i++)
            {
                using (var muxer = Create(failMessage: i + ": "))
                {
                    foreach (var ep in muxer.GetEndPoints())
                    {
                        var srv = muxer.GetServer(ep);
                        var counters = srv.GetCounters();
                        Output.WriteLine(i + "; interactive, " + ep);
                        Assert.Equal(1, counters.Interactive.SocketCount);
                        Output.WriteLine(i + "; subscription, " + ep);
                        Assert.Equal(1, counters.Subscription.SocketCount);
                    }
                }
            }
        }

        [Fact]
        public void CanGetTotalStats()
        {
            using (var muxer = Create())
            {
                var counters = muxer.GetCounters();
                Output.WriteLine(counters.ToString());
            }
        }

        [Fact]
        public void Connect()
        {
            using (var muxer = Create())
            {
                var endpoints = muxer.GetEndPoints();

                Assert.Equal(TestConfig.Current.ClusterServerCount, endpoints.Length);
                var expectedPorts = new HashSet<int>(Enumerable.Range(TestConfig.Current.ClusterStartPort, TestConfig.Current.ClusterServerCount));
                int masters = 0, slaves = 0;
                var failed = new List<EndPoint>();
                foreach (var endpoint in endpoints)
                {
                    var server = muxer.GetServer(endpoint);
                    if (!server.IsConnected)
                    {
                        failed.Add(endpoint);
                    }
                    Output.WriteLine("endpoint:" + endpoint);
                    Assert.Equal(endpoint, server.EndPoint);

                    Output.WriteLine("endpoint-type:" + endpoint);
                    Assert.IsType<IPEndPoint>(endpoint);

                    Output.WriteLine("port:" + endpoint);
                    Assert.True(expectedPorts.Remove(((IPEndPoint)endpoint).Port));

                    Output.WriteLine("server-type:" + endpoint);
                    Assert.Equal(ServerType.Cluster, server.ServerType);

                    if (server.IsSlave) slaves++;
                    else masters++;
                }
                if (failed.Count != 0)
                {
                    Output.WriteLine("{0} failues", failed.Count);
                    foreach (var fail in failed)
                    {
                        Output.WriteLine(fail.ToString());
                    }
                    Assert.True(false, "not all servers connected");
                }

                Assert.Equal(TestConfig.Current.ClusterServerCount / 2, slaves);
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
            using (var conn = Create())
            {
                var endpoints = conn.GetEndPoints();
                var servers = endpoints.Select(e => conn.GetServer(e));

                var key = Me();
                const string value = "abc";
                var db = conn.GetDatabase();
                db.KeyDelete(key);
                db.StringSet(key, value);
                servers.First().Ping();
                var config = servers.First().ClusterConfiguration;
                Assert.NotNull(config);
                int slot = conn.HashSlot(key);
                var rightMasterNode = config.GetBySlot(key);
                Assert.NotNull(rightMasterNode);
                Output.WriteLine("Right Master: {0} {1}", rightMasterNode.EndPoint, rightMasterNode.NodeId);

#if DEBUG
                string a = conn.GetServer(rightMasterNode.EndPoint).StringGet(db.Database, key);
                Assert.Equal(value, a); // right master

                var node = config.Nodes.FirstOrDefault(x => !x.IsSlave && x.NodeId != rightMasterNode.NodeId);
                Assert.NotNull(node);
                Output.WriteLine("Using Master: {0}", node.EndPoint, node.NodeId);
                if (node != null)
                {
                    string b = conn.GetServer(node.EndPoint).StringGet(db.Database, key);
                    Assert.Equal(value, b); // wrong master, allow redirect

                    var ex = Assert.Throws<RedisServerException>(() => conn.GetServer(node.EndPoint).StringGet(db.Database, key, CommandFlags.NoRedirect));
                    Assert.StartsWith($"Key has MOVED from Endpoint {rightMasterNode.EndPoint} and hashslot {slot}", ex.Message);
                }

                node = config.Nodes.FirstOrDefault(x => x.IsSlave && x.ParentNodeId == rightMasterNode.NodeId);
                Assert.NotNull(node);
                if (node != null)
                {
                    string d = conn.GetServer(node.EndPoint).StringGet(db.Database, key);
                    Assert.Equal(value, d); // right slave
                }

                node = config.Nodes.FirstOrDefault(x => x.IsSlave && x.ParentNodeId != rightMasterNode.NodeId);
                Assert.NotNull(node);
                if (node != null)
                {
                    string e = conn.GetServer(node.EndPoint).StringGet(db.Database, key);
                    Assert.Equal(value, e); // wrong slave, allow redirect

                    var ex = Assert.Throws<RedisServerException>(() => conn.GetServer(node.EndPoint).StringGet(db.Database, key, CommandFlags.NoRedirect));
                    Assert.StartsWith($"Key has MOVED from Endpoint {rightMasterNode.EndPoint} and hashslot {slot}", ex.Message);
                }
#endif

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
                    Output.WriteLine("x={0}, served by {1}", x, xNode.NodeId);
                    Output.WriteLine("y={0}, served by {1}", y, yNode.NodeId);
                    Assert.NotEqual(xNode.NodeId, yNode.NodeId);

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
                    Output.WriteLine("x={0}, served by {1}", x, xNode.NodeId);
                    Output.WriteLine("y={0}, served by {1}", y, yNode.NodeId);
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
                Output.WriteLine("x={0}, served by {1}", x, xNode.NodeId);
                Output.WriteLine("y={0}, served by {1}", y, yNode.NodeId);
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
                var cluster = conn.GetDatabase();
                var server = conn.GetEndPoints().Select(x => conn.GetServer(x)).First(x => !x.IsSlave);
                server.FlushAllDatabases();
                try
                {
                    Assert.False(server.Keys(pattern: pattern, pageSize: pageSize).Any());
                    Output.WriteLine("Complete: '{0}' / {1}", pattern, pageSize);
                }
                catch
                {
                    Output.WriteLine("Failed: '{0}' / {1}", pattern, pageSize);
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
            using (var muxer = Create(connectTimeout: 500, pause: false))
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
                db.KeyDelete(key);

                int totalUnfiltered = 0, totalFiltered = 0;
                for (int i = 0; i < 1000; i++)
                {
                    db.SetAdd(key, i);
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
            using (var muxer = Create(allowAdmin: true))
            {
                var endpoints = muxer.GetEndPoints();
                var server = muxer.GetServer(endpoints[0]);
                var nodes = server.ClusterNodes();

                Output.WriteLine("Endpoints:");
                foreach (var endpoint in endpoints)
                {
                    Output.WriteLine(endpoint.ToString());
                }
                Output.WriteLine("Nodes:");
                foreach (var node in nodes.Nodes.OrderBy(x => x))
                {
                    Output.WriteLine(node.ToString());
                }
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
                    Output.WriteLine("{0} moved from {1} to {2}", a.HashSlot, Describe(a.OldEndPoint), Describe(a.NewEndPoint));
                    Interlocked.Increment(ref slotMovedCount);
                };
                var pairs = new Dictionary<string, string>();
                const int COUNT = 500;
                Task[] send = new Task[COUNT];
                int index = 0;

                var servers = conn.GetEndPoints().Select(x => conn.GetServer(x));
                foreach (var server in servers)
                {
                    if (!server.IsSlave)
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
                    send[index++] = cluster.StringSetAsync(key, value);
                }
                conn.WaitAll(send);

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
                    Assert.Equal(expected[i], (string)actual[i].Result);
                }

                int total = 0;
                Parallel.ForEach(servers, server =>
                {
                    if (!server.IsSlave)
                    {
                        int count = server.Keys(pageSize: 100).Count();
                        Output.WriteLine("{0} has {1} keys", server.EndPoint, count);
                        Interlocked.Add(ref total, count);
                    }
                });

                foreach (var server in servers)
                {
                    var counters = server.GetCounters();
                    Output.WriteLine(counters.ToString());
                }
                int final = Interlocked.CompareExchange(ref total, 0, 0);
                Assert.Equal(COUNT, final);
                Assert.Equal(0, Interlocked.CompareExchange(ref slotMovedCount, 0, 0));
            }
        }

        [Theory]
        [InlineData(CommandFlags.DemandMaster, false)]
        [InlineData(CommandFlags.DemandSlave, true)]
        [InlineData(CommandFlags.PreferMaster, false)]
        [InlineData(CommandFlags.PreferSlave, true)]
        public void GetFromRightNodeBasedOnFlags(CommandFlags flags, bool isSlave)
        {
            using (var muxer = Create(allowAdmin: true))
            {
                var db = muxer.GetDatabase();
                for (int i = 0; i < 1000; i++)
                {
                    var key = Guid.NewGuid().ToString();
                    var endpoint = db.IdentifyEndpoint(key, flags);
                    var server = muxer.GetServer(endpoint);
                    Output.WriteLine("Comparing: key");
                    Assert.Equal(isSlave, server.IsSlave);
                }
            }
        }

        private static string Describe(EndPoint endpoint) => endpoint?.ToString() ?? "(unknown)";

        private class TestProfiler : IProfiler
        {
            public object MyContext = new object();
            public object GetContext() => MyContext;
        }

        [Fact]
        public void SimpleProfiling()
        {
            using (var conn = Create())
            {
                var profiler = new TestProfiler();

                conn.RegisterProfiler(profiler);
                conn.BeginProfiling(profiler.MyContext);
                var db = conn.GetDatabase();
                db.StringSet("hello", "world");
                var val = db.StringGet("hello");
                Assert.Equal("world", val);

                var msgs = conn.FinishProfiling(profiler.MyContext);
                Assert.Equal(2, msgs.Count());
                Assert.Contains(msgs, m => m.Command == "GET");
                Assert.Contains(msgs, m => m.Command == "SET");
            }
        }

#if DEBUG
        [Fact]
        public void MovedProfiling()
        {
            const string Key = "redirected-key";
            const string Value = "redirected-value";

            var profiler = new TestProfiler();

            using (var conn = Create())
            {
                conn.RegisterProfiler(profiler);

                var endpoints = conn.GetEndPoints();
                var servers = endpoints.Select(e => conn.GetServer(e));

                conn.BeginProfiling(profiler.MyContext);
                var db = conn.GetDatabase();
                db.KeyDelete(Key);
                db.StringSet(Key, Value);
                var config = servers.First().ClusterConfiguration;
                Assert.NotNull(config);

                int slot = conn.HashSlot(Key);
                var rightMasterNode = config.GetBySlot(Key);
                Assert.NotNull(rightMasterNode);

                string a = conn.GetServer(rightMasterNode.EndPoint).StringGet(db.Database, Key);
                Assert.Equal(Value, a); // right master

                var wrongMasterNode = config.Nodes.FirstOrDefault(x => !x.IsSlave && x.NodeId != rightMasterNode.NodeId);
                Assert.NotNull(wrongMasterNode);

                string b = conn.GetServer(wrongMasterNode.EndPoint).StringGet(db.Database, Key);
                Assert.Equal(Value, b); // wrong master, allow redirect

                var msgs = conn.FinishProfiling(profiler.MyContext).ToList();

                // verify that things actually got recorded properly, and the retransmission profilings are connected as expected
                {
                    // expect 1 DEL, 1 SET, 1 GET (to right master), 1 GET (to wrong master) that was responded to by an ASK, and 1 GET (to right master or a slave of it)
                    Assert.Equal(5, msgs.Count);
                    Assert.Equal(1, msgs.Count(c => c.Command == "DEL"));
                    Assert.Equal(1, msgs.Count(c => c.Command == "SET"));
                    Assert.Equal(3, msgs.Count(c => c.Command == "GET"));

                    var toRightMasterNotRetransmission = msgs.Where(m => m.Command == "GET" && m.EndPoint.Equals(rightMasterNode.EndPoint) && m.RetransmissionOf == null);
                    Assert.Single(toRightMasterNotRetransmission);

                    var toWrongMasterWithoutRetransmission = msgs.Where(m => m.Command == "GET" && m.EndPoint.Equals(wrongMasterNode.EndPoint) && m.RetransmissionOf == null);
                    Assert.Single(toWrongMasterWithoutRetransmission);

                    var toRightMasterOrSlaveAsRetransmission = msgs.Where(m => m.Command == "GET" && (m.EndPoint.Equals(rightMasterNode.EndPoint) || rightMasterNode.Children.Any(c => m.EndPoint.Equals(c.EndPoint))) && m.RetransmissionOf != null);
                    Assert.Single(toRightMasterOrSlaveAsRetransmission);

                    var originalWrongMaster = toWrongMasterWithoutRetransmission.Single();
                    var retransmissionToRight = toRightMasterOrSlaveAsRetransmission.Single();

                    Assert.True(object.ReferenceEquals(originalWrongMaster, retransmissionToRight.RetransmissionOf));
                }

                foreach (var msg in msgs)
                {
                    Assert.True(msg.CommandCreated != default(DateTime));
                    Assert.True(msg.CreationToEnqueued > TimeSpan.Zero);
                    Assert.True(msg.EnqueuedToSending > TimeSpan.Zero);
                    Assert.True(msg.SentToResponse > TimeSpan.Zero);
                    Assert.True(msg.ResponseToCompletion > TimeSpan.Zero);
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
#endif
    }
}
