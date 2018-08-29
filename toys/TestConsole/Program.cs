using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace TestConsole
{
    internal static class Program
    {
        private static async Task Main()
        {
            using (var conn = Create())
            {
                var sub = conn.GetSubscriber();
                Console.WriteLine("Subscsribe...");
                sub.Subscribe("foo", (channel, value) => Console.WriteLine($"{channel}: {value}"));
                Console.WriteLine("Ping...");
                sub.Ping();

                Console.WriteLine("Run publish...");
                await RunPub().ConfigureAwait(false);
            }

            await Console.Out.WriteLineAsync("Waiting a minute...").ConfigureAwait(false);
            await Task.Delay(60 * 1000).ConfigureAwait(false);
        }

        private static ConnectionMultiplexer Create()
        {
            var options = new ConfigurationOptions
            {
                KeepAlive = 5,
                EndPoints = { "localhost:6379" },
                SyncTimeout = int.MaxValue,
                // CommandMap = CommandMap.Create(new HashSet<string> { "subscribe", "psubscsribe", "publish" }, false),
            };

            Console.WriteLine("Connecting...");
            var muxer = ConnectionMultiplexer.Connect(options, Console.Out);
            Console.WriteLine("Connected");
            muxer.ConnectionFailed += (_, a) => Console.WriteLine($"Failed: {a.ConnectionType}, {a.EndPoint}, {a.FailureType}, {a.Exception}");
            muxer.ConnectionRestored += (_, a) => Console.WriteLine($"Restored: {a.ConnectionType}, {a.EndPoint}, {a.FailureType}, {a.Exception}");
            Console.WriteLine("Ping...");
            var time = muxer.GetDatabase().Ping();
            Console.WriteLine($"Pinged: {time.TotalMilliseconds}ms");
            return muxer;
        }

        public static async Task RunPub()
        {
            using (var conn = Create())
            {
                var pub = conn.GetSubscriber();
                for (int i = 0; i < 100; i++)
                {
                    await pub.PublishAsync("foo", i).ConfigureAwait(false);
                }

                await Console.Out.WriteLineAsync("Waiting a minute...").ConfigureAwait(false);
                await Task.Delay(60 * 1000).ConfigureAwait(false);
            }
        }
    }
}
