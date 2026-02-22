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

            var obj2 = new EnumParseBenchmarks();
            foreach (var value in obj2.Values())
            {
                Console.WriteLine($"Value: {value}");
                obj2.Value = value;
                // obj2.Setup();
                Console.WriteLine($"  Enum:   {obj2.EnumParse_CS()}");
                Console.WriteLine($"  Fast:   {obj2.FastHash_CS()}");
                Console.WriteLine($"  Bytes:  {obj2.Bytes_CS()}");
                Console.WriteLine($"  Switch: {obj2.Switch()}");
                Console.WriteLine();
            }

            /*
            Console.WriteLine();
            foreach (var val in Enum.GetValues<EnumParseBenchmarks.RedisCommand>())
            {
                Console.WriteLine($"\"{val}\" => RedisCommand.{val},");
            }
            */
#else
            BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
#endif
        }
    }
}
