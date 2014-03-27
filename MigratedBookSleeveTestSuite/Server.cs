//using System.Linq;
//using BookSleeve;
//using NUnit.Framework;
//using System.Threading;
//using System;
//using System.Threading.Tasks;
//using System.Collections.Generic;
//using System.Diagnostics;

//namespace Tests
//{
//    [TestFixture]
//    public class Server // http://redis.io/commands#server
//    {
//        [Test]
//        public void TestGetConfigAll()
//        {
//            using (var db = Config.GetUnsecuredConnection())
//            {
//                var pairs = db.Wait(db.Server.GetConfig("*"));
//                Assert.Greater(1, 0); // I always get double-check which arg is which
//                Assert.Greater(pairs.Count, 0);
//            }
//        }

//        [Test]
//        public void BGSaveAndLastSave()
//        {
//            using(var db = Config.GetUnsecuredConnection(allowAdmin: true))
//            {
//                var oldWhen = db.Server.GetLastSaveTime();
//                db.Wait(db.Server.SaveDatabase(foreground: false));

//                bool saved = false;
//                for(int i = 0; i < 50; i++)
//                {
//                    var newWhen = db.Server.GetLastSaveTime();
//                    db.Wait(newWhen);
//                    if(newWhen.Result > oldWhen.Result)
//                    {
//                        saved = true;
//                        break;
//                    }
//                    Console.WriteLine("waiting...");
//                    Thread.Sleep(200);
//                }
//                Assert.IsTrue(saved);
//            }
//        }

//        [Test]
//        [TestCase(true)]
//        [TestCase(false)]
//        public void Slowlog(bool remote)
//        {
//            using(var db = remote ? Config.GetRemoteConnection(allowAdmin: true) : Config.GetUnsecuredConnection(allowAdmin: true))
//            {
//                var oldWhen = db.Wait(db.Server.Time());
//                db.Server.FlushAll();
//                db.Server.ResetSlowCommands();
//                for (int i = 0; i < 100000; i++)
//                {
//                    db.Strings.Set(1, Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
//                }
//                var settings = db.Wait(db.Server.GetConfig("slowlog-*"));
//                var count = int.Parse(settings["slowlog-max-len"]);
//                var threshold = int.Parse(settings["slowlog-log-slower-than"]);

//                var ping = db.Server.Ping();
//                Assert.IsTrue(ping.Wait(10000)); // wait until inserted
//                db.Server.SaveDatabase(foreground: true);
//                var keys = db.Wait(db.Keys.Find(1, "*"));
//                var slow = db.Wait(db.Server.GetSlowCommands());
//                var slow2 = db.Wait(db.Server.GetSlowCommands(slow.Length)); // different command syntax
//                Assert.AreEqual(slow.Length, slow2.Length);

                

//                foreach(var cmd in slow)
//                {
//                    Console.WriteLine(cmd.UniqueId + ": " + cmd.Duration.Milliseconds + "ms; " +
//                        string.Join(", ", cmd.Arguments), cmd.GetHelpUrl());
//                    Assert.IsTrue(cmd.Time > oldWhen && cmd.Time < oldWhen.AddMinutes(1));
//                }

//                Assert.AreEqual(2, slow.Length);

//                Assert.AreEqual(2, slow[0].Arguments.Length);
//                Assert.AreEqual("KEYS", slow[0].Arguments[0]);
//                Assert.AreEqual("*", slow[0].Arguments[1]);
//                Assert.AreEqual("http://redis.io/commands/keys", slow[0].GetHelpUrl());

//                Assert.AreEqual(1, slow[1].Arguments.Length);
//                Assert.AreEqual("SAVE", slow[1].Arguments[0]);
//                Assert.AreEqual("http://redis.io/commands/save", slow[1].GetHelpUrl());
//            }
//        }

//        [Test]
//        public void TestTime()
//        {
//            using (var db = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                Assert.IsNotNull(db.Features); // we waited, after all
//                if (db.Features.Time)
//                {
//                    var local = DateTime.UtcNow;
//                    var server = db.Wait(db.Server.Time());

//                    Assert.True(Math.Abs((local - server).TotalMilliseconds) < 10);

//                }
//            }
//        }

