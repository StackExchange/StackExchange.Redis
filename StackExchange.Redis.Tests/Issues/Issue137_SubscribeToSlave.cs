using System.Threading;
using NUnit.Framework;

namespace StackExchange.Redis.Tests.Issues
{
    // (Reported issue: https://github.com/StackExchange/StackExchange.Redis/issues/137) 
    //
    // As-of July 2015, the implementation of Subscription.SubscribeToServer() passes CommandFlags.DemandMaster
    // to CommandMultiplexer.SelectServer(), forcing the (P)SUBSCRIBE command to be executed against the master,
    // regardless of any flags passed in to RedisSubscriber.Subscribe. This is a problem for two reasons:
    //
    //  1) Pub-to-master / sub-to-slaves is commonly used for load balancing - there is no reason not to support
    //     it if the client specifies it.
    //  2) Configurations with only slave instance(s) can't be used for subscription at all.
    //

    [TestFixture]
    public class Issue137_SubscribeToSlave : TestBase
    {
        [Test]
        public void SubscribeShouldRespectCommandFlags()
        {
            const string Channel = "SubscribeShouldRespectCommandFlags";

            using (var mux = Create())
            {
                var sub = mux.GetSubscriber();
                sub.Subscribe(Channel, (channel, value) => { }, CommandFlags.DemandSlave);

                var slaveEndPoint = mux.GetServer(PrimaryServer + ":" + SlavePort).EndPoint;
                var subscriberEndPoint = sub.SubscribedEndpoint(Channel);

                Assert.AreEqual(slaveEndPoint, subscriberEndPoint);
            }
        }

        [Test]
        public void SubscribeToSlave()
        {
            const string Channel = "SubscribeToSlave";

            // Having two multiplexers here looks a little funky, but it is a simple way to demonstrate the 
            // issue using actual data flow - pre-fix, the 'slave only' mux can't be used for subscription.
            using (ConnectionMultiplexer master = ConnectionMultiplexer.Connect(PrimaryServer + ":" + PrimaryPort),
                                         slave = ConnectionMultiplexer.Connect(PrimaryServer + ":" + SlavePort))
            {
                int receivedCount = 0;

                var sub = slave.GetSubscriber();
                sub.Subscribe(Channel,
                    (channel, value) =>
                    { Interlocked.Increment(ref receivedCount); });

                var pub = master.GetSubscriber();
                pub.Publish(Channel, "Hey-ya");
                Thread.Sleep(500);

                Assert.AreEqual(1, receivedCount);
            }
        }
    }
}