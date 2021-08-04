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
            string keyname = "testretrypolicy";
            ConfigurationOptions configAdmin = new ConfigurationOptions();
            configAdmin.EndPoints.Add("127.0.0.1");
            configAdmin.AllowAdmin = true;
            using (var adminMuxer = ConnectionMultiplexer.Connect(configAdmin))
            {
                ConfigurationOptions configClient = new ConfigurationOptions();
                configClient.EndPoints.Add("127.0.0.1");
                configClient.RetryCommandsOnReconnect = null;
                if (retryPolicySet) configClient.RetryCommandsOnReconnect = RetryOnReconnect.Always;
                using (var clientmuxer = ConnectionMultiplexer.Connect(configClient))
                {
                    var conn = clientmuxer.GetDatabase();
                    bool stop = false;
                    long count = 0;
                    var runLoad = Task.Run(async () =>
                    {
                        try
                        {
                            int paralleltasks = 200;
                            while (true)
                            {
                                Task[] tasks = new Task[paralleltasks];
                                for (int i = 0; i < paralleltasks; i++)
                                {
                                    tasks[i] = conn.StringSetBitAsync(keyname, count, true);
                                    Interlocked.Increment(ref count);
                                }
                                await Task.WhenAll(tasks);
                                if (stop) break;
                            }
                        }
                        catch
                        {
                            return false;
                        }
                        return true;
                    });


                    // let the load warmup atleast n times before connection blip
                    await Task.Delay(2000);

                    // connection blip
                    KillClient(adminMuxer, clientmuxer);

                    // let the load run atleast n times during connection blip
                    await Task.Delay(10000);

                    stop = true;

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