//        [Test]
//        public void TestTimeWithExplicitVersion()
//        {
//            using (var db = Config.GetUnsecuredConnection(open: false))
//            {
//                db.SetServerVersion(new Version("2.6.9"), ServerType.Master);
//                db.SetKeepAlive(10);
//                Assert.IsNotNull(db.Features, "Features"); // we waited, after all
//                Assert.IsTrue(db.Features.ClientName, "ClientName");
//                Assert.IsTrue(db.Features.Time, "Time");
//                db.Name = "FooFoo";
//                db.Wait(db.Open());                
                
//                var local = DateTime.UtcNow;
//                var server = db.Wait(db.Server.Time());

//                Assert.True(Math.Abs((local - server).TotalMilliseconds) < 10, "Latency");
//            }
//        }

//        [Test, ExpectedException(typeof(TimeoutException), ExpectedMessage = "The operation has timed out; the connection is not open")]
//        public void TimeoutMessageNotOpened()
//        {
//            using (var conn = Config.GetUnsecuredConnection(open: false))
//            {
//                conn.Wait(conn.Strings.Get(0, "abc"));
//            }
//        }

//        [Test, ExpectedException(typeof(TimeoutException), ExpectedMessage = "The operation has timed out.")]
//        public void TimeoutMessageNoDetail()
//        {
//            using (var conn = Config.GetUnsecuredConnection(open: true))
//            {
//                conn.IncludeDetailInTimeouts = false;
//                conn.Keys.Remove(0, "noexist");
//                conn.Lists.BlockingRemoveFirst(0, new[] { "noexist" }, 5);
//                conn.Wait(conn.Strings.Get(0, "abc"));
//            }
//        }

//        [Test, ExpectedException(typeof(TimeoutException), ExpectedMessage = "The operation has timed out; possibly blocked by: 0: BLPOP \"noexist\" 5")]
//        public void TimeoutMessageWithDetail()
//        {
//            using (var conn = Config.GetUnsecuredConnection(open: true, waitForOpen: true))
//            {
//                conn.IncludeDetailInTimeouts = true;
//                conn.Keys.Remove(0, "noexist");
//                conn.Lists.BlockingRemoveFirst(0, new[] { "noexist" }, 5);
//                conn.Wait(conn.Strings.Get(0, "abc"));
//            }
//        }

//        [Test]
//        public void ClientList()
//        {
//            using (var killMe = Config.GetUnsecuredConnection())
//            using (var conn = Config.GetUnsecuredConnection(allowAdmin: true))
//            {
//                killMe.Wait(killMe.Strings.GetString(7, "kill me quick"));
//                var clients = conn.Wait(conn.Server.ListClients());
//                var target = clients.Single(x => x.Database == 7);
//                conn.Wait(conn.Server.KillClient(target.Address));
//                Assert.IsTrue(clients.Length > 0);

//                try
//                {
//                    killMe.Wait(killMe.Strings.GetString(7, "kill me quick"));
//                    Assert.Fail("Should have been dead");
//                }
//                catch (Exception) { }
//            }
//        }

//        [Test]
//        public void HappilyMurderedClientDoesntGetError()
//        {
//            using (var victim = Config.GetUnsecuredConnection(waitForOpen: true))
//            using (var murderer = Config.GetUnsecuredConnection(allowAdmin: true))
//            {
//                const int VictimDB = 4;
//                victim.Wait(victim.Strings.GetString(VictimDB, "kill me quick"));
//                victim.CompletionMode = ResultCompletionMode.PreserveOrder;
//                var clients = murderer.Wait(murderer.Server.ListClients());
//                var target = clients.Single(x => x.Database == VictimDB);

//                int i = 0;
//                victim.Closed += (s, a) =>
//                {
//                    Interlocked.Increment(ref i);
//                };
//                var errors = new List<Exception>();
//                victim.Shutdown += (s, a) =>
//                {
//                    if (a.Exception != null)
//                    {
//                        lock (errors)
//                        {
//                            errors.Add(a.Exception);
//                        }
//                    }
//                };
//                victim.Error += (s, a) =>
//                {
//                    lock (errors)
//                    {
//                        errors.Add(a.Exception);
//                    }
//                };
//                victim.Wait(victim.Server.Ping());
//                murderer.Wait(murderer.Server.KillClient(target.Address));

