using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.CommandRetry
{
    public class MessageRetryQueueIntegrationTests
    {

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RetryAsyncMessageIntegration(bool retryPolicySet)
        {
            ConfigurationOptions configAdmin = new ConfigurationOptions();
            configAdmin.EndPoints.Add("127.0.0.1");
            configAdmin.AbortOnConnectFail = false;
            configAdmin.AllowAdmin = true;
            configAdmin.RetryCommandsOnReconnect = retryPolicySet ? RetryOnReconnect.Always : null;

            ConfigurationOptions configClient = new ConfigurationOptions();
            configClient.EndPoints.Add("127.0.0.1");
            configAdmin.AbortOnConnectFail = false;
            configClient.RetryCommandsOnReconnect = retryPolicySet ? RetryOnReconnect.Always : null;

            using (var adminMuxer = ConnectionMultiplexer.Connect(configAdmin))
            using (var clientmuxer = ConnectionMultiplexer.Connect(configClient))
            {
                var conn = clientmuxer.GetDatabase();
                const string keyname = "testretrypolicy";
                long count = 0;

                var runLoad = Task.Run(() =>
                {
                    try
                    {
                        using (var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                        {
                            var cancellationToken = cancellationTokenSource.Token;
                            int parallelTaskCount = 200;

                            while (true)
                                {
                                Task[] tasks = new Task[parallelTaskCount];
                                for (int i = 0; i < parallelTaskCount; i++)
                                {
                                    tasks[i] = conn.StringSetBitAsync(keyname, count, true);
                                    Interlocked.Increment(ref count);
                                }
                                Task.WaitAll(tasks, cancellationToken);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Assert.False(retryPolicySet, ex.ToString());
                        return false;
                    }
                });

                // let the load warmup at least n times before connection blip
                await Task.Delay(2000);

                // connection blip
                KillClient(adminMuxer, clientmuxer);

                // wait for load to stop
                var isLoadSucceed = await runLoad;

                // Assert load completed based on policy
                Assert.Equal(retryPolicySet, isLoadSucceed);

                // Assert for retrypolicy data was set correctly after retry
                if (retryPolicySet)
                {
                    Assert.Equal(Interlocked.Read(ref count), await conn.StringBitCountAsync(keyname));
                }

                // cleanup
                await adminMuxer.GetDatabase().KeyDeleteAsync(keyname);
            }
        }

        private void KillClient(IInternalConnectionMultiplexer adminMuxer, IInternalConnectionMultiplexer clientmuxer)
        {
            string clientname = "retrypolicy";
            var clientServer = clientmuxer.GetServer(clientmuxer.GetEndPoints()[0]);
            clientServer.Execute("client", "setname", clientname);

            var adminServer = adminMuxer.GetServer(adminMuxer.GetEndPoints()[0]);
            var clientsToKill = adminServer.ClientList().Where(c => c.Name.Equals(clientname));
            Assert.NotNull(clientsToKill);
            foreach (var client in clientsToKill)
            {
                Assert.Equal(clientname, client.Name);
                adminMuxer.GetServer(adminMuxer.GetEndPoints()[0]).ClientKill(client.Address);
            }
        }

    }
}
