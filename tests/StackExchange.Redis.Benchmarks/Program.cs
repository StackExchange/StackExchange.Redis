using System;
using System.Reflection;
using BenchmarkDotNet.Running;

namespace StackExchange.Redis.Benchmarks
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
#if DEBUG
            var obj = new AsciiHashBenchmarks();
            foreach (var size in obj.Sizes)
            {
                Console.WriteLine($"Size: {size}");
                obj.Size = size;
                obj.Setup();
                obj.HashCS_C();
                obj.HashCS_B();
            }
#else
            if (args.Length > 0 && args[0] == "cache-scaling")
            {
                CacheHitScaling.Run(args); // threads x keys; not a BenchmarkDotNet run - see the class remarks
                return;
            }

            BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
#endif
        }
    }
}
