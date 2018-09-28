using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using StackExchange.Redis;

static class Program
{
    private static int taskCount = 10;
    private static int totalRecords = 100000;

    static void Main()
    {

#if SEV2
        Pipelines.Sockets.Unofficial.SocketConnection.AssertDependencies();
        Console.WriteLine("We loaded the things...");
        // Console.ReadLine();
#endif

        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();

        var taskList = new List<Task>();
        var connection = ConnectionMultiplexer.Connect("127.0.0.1");
        for (int i = 0; i < taskCount; i++)
        {
            var i1 = i;
            var task = new Task(() => Run(i1, connection));
            task.Start();
            taskList.Add(task);
        }

        Task.WaitAll(taskList.ToArray());

        stopwatch.Stop();

        Console.WriteLine($"Done. {stopwatch.ElapsedMilliseconds}");
        Console.ReadLine();
    }

    static void Run(int taskId, ConnectionMultiplexer connection)
    {
        Console.WriteLine($"{taskId} Started");
        var database = connection.GetDatabase(0);

        for (int i = 0; i < totalRecords; i++)
        {
            database.StringSet(i.ToString(), i.ToString());
        }

        Console.WriteLine($"{taskId} Insert completed");

        for (int i = 0; i < totalRecords; i++)
        {
            var result = database.StringGet(i.ToString());
        }
        Console.WriteLine($"{taskId} Completed");
    }
}
