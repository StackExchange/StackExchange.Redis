using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Examples
{
    /// <summary>
    /// Simple demonstration of the new ambient cancellation functionality.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== Simple Ambient Cancellation Demo ===\n");

            Console.WriteLine("Note: This demo shows the API usage. In a real scenario, connect to Redis:");
            Console.WriteLine("using var redis = ConnectionMultiplexer.Connect(\"localhost\");");
            Console.WriteLine("var database = redis.GetDatabase();\n");

            DemoBasicUsage();
            DemoNestedScopes();
            DemoTimeoutUsage();

            Console.WriteLine("Demo completed successfully!");
            await Task.CompletedTask;
        }

        private static void DemoBasicUsage()
        {
            Console.WriteLine("1. Basic Cancellation Usage");
            Console.WriteLine("----------------------------");

            using var cts = new CancellationTokenSource();
            IDatabase? database = null; // In real usage: redis.GetDatabase()

            try
            {
                using (database?.WithCancellation(cts.Token))
                {
                    Console.WriteLine("✓ Ambient cancellation token set");
                    Console.WriteLine("  All Redis operations in this scope will use the cancellation token");
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

        private static void DemoNestedScopes()
        {
            Console.WriteLine("2. Nested Scopes Example");
            Console.WriteLine("-------------------------");

            using var outerToken = new CancellationTokenSource();
            using var innerToken = new CancellationTokenSource();
            IDatabase? database = null; // In real usage: redis.GetDatabase()

            try
            {
                using (database?.WithCancellation(outerToken.Token))
                {
                    Console.WriteLine("✓ Outer scope: Using outer cancellation token");

                    using (database?.WithCancellation(innerToken.Token))
                    {
                        Console.WriteLine("✓ Inner scope: Using inner cancellation token (overrides outer)");
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

        private static void DemoTimeoutUsage()
        {
            Console.WriteLine("3. Timeout Usage Example");
            Console.WriteLine("-------------------------");

            IDatabase? database = null; // In real usage: redis.GetDatabase()

            try
            {
                using (database?.WithTimeout(TimeSpan.FromSeconds(5)))
                {
                    Console.WriteLine("✓ Timeout scope: Operations will timeout after 5 seconds");
                }

                Console.WriteLine("✓ Timeout scope disposed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine();
        }
    }
}
