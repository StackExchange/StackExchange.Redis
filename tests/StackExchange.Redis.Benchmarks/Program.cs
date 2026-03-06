using System;
using System.Reflection;
using BenchmarkDotNet.Running;
using RESPite;

namespace StackExchange.Redis.Benchmarks
{
    internal static partial class Program
    {
        [AsciiHash("FooMagic")]
        public enum Foo
        {
            A,
            B,
            C,
        }

        private static void Main(string[] args)
        {
#if DEBUG
            // 8 when (hashCS is 6071209992391776325 or 8386095523210229861) || (hashCI is 6071209992391776325 && global::RESPite.AsciiHash.SequenceEqualsCI(value, "EXPIREAT"u8)) => StackExchange.Redis.Benchmarks.EnumParseBenchmarks.RedisCommand.EXPIREAT,
            AsciiHash.Hash("EXPIREAT", out var hashCS, out var hashCI);
            Console.WriteLine($"CS: {hashCS}, CI: {hashCI}");
            if (EnumParseBenchmarks.TryParse_CI("EXPIREAT", out var cmd))
            {
                Console.WriteLine(cmd);
            }

            var obj = new AsciiHashBenchmarks();
            foreach (var size in obj.Sizes)
            {
                Console.WriteLine($"Size: {size}");
                obj.Size = size;
                obj.Setup();
                Console.WriteLine($"    CS_B:  {obj.HashCS_B()}");
                Console.WriteLine($"    CS_C:  {obj.HashCS_C()}");
            }

            var obj2 = new EnumParseBenchmarks();
            foreach (var value in obj2.Values())
            {
                Console.WriteLine($"Value: {value}");
                obj2.Value = value;
                // obj2.Setup();
                Console.WriteLine($" CS Enum:   {obj2.EnumParse_CS()}");
                Console.WriteLine($" CS Fast:   {obj2.Ascii_C_CS()}");
                Console.WriteLine($" CS Bytes:  {obj2.Ascii_B_CS()}");
                Console.WriteLine($" CS Switch: {obj2.Switch_CS()}");
                Console.WriteLine($" CI Enum:   {obj2.EnumParse_CI()}");
                Console.WriteLine($" CI Fast:   {obj2.Ascii_C_CI()}");
                Console.WriteLine($" CI Bytes:  {obj2.Ascii_B_CI()}");
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