//                PubSub.AllowReasonableTimeToPublishAndProcess();

//                Assert.AreEqual(1, Interlocked.CompareExchange(ref i, 0, 0));
//                lock(errors)
//                {
//                    foreach (var err in errors) Console.WriteLine(err.Message);
//                    Assert.AreEqual(0, errors.Count);
//                }
//                Assert.AreEqual(ShutdownType.ServerClosed, victim.ShutdownType);


//            }
//        }
//        [Test]
//        public void MurderedClientKnowsAboutIt()
//        {
//            using (var victim = Config.GetUnsecuredConnection(waitForOpen: true))
//            using (var murderer = Config.GetUnsecuredConnection(allowAdmin: true))
//            {
//                const int VictimDB = 3;
//                victim.Wait(victim.Strings.GetString(VictimDB, "kill me quick"));
//                victim.CompletionMode = ResultCompletionMode.PreserveOrder;
//                var clients = murderer.Wait(murderer.Server.ListClients());
//                var target = clients.Single(x => x.Database == VictimDB);

//                object sync = new object();
//                ErrorEventArgs args = null;
//                Exception ex = null;
//                ManualResetEvent shutdownGate = new ManualResetEvent(false),
//                    exGate = new ManualResetEvent(false);
//                victim.Shutdown += (s,a) =>
//                {
//                    Console.WriteLine("shutdown");
//                    Interlocked.Exchange(ref args, a);
//                    shutdownGate.Set();
//                };
//                lock (sync)
//                {
//                    ThreadPool.QueueUserWorkItem(x =>
//                    {
//                        try
//                        {
                            
//                            for (int i = 0; i < 50000; i++)
//                            {
//                                if (i == 5) lock (sync) { Monitor.PulseAll(sync); }
//                                victim.Wait(victim.Strings.Set(VictimDB, "foo", "foo"));
//                            }
//                        }
//                        catch(Exception ex2)
//                        {
//                            Console.WriteLine("ex");
//                            Interlocked.Exchange(ref ex, ex2);
//                            exGate.Set();
//                        }
//                    }, null);
//                    // want the other thread to be running
//                    Monitor.Wait(sync);
//                    Console.WriteLine("got pulse; victim is ready");
//                }

//                Console.WriteLine("killing " + target.Address);
//                murderer.Wait(murderer.Server.KillClient(target.Address));

//                Console.WriteLine("waiting on gates...");
//                Assert.IsTrue(shutdownGate.WaitOne(10000), "shutdown gate");
//                Assert.IsTrue(exGate.WaitOne(10000), "exception gate");
//                Console.WriteLine("gates passed");

//                Assert.AreEqual(ShutdownType.ServerClosed, victim.ShutdownType);
//                var args_final = Interlocked.Exchange(ref args, null);
//                var ex_final = Interlocked.Exchange(ref ex, null);
//                Assert.IsNotNull(ex_final, "ex");
//                Assert.IsNotNull(args_final, "args");
//            }
//        }

//        [Test]
//        public void CleanCloseKnowsReason()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                conn.Wait(conn.Server.Ping());
//                conn.Close(false);
//                Assert.AreEqual(ShutdownType.ClientClosed, conn.ShutdownType);
//            }
//        }
//        [Test]
//        public void DisposeKnowsReason()
//        {
//            using (var conn = Config.GetUnsecuredConnection(waitForOpen: true))
//            {
//                conn.Wait(conn.Server.Ping());
//                conn.Dispose();
//                Assert.AreEqual(ShutdownType.ClientDisposed, conn.ShutdownType);
//            }
//        }

//        [Test]
//        public void TestLastSentCounter()
//        {
//            using (var db = Config.GetUnsecuredConnection(open: false))
//            {
//                db.SetServerVersion(new Version("2.6.0"), ServerType.Master);
//                db.SetKeepAlive(0); // turn off keep-alives so we don't get unexpected pings

//                db.Wait(db.Open());
//                db.Wait(db.Server.Ping());
//                var first = db.GetCounters(false);
//                Assert.LessOrEqual(0, 1, "0 <= 1");
//                Assert.LessOrEqual(first.LastSentMillisecondsAgo, 100, "first");

