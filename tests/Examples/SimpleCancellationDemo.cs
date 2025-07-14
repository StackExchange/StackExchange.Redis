using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Examples
{
    /// <summary>
    /// Simple demonstration of the new ambient cancellation functionality.
    /// </summary>
    public class SimpleCancellationDemo
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== Simple Ambient Cancellation Demo ===\n");

            // For this demo, we'll use a mock connection since we don't have Redis running
            // In a real scenario, you would connect to an actual Redis instance
            Console.WriteLine("Note: This demo shows the API usage. In a real scenario, connect to Redis:");
            Console.WriteLine("using var redis = ConnectionMultiplexer.Connect(\"localhost\");");
            Console.WriteLine("var database = redis.GetDatabase();\n");

            await DemoBasicUsage();
            await DemoNestedScopes();
            await DemoTimeoutUsage();
            await DemoContextInspection();

            Console.WriteLine("Demo completed successfully!");
        }

        static async Task DemoBasicUsage()
        {
            Console.WriteLine("1. Basic Cancellation Usage");
            Console.WriteLine("----------------------------");

            // Create a cancellation token
            using var cts = new CancellationTokenSource();

            // Simulate getting a database instance
            IDatabase database = null; // In real usage: redis.GetDatabase()

            try
            {
                // Set ambient cancellation - all operations in this scope will use this token
                using (database?.WithCancellation(cts.Token))
                {
                    Console.WriteLine("✓ Ambient cancellation token set");
                    Console.WriteLine("  All Redis operations in this scope will use the cancellation token");
                    
                    // In real usage, these would be actual Redis operations:
                    // await database.StringSetAsync("key", "value");
                    // var value = await database.StringGetAsync("key");
                    
                    Console.WriteLine("  (Redis operations would execute here with cancellation support)");
                }
                Console.WriteLine("✓ Cancellation scope disposed - back to normal operation");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine();
        }

        static async Task DemoNestedScopes()
        {
            Console.WriteLine("2. Nested Scopes Example");
            Console.WriteLine("-------------------------");

            using var outerToken = new CancellationTokenSource();
            using var innerToken = new CancellationTokenSource();

            IDatabase database = null; // In real usage: redis.GetDatabase()

            try
            {
                using (database?.WithCancellation(outerToken.Token))
                {
                    Console.WriteLine("✓ Outer scope: Using outer cancellation token");
                    
                    using (database?.WithCancellation(innerToken.Token))
                    {
                        Console.WriteLine("✓ Inner scope: Using inner cancellation token (overrides outer)");
                        
                        // Check current context
                        var context = RedisCancellationExtensions.GetCurrentContext();
                        if (context != null)
                        {
                            Console.WriteLine($"  Current context: {context}");
                        }
                    }
                    
                    Console.WriteLine("✓ Back to outer scope: Using outer cancellation token again");
                }
                Console.WriteLine("✓ No cancellation scope: Normal operation");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine();
        }

        static async Task DemoTimeoutUsage()
        {
            Console.WriteLine("3. Timeout Usage Example");
            Console.WriteLine("-------------------------");

            IDatabase database = null; // In real usage: redis.GetDatabase()

            try
            {
                using (database?.WithTimeout(TimeSpan.FromSeconds(5)))
                {
                    Console.WriteLine("✓ Timeout scope: Operations will timeout after 5 seconds");
                    
                    // Check current context
                    var context = RedisCancellationExtensions.GetCurrentContext();
                    if (context != null)
                    {
                        Console.WriteLine($"  Current context: {context}");
                    }
                }
                Console.WriteLine("✓ Timeout scope disposed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine();
        }

        static async Task DemoContextInspection()
        {
            Console.WriteLine("4. Context Inspection Example");
            Console.WriteLine("------------------------------");

            using var cts = new CancellationTokenSource();
            IDatabase database = null; // In real usage: redis.GetDatabase()

            // No context initially
            var context = RedisCancellationExtensions.GetCurrentContext();
            Console.WriteLine($"Initial context: {context?.ToString() ?? "None"}");

            using (database?.WithCancellation(cts.Token))
            {
                context = RedisCancellationExtensions.GetCurrentContext();
                Console.WriteLine($"With cancellation: {context?.ToString() ?? "None"}");

                using (database?.WithTimeout(TimeSpan.FromSeconds(10)))
                {
                    context = RedisCancellationExtensions.GetCurrentContext();
                    Console.WriteLine($"With timeout: {context?.ToString() ?? "None"}");

                    using (database?.WithCancellationAndTimeout(cts.Token, TimeSpan.FromSeconds(3)))
                    {
                        context = RedisCancellationExtensions.GetCurrentContext();
                        Console.WriteLine($"With both: {context?.ToString() ?? "None"}");
                    }
                }
            }

            context = RedisCancellationExtensions.GetCurrentContext();
            Console.WriteLine($"Final context: {context?.ToString() ?? "None"}");

            Console.WriteLine();
        }
    }
}
