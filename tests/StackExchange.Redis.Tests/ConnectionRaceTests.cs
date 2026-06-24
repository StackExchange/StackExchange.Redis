using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ConnectionRaceTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task HandshakeCompletionGatePreventsUnauthenticatedPayloads()
    {
        // Simulate severe thread pool exhaustion using ThreadPool.SetMinThreads(1, 1);
        ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
        ThreadPool.SetMinThreads(1, 1);
        try
        {
            var options = new ConfigurationOptions
            {
                EndPoints = { { TestConfig.Current.MasterServer, TestConfig.Current.MasterPort } },
                Password = TestConfig.Current.MasterPassword,
                AbortOnConnectFail = false,
                AllowAdmin = true
            };

            await using var conn = await ConnectionMultiplexer.ConnectAsync(options);
            var db = conn.GetDatabase();
            var server = conn.GetServer(TestConfig.Current.MasterServer, TestConfig.Current.MasterPort);

            // Trigger an asynchronous connection reset loop
            var resetLoop = Task.Run(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    server.SimulateConnectionFailure(SimulatedFailureType.AuthenticationFailure);
                    await Task.Delay(10);
                }
            });

            // Concurrently launch 100 parallel Tasks trying to dispatch rapid PingAsync() operations.
            int taskCount = 100;
            var tasks = new Task[taskCount];
            for (int i = 0; i < taskCount; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    for (int j = 0; j < 50; j++)
                    {
                        try
                        {
                            await db.PingAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // Assert that no commands throw a NOAUTH or protocol corruption exception
                            Assert.DoesNotContain("NOAUTH", ex.Message);
                            Assert.DoesNotContain("protocol", ex.Message.ToLowerInvariant());
                        }
                    }
                });
            }

            await Task.WhenAll(tasks);
            await resetLoop;
        }
        finally
        {
            ThreadPool.SetMinThreads(workerThreads, completionPortThreads);
        }
    }
}
