using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.CommandRetry
{
    public class CommandRetryQueueManagerIntegrationTests
    {

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RetryAsyncMessageSucceedsOnTransientConnectionReset(bool retryPolicySet)
        {
            ConfigurationOptions configAdmin = new ConfigurationOptions();
            configAdmin.EndPoints.Add("127.0.0.1");
            configAdmin.AllowAdmin = true;
            using (var adminMuxer = ConnectionMultiplexer.Connect(configAdmin))
            {
                ConfigurationOptions configClient = new ConfigurationOptions();
                configClient.EndPoints.Add("127.0.0.1");
                configClient.RetryPolicy = null;
                if(retryPolicySet) configClient.RetryPolicy = RetryPolicy.Handle<RedisConnectionException>().AlwaysRetry();
                using (var clientmuxer = ConnectionMultiplexer.Connect(configClient))
                {
                    var conn = clientmuxer.GetDatabase();
                    bool stop = false;
                    var runLoad = Task.Run(async () =>
                    {
                        try
                        {
                            while (!stop)
                            {
                                Task[] tasks = new Task[100];
                                for (int i = 0; i < 100; i++)
                                {
                                    tasks[i] = conn.StringSetAsync("test", "test");
                                }
                                await Task.WhenAll(tasks);
                            }
                        }
                        catch
                        {
                            return false;
                        }
                        return true;
                    });


                    //connection blip
                    // making sure client name is set
                    KillClient(adminMuxer, clientmuxer);
                    await Task.Delay(10000);
                    stop = true;

                    // Assert all commands completed successfully
                    Assert.Equal(retryPolicySet, await runLoad);
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
