using NUnit.Framework;
using System;
using System.Threading;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class ConnectingFailDetection : TestBase
    {
#if DEBUG
        [TestCase]
        public void FastNoticesFailOnConnectingSync()
        {
            try
            {
                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    var conn = muxer.GetDatabase();
                    conn.Ping();

                    var server = muxer.GetServer(muxer.GetEndPoints()[0]);

                    muxer.AllowConnect = false;
                    SocketManager.ConnectCompletionType = CompletionType.Sync;

                    server.SimulateConnectionFailure();

                    Assert.IsFalse(muxer.IsConnected);

                    // should reconnect within 1 keepalive interval
                    muxer.AllowConnect = true;
                    Console.WriteLine("Waiting for reconnect");
                    Thread.Sleep(2000);

                    Assert.IsTrue(muxer.IsConnected);
                }

                ClearAmbientFailures();
            }
            finally 
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
            }
        }

        [TestCase]
        public void ConnectsWhenBeginConnectCompletesSynchronously()
        {
            try
            {
                SocketManager.ConnectCompletionType = CompletionType.Sync;

                using (var muxer = Create(keepAlive: 1, connectTimeout: 3000))
                {
                    var conn = muxer.GetDatabase();
                    conn.Ping();

                    Assert.IsTrue(muxer.IsConnected);
                }

                ClearAmbientFailures();
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
            }
        }

        [TestCase]
        public void FastNoticesFailOnConnectingAsync()
        {
            try
            {

                using (var muxer = Create(keepAlive: 1, connectTimeout: 10000, allowAdmin: true))
                {
                    var conn = muxer.GetDatabase();
                    conn.Ping();

                    var server = muxer.GetServer(muxer.GetEndPoints()[0]);

                    muxer.AllowConnect = false;
                    SocketManager.ConnectCompletionType = CompletionType.Async;

                    server.SimulateConnectionFailure();

                    Assert.IsFalse(muxer.IsConnected);

                    // should reconnect within 1 keepalive interval
                    muxer.AllowConnect = true;
                    Console.WriteLine("Waiting for reconnect");
                    Thread.Sleep(2000);

                    Assert.IsTrue(muxer.IsConnected);
                    ClearAmbientFailures();

                }
            }
            finally
            {
                SocketManager.ConnectCompletionType = CompletionType.Any;
            }
        }
#endif
    }
}
