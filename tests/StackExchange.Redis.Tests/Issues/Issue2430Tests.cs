using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue2430Tests : TestBase
    {
        public Issue2430Tests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void Execute()
        {
            var options = new ConfigurationOptions()
            {
                AbortOnConnectFail = false,
                ConnectTimeout = 1,
                ConnectRetry = 0,
                SyncTimeout = 1,
                AllowAdmin = true,
                EndPoints = { GetConfiguration() },
            };

            using var conn = ConnectionMultiplexer.Connect(options, Writer);
            var db = conn.GetDatabase();

            // Disconnect and don't allow re-connection
            conn.AllowConnect = false;
            var server = conn.GetServerSnapshot()[0];
            server.SimulateConnectionFailure(SimulatedFailureType.All);

            // Increasing the number of backlog items increases the chance of a concurrency issue occurring
            var backlogTasks = new Task[100000];
            for (int i = 0; i < backlogTasks.Length; i++)
                backlogTasks[i] = db.PingAsync();

            Assert.True(Task.WaitAny(backlogTasks, 5000) != -1, "Timeout.");
            conn.Dispose();

            foreach (var task in backlogTasks)
            {
                Assert.True(task.IsCompleted, "Not completed.");
                Assert.True(task.IsFaulted, "Not faulted.");
                Assert.True(task.Exception!.InnerException is RedisTimeoutException or RedisConnectionException or ObjectDisposedException, $"Wrong exception: {task.Exception.InnerException.Message}");
            }
        }
    }
}
