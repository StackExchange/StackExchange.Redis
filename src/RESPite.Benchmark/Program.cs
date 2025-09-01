using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace RESPite.Benchmark;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            List<BenchmarkBase> benchmarks = [];
            foreach (var arg in args)
            {
                switch (arg)
                {
                    case "--old":
                        benchmarks.Add(new OldCoreBenchmark(args));
                        break;

                    case "--new":
                        benchmarks.Add(new NewCoreBenchmark(args));
                        break;
                }
            }

            if (benchmarks.Count == 0)
            {
                benchmarks.Add(new NewCoreBenchmark(args));
            }

            do
            {
                foreach (var bench in benchmarks)
                {
                    if (benchmarks.Count > 1)
                    {
                        Console.WriteLine($"### {bench} ###");
                    }

                    await bench.RunAll().ConfigureAwait(false);
                }
            }
            // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
            while (benchmarks[0].Loop);

            return 0;
        }
        catch (Exception ex)
        {
            WriteException(ex);
            return -1;
        }
    }

    internal static void WriteException(Exception? ex, [CallerMemberName] string operation = "")
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"### EXCEPTION: {operation}");
        while (ex is not null)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine($"\t{ex.StackTrace}");
            var data = ex.Data;
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (data is not null)
            {
                foreach (var key in data.Keys)
                {
                    Console.Error.WriteLine($"\t{key}: {data[key]}");
                }
            }

            ex = ex.InnerException;
        }
        Console.Error.WriteLine();
    }
}
