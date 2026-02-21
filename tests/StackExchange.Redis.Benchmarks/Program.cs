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
            var obj = new FastHashBenchmarks();
            foreach (var size in obj.Sizes)
            {
                Console.WriteLine($"Size: {size}");
                obj.Size = size;
                obj.Setup();
                obj.HashCS_B();
                obj.HashCS_C();
            }
#else
            BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
#endif
        }
    }
}
