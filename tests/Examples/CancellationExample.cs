using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Examples
{
    /// <summary>
    /// Demonstrates the new ambient cancellation functionality in StackExchange.Redis.
    /// </summary>
    public class CancellationExample
    {
        public static async Task Main(string[] args)
        {
            // Connect to Redis
            using var redis = ConnectionMultiplexer.Connect("localhost");
            var database = redis.GetDatabase();
            var subscriber = redis.GetSubscriber();

            Console.WriteLine("=== StackExchange.Redis Ambient Cancellation Examples ===\n");

            // Example 1: Basic cancellation
            await BasicCancellationExample(database);

            // Example 2: Timeout example
            await TimeoutExample(database);

            // Example 3: Combined cancellation and timeout
            await CombinedExample(database);

            // Example 4: Nested scopes
            await NestedScopesExample(database);

            // Example 5: Pub/Sub with cancellation
            await PubSubExample(subscriber);

            // Example 6: Cancellation during operation
            await CancellationDuringOperationExample(database);

            Console.WriteLine("\n=== All examples completed ===");
        }

        static async Task BasicCancellationExample(IDatabase database)
        {
            Console.WriteLine("1. Basic Cancellation Example");
            Console.WriteLine("------------------------------");

            using var cts = new CancellationTokenSource();

            try
            {
                using (database.WithCancellation(cts.Token))
                {
                    Console.WriteLine("Setting key with cancellation token...");
                    await database.StringSetAsync("example:basic", "Hello, World!");
                    
                    Console.WriteLine("Getting key with cancellation token...");
                    var value = await database.StringGetAsync("example:basic");
                    Console.WriteLine($"Retrieved value: {value}");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation was cancelled!");
            }

            Console.WriteLine();
        }

        static async Task TimeoutExample(IDatabase database)
        {
            Console.WriteLine("2. Timeout Example");
            Console.WriteLine("------------------");

            try
            {
                using (database.WithTimeout(TimeSpan.FromSeconds(5)))
                {
                    Console.WriteLine("Setting key with 5-second timeout...");
                    await database.StringSetAsync("example:timeout", "Timeout test");
                    
                    Console.WriteLine("Getting key with 5-second timeout...");
                    var value = await database.StringGetAsync("example:timeout");
                    Console.WriteLine($"Retrieved value: {value}");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation timed out!");
            }

            Console.WriteLine();
        }

        static async Task CombinedExample(IDatabase database)
        {
            Console.WriteLine("3. Combined Cancellation and Timeout Example");
            Console.WriteLine("---------------------------------------------");

            using var cts = new CancellationTokenSource();

            try
            {
                using (database.WithCancellationAndTimeout(cts.Token, TimeSpan.FromSeconds(10)))
                {
                    Console.WriteLine("Setting key with both cancellation token and timeout...");
                    await database.StringSetAsync("example:combined", "Combined test");
                    
                    Console.WriteLine("Getting key with both cancellation token and timeout...");
                    var value = await database.StringGetAsync("example:combined");
                    Console.WriteLine($"Retrieved value: {value}");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation was cancelled or timed out!");
            }

            Console.WriteLine();
        }

        static async Task NestedScopesExample(IDatabase database)
        {
            Console.WriteLine("4. Nested Scopes Example");
            Console.WriteLine("-------------------------");

            using var outerCts = new CancellationTokenSource();
            using var innerCts = new CancellationTokenSource();

            try
            {
                using (database.WithCancellation(outerCts.Token))
                {
                    Console.WriteLine("In outer scope - setting key1...");
                    await database.StringSetAsync("example:outer", "Outer scope");

                    using (database.WithCancellation(innerCts.Token))
                    {
                        Console.WriteLine("In inner scope - setting key2...");
                        await database.StringSetAsync("example:inner", "Inner scope");
                    }

                    Console.WriteLine("Back in outer scope - setting key3...");
                    await database.StringSetAsync("example:outer2", "Outer scope again");
                }

                // Verify all operations
                Console.WriteLine($"Outer value: {await database.StringGetAsync("example:outer")}");
                Console.WriteLine($"Inner value: {await database.StringGetAsync("example:inner")}");
                Console.WriteLine($"Outer2 value: {await database.StringGetAsync("example:outer2")}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("One of the operations was cancelled!");
            }

            Console.WriteLine();
        }

        static async Task PubSubExample(ISubscriber subscriber)
        {
            Console.WriteLine("5. Pub/Sub with Cancellation Example");
            Console.WriteLine("-------------------------------------");

            using var cts = new CancellationTokenSource();
            var messageReceived = new TaskCompletionSource<string>();

            try
            {
                using (subscriber.WithCancellation(cts.Token))
                {
                    var channel = "example:channel";

                    Console.WriteLine("Subscribing to channel with cancellation...");
                    await subscriber.SubscribeAsync(channel, (ch, message) =>
                    {
                        Console.WriteLine($"Received message: {message}");
                        messageReceived.TrySetResult(message);
                    });

                    Console.WriteLine("Publishing message with cancellation...");
                    await subscriber.PublishAsync(channel, "Hello from pub/sub!");

                    // Wait for the message
                    var receivedMessage = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Console.WriteLine($"Successfully received: {receivedMessage}");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Pub/Sub operation was cancelled!");
            }

            Console.WriteLine();
        }

        static async Task CancellationDuringOperationExample(IDatabase database)
        {
            Console.WriteLine("6. Cancellation During Operation Example");
            Console.WriteLine("-----------------------------------------");

            using var cts = new CancellationTokenSource();

            try
            {
                using (database.WithCancellation(cts.Token))
                {
                    Console.WriteLine("Starting operation...");
                    var task = database.StringSetAsync("example:cancel-during", "This might be cancelled");

                    // Cancel after a short delay
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        Console.WriteLine("Cancelling operation...");
                        cts.Cancel();
                    });

                    await task;
                    Console.WriteLine("Operation completed before cancellation");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation was successfully cancelled!");
            }

            Console.WriteLine();
        }
    }
}
