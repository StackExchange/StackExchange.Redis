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
                sub.Subscribe("foo", (channel, value) => Console.WriteLine($"{channel}: {value}"));
                sub.Ping();

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
            var muxer = ConnectionMultiplexer.Connect(options);
            muxer.ConnectionFailed += (s, a) => Console.WriteLine($"Failed: {a.ConnectionType}, {a.EndPoint}, {a.FailureType}, {a.Exception}");
            muxer.ConnectionRestored += (s, a) => Console.WriteLine($"Restored: {a.ConnectionType}, {a.EndPoint}, {a.FailureType}, {a.Exception}");
            muxer.GetDatabase().Ping();
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
