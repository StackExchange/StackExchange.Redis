using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues
{
    public class Issue2763Tests(ITestOutputHelper output) : TestBase(output)
    {
        [Fact]
        public void Execute()
        {
            using var conn = Create();
            var subscriber = conn.GetSubscriber();

            static void Handler(RedisChannel c, RedisValue v) { }

            const int COUNT = 1000;
            RedisChannel channel = RedisChannel.Literal("CHANNEL:TEST");

            List<Action> subscribes = new List<Action>(COUNT);
            for (int i = 0; i < COUNT; i++)
                subscribes.Add(() => subscriber.Subscribe(channel, Handler));
            Parallel.ForEach(subscribes, action => action());

            Assert.Equal(COUNT, CountSubscriptionsForChannel(subscriber, channel));

            List<Action> unsubscribes = new List<Action>(COUNT);
            for (int i = 0; i < COUNT; i++)
                unsubscribes.Add(() => subscriber.Unsubscribe(channel, Handler));
            Parallel.ForEach(unsubscribes, action => action());

            Assert.Equal(0, CountSubscriptionsForChannel(subscriber, channel));
        }

        private static int CountSubscriptionsForChannel(ISubscriber subscriber, RedisChannel channel)
        {
            ConnectionMultiplexer connMultiplexer = (ConnectionMultiplexer)subscriber.Multiplexer;
            connMultiplexer.GetSubscriberCounts(channel, out int handlers, out int _);
            return handlers;
        }
    }
}
