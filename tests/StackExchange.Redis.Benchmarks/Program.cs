using System.Reflection;
using BenchmarkDotNet.Running;

namespace StackExchange.Redis.Benchmarks
{
    internal static class Program
    {
        private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
    }
}
