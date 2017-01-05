using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if FEATURE_BOOKSLEEVE
using BookSleeve;
#endif
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{

    public abstract class TestBase : IDisposable
    {

        protected void CollectGarbage()
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
            }
        }
        private readonly SocketManager socketManager;

        protected TestBase()
        {
            socketManager = new SocketManager(GetType().Name);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public void Dispose()
        {
            socketManager.Dispose();
        }
#if VERBOSE
        protected const int AsyncOpsQty = 100, SyncOpsQty = 10;
#else
        protected const int AsyncOpsQty = 100000, SyncOpsQty = 10000;
#endif
        static TestBase()
        {
            TaskScheduler.UnobservedTaskException += (sender,args)=>
            {
                Console.WriteLine("Unobserved: " + args.Exception);
                args.SetObserved();
#if CORE_CLR
                if (IgnorableExceptionPredicates.Any(predicate => predicate(args.Exception.InnerException))) return;
#endif
                Interlocked.Increment(ref failCount);
                lock (exceptions)
                {
                    exceptions.Add(args.Exception.Message);
                }
            };
        }

#if CORE_CLR
        static Func<Exception, bool>[] IgnorableExceptionPredicates = new Func<Exception, bool>[]
        {
            e => e != null && e is ObjectDisposedException && e.Message.Equals("Cannot access a disposed object.\r\nObject name: 'System.Net.Sockets.NetworkStream'."),
            e => e != null && e is IOException && e.Message.StartsWith("Unable to read data from the transport connection:")
        };
#endif

        protected void OnConnectionFailed(object sender, ConnectionFailedEventArgs e)
        {
            Interlocked.Increment(ref failCount);
            lock(exceptions)
            {
                exceptions.Add("Connection failed: " + EndPointCollection.ToString(e.EndPoint) + "/" + e.ConnectionType);
            }
        }

        protected void OnInternalError(object sender, InternalErrorEventArgs e)
        {
            Interlocked.Increment(ref failCount);
            lock (exceptions)
            {
                exceptions.Add("Internal error: " + e.Origin + ", " + EndPointCollection.ToString(e.EndPoint) + "/" + e.ConnectionType);
            }
        }

        static int failCount;
        volatile int expectedFailCount;
        [SetUp]
        public void Setup()
        {
            ClearAmbientFailures();
        }
        public void ClearAmbientFailures()
        {
            Collect();
            Interlocked.Exchange(ref failCount, 0);
            expectedFailCount = 0;
            lock(exceptions)
            {
                exceptions.Clear();
            }
        }
        private static readonly List<string> exceptions = new List<string>();
        public void SetExpectedAmbientFailureCount(int count)
        {
            expectedFailCount = count;
        }
        static void Collect() {
            for (int i = 0; i < GC.MaxGeneration; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
            }
        }
        [TearDown]
        public void Teardown()
        {
            Collect();
            if (expectedFailCount >= 0 && Interlocked.CompareExchange(ref failCount, 0, 0) != expectedFailCount)
            {
                var sb = new StringBuilder("There were ").Append(failCount).Append(" general ambient exceptions; expected ").Append(expectedFailCount);
                lock(exceptions)
                {
                    foreach(var item in exceptions.Take(5))
                    {
                        sb.Append("; ").Append(item);
                    }
                }
                Assert.Fail(sb.ToString());
            }            
        }

        protected const int PrimaryPort = 6379, SlavePort = 6380, SecurePort = 6381;
        protected const string PrimaryServer = "127.0.0.1", SecurePassword = "changeme", PrimaryPortString = "6379", SlavePortString = "6380", SecurePortString = "6381";
        internal static Task Swallow(Task task)
        {
            if (task != null) task.ContinueWith(swallowErrors, TaskContinuationOptions.OnlyOnFaulted);
            return task;
        }
        private static readonly Action<Task> swallowErrors = SwallowErrors;
        private static void SwallowErrors(Task task)
        {
            if (task != null) GC.KeepAlive(task.Exception);
        }
        protected IServer GetServer(ConnectionMultiplexer muxer)
        {
            EndPoint[] endpoints = muxer.GetEndPoints();
            IServer result = null;
            foreach(var endpoint in endpoints)
            {
                var server = muxer.GetServer(endpoint);
                if (server.IsSlave || !server.IsConnected) continue;
                if(result != null) throw new InvalidOperationException("Requires exactly one master endpoint (found " + server.EndPoint + " and " + result.EndPoint + ")");
                result = server;
            }
            if(result == null) throw new InvalidOperationException("Requires exactly one master endpoint (found none)");
            return result;
        }

        protected virtual ConnectionMultiplexer Create(
            string clientName = null, int? syncTimeout = null, bool? allowAdmin = null, int? keepAlive = null,
            int? connectTimeout = null, string password = null, string tieBreaker = null, TextWriter log = null,
            bool fail = true, string[] disabledCommands = null, string[] enabledCommands = null,
            bool checkConnect = true, bool pause = true, string failMessage = null,
            string channelPrefix = null, bool useSharedSocketManager = true, Proxy? proxy = null)
        {
            if(pause) Thread.Sleep(250); // get a lot of glitches when hammering new socket creations etc; pace it out a bit
            string configuration = GetConfiguration();
            var config = ConfigurationOptions.Parse(configuration);
            if (disabledCommands != null && disabledCommands.Length != 0)
            {
                config.CommandMap = CommandMap.Create(new HashSet<string>(disabledCommands), false);
            } else if (enabledCommands != null && enabledCommands.Length != 0)
            {
                config.CommandMap = CommandMap.Create(new HashSet<string>(enabledCommands), true);
            }

            if(Debugger.IsAttached)
            {
                syncTimeout = int.MaxValue;
            }

            if (useSharedSocketManager) config.SocketManager = socketManager;
            if (channelPrefix != null) config.ChannelPrefix = channelPrefix;
            if (tieBreaker != null) config.TieBreaker = tieBreaker;
            if (password != null) config.Password = string.IsNullOrEmpty(password) ? null : password;
            if (clientName != null) config.ClientName = clientName;
            if (syncTimeout != null) config.SyncTimeout = syncTimeout.Value;
            if (allowAdmin != null) config.AllowAdmin = allowAdmin.Value;
            if (keepAlive != null) config.KeepAlive = keepAlive.Value;
            if (connectTimeout != null) config.ConnectTimeout = connectTimeout.Value;
            if (proxy != null) config.Proxy = proxy.Value;
            var watch = Stopwatch.StartNew();
            var task = ConnectionMultiplexer.ConnectAsync(config, log ?? Console.Out);
            if (!task.Wait(config.ConnectTimeout >= (int.MaxValue / 2) ? int.MaxValue : config.ConnectTimeout * 2))
            {
                task.ContinueWith(x =>
                {
                    try
                    {
                        GC.KeepAlive(x.Exception);
                    }
                    catch
                    { }
                }, TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("Connect timeout");
            }
            watch.Stop();
            Console.WriteLine("Connect took: " + watch.ElapsedMilliseconds + "ms");
            var muxer = task.Result;
            if (checkConnect)
            {
                if (!muxer.IsConnected)
                {
                    if (fail) Assert.Fail(failMessage + "Server is not available");
                    Assert.Inconclusive(failMessage + "Server is not available");
                }
            }
            muxer.InternalError += OnInternalError;
            muxer.ConnectionFailed += OnConnectionFailed;
            return muxer;
        }

        protected virtual string GetConfiguration()
        {
            return PrimaryServer + ":" + PrimaryPort + "," + PrimaryServer + ":" + SlavePort;
        }

        protected static string Me([CallerMemberName] string caller = null)
        {
            return caller;
        }

#if FEATURE_BOOKSLEEVE
        protected static RedisConnection GetOldStyleConnection(bool open = true, bool allowAdmin = false, bool waitForOpen = false, int syncTimeout = 5000, int ioTimeout = 5000)
        {
            return GetOldStyleConnection(PrimaryServer, PrimaryPort, open, allowAdmin, waitForOpen, syncTimeout, ioTimeout);
        }
        private static RedisConnection GetOldStyleConnection(string host, int port, bool open = true, bool allowAdmin = false, bool waitForOpen = false, int syncTimeout = 5000, int ioTimeout = 5000)
        {
            var conn = new RedisConnection(host, port, syncTimeout: syncTimeout, ioTimeout: ioTimeout, allowAdmin: allowAdmin);
            conn.Error += (s, args) =>
            {
                Trace.WriteLine(args.Exception.Message, args.Cause);
            };
            if (open)
            {
                var openAsync = conn.Open();
                if (waitForOpen) conn.Wait(openAsync);
            }
            return conn;
        }
#endif
        protected static TimeSpan RunConcurrent(Action work, int threads, int timeout = 10000, [CallerMemberName] string caller = null)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (threads < 1) throw new ArgumentOutOfRangeException(nameof(threads));
            if(string.IsNullOrWhiteSpace(caller)) caller = Me();
            Stopwatch watch = null;
            ManualResetEvent allDone = new ManualResetEvent(false);
            object token = new object();
            int active = 0;
            ThreadStart callback = delegate
            {
                lock (token)
                {
                    int nowActive = Interlocked.Increment(ref active);
                    if (nowActive == threads)
                    {
                        watch = Stopwatch.StartNew();
                        Monitor.PulseAll(token);
                    }
                    else
                    {
                        Monitor.Wait(token);
                    }
                }
                work();
                if (Interlocked.Decrement(ref active) == 0)
                {
                    watch.Stop();
                    allDone.Set();
                }
            };

            Thread[] threadArr = new Thread[threads];
            for (int i = 0; i < threads; i++)
            {
                var thd = new Thread(callback);
                thd.Name = caller;
                threadArr[i] = thd;
                thd.Start();
            }
            if (!allDone.WaitOne(timeout))
            {
#if !CORE_CLR
                for (int i = 0; i < threads; i++)
                {
                    var thd = threadArr[i];
                    if (thd.IsAlive) thd.Abort();
                }
#endif
                throw new TimeoutException();
            }

            return watch.Elapsed;
        }

        
        protected virtual void GetAzureCredentials(out string name, out string password)
        {
            var lines = File.ReadAllLines(@"d:\dev\azure.txt");
            if (lines == null || lines.Length != 2)
                Assert.Inconclusive("azure credentials missing");
            name = lines[0];
            password = lines[1];
        }

    }
}
