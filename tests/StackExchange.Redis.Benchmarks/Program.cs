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
                obj.HashCS_B();
                obj.HashCS_C();
            }

            var obj2 = new EnumParseBenchmarks();
            foreach (var value in obj2.Values())
            {
                Console.WriteLine($"Value: {value}");
                obj2.Value = value;
                // obj2.Setup();
                Console.WriteLine($" CS Enum:   {obj2.EnumParse_CS()}");
                Console.WriteLine($" CS Fast:   {obj2.AsciiHash_CS()}");
                Console.WriteLine($" CS Bytes:  {obj2.Bytes_CS()}");
                Console.WriteLine($" CS Switch: {obj2.Switch_CS()}");
                Console.WriteLine($" CI Enum:   {obj2.EnumParse_CI()}");
                Console.WriteLine($" CI Fast:   {obj2.AsciiHash_CI()}");
                Console.WriteLine($" CI Bytes:  {obj2.Bytes_CI()}");
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
