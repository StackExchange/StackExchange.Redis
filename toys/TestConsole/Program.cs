using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace TestConsole
{
    internal static class Program
    {
        public static void Main()
        {
            //using (var muxer = await ConnectionMultiplexer.ConnectAsync("127.0.0.1"))
            //{
            //    var db = muxer.GetDatabase();
            //    var sub = muxer.GetSubscriber();
            //    Console.WriteLine("subscribing");
            //    ChannelMessageQueue queue = await sub.SubscribeAsync("yolo");
            //    Console.WriteLine("subscribed");
            //}
        }
    }
}