//                Thread.Sleep(2000);
//                var second = db.GetCounters(false);
//                Assert.GreaterOrEqual(1, 0, "1 >= 0");
//                Assert.GreaterOrEqual(second.LastSentMillisecondsAgo, 1900, "second");
//                Assert.LessOrEqual(second.LastSentMillisecondsAgo, 2100, "second");

//                db.Wait(db.Server.Ping());
//                var third = db.GetCounters(false);
//                Assert.LessOrEqual(0, 1, "0 <= 1");
//                Assert.LessOrEqual(third.LastSentMillisecondsAgo, 100, "third");
//            }
//        }

//        [Test]
//        public void TestKeepAlive()
//        {
//            string oldValue = null;
//            try
//            {
//                using (var db = Config.GetUnsecuredConnection(allowAdmin: true))
//                {
//                    oldValue = db.Wait(db.Server.GetConfig("timeout")).Single().Value;
//                    db.Server.SetConfig("timeout", "20");
//                }
//                using (var db  = Config.GetUnsecuredConnection(allowAdmin: false, waitForOpen:true))
//                {
//                    var before = db.GetCounters(false);
//                    Assert.AreEqual(4, before.KeepAliveSeconds, "keep-alive");
//                    Thread.Sleep(13 * 1000); 
//                    var after = db.GetCounters(false); 
//                    // 3 here is 2 * keep-alive, and one PING in GetCounters()
//                    int sent = after.MessagesSent - before.MessagesSent;
//                    Assert.GreaterOrEqual(1, 0);
//                    Assert.GreaterOrEqual(sent, 3);
//                    Assert.LessOrEqual(0, 4);
//                    Assert.LessOrEqual(sent, 4);
//                }
//            }
//            finally
//            {
//                if (oldValue != null)
//                {
//                    Task t;
//                    using (var db = Config.GetUnsecuredConnection(allowAdmin: true))
//                    {
//                        t = db.Server.SetConfig("timeout", oldValue);
//                    }
//                    Assert.IsTrue(t.Wait(5000));
//                    if (t.Exception != null) throw t.Exception;
//                }
//            }
//        }

//        [Test, ActiveTest]
//        public void SetValueWhileDisposing()
//        {
//            const int LOOP = 10;
//            for (int i = 0; i < LOOP; i++)
//            {
//                var guid = Config.CreateUniqueName();
//                Task t1, t3;
//                Task<string> t2;
//                string key = "SetValueWhileDisposing:" + i;
//                using (var db = Config.GetUnsecuredConnection(open: true))
//                {
//                    t1 = db.Strings.Set(0, key, guid);
//                }
//                Assert.IsTrue(t1.Wait(500));
//                using (var db = Config.GetUnsecuredConnection())
//                {
//                    t2 = db.Strings.GetString(0, key);
//                    t3 = db.Keys.Remove(0, key);                    
//                }                
//                Assert.IsTrue(t2.Wait(500));
//                Assert.AreEqual(guid, t2.Result);
//                Assert.IsTrue(t3.Wait(500));
//            }
//        }

//        [Test]
//        public void TestMasterSlaveSetup()
//        {
//            using (var unsec = Config.GetUnsecuredConnection(true, true, true))
//            using (var sec = Config.GetUnsecuredConnection(true, true, true))
//            {
//                try
//                {
//                    var makeSlave = sec.Server.MakeSlave(unsec.Host, unsec.Port);
//                    var info = sec.Wait(sec.Server.GetInfo());
//                    sec.Wait(makeSlave);
//                    Assert.AreEqual("slave", info["role"], "slave");
//                    Assert.AreEqual(unsec.Host, info["master_host"], "host");
//                    Assert.AreEqual(unsec.Port.ToString(), info["master_port"], "port");
//                    var makeMaster = sec.Server.MakeMaster();
//                    info = sec.Wait(sec.Server.GetInfo());
//                    sec.Wait(makeMaster);
//                    Assert.AreEqual("master", info["role"], "master");
//                }
//                finally
//                {
//                    sec.Server.MakeMaster();
//                }

//            }
//        }
//    }
//}
