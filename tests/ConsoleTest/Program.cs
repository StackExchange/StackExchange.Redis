using System.Diagnostics;
using System.Reflection;
using StackExchange.Redis;

Stopwatch stopwatch = new Stopwatch();
stopwatch.Start();

var options = ConfigurationOptions.Parse("127.0.0.1");
#if !SEREDIS_BASELINE
options.HighIntegrity = false; // as needed
Console.WriteLine($"{nameof(options.HighIntegrity)}: {options.HighIntegrity}");
#endif

// options.SocketManager = SocketManager.ThreadPool;
Console.WriteLine("Connecting...");
var connection = ConnectionMultiplexer.Connect(options);
Console.WriteLine("Connected");
connection.ConnectionFailed += Connection_ConnectionFailed;

void Connection_ConnectionFailed(object? sender, ConnectionFailedEventArgs e)
{
    Console.Error.WriteLine($"CONNECTION FAILED: {e.ConnectionType}, {e.FailureType}, {e.Exception}");
}

var startTime = DateTime.UtcNow;
var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;

var scenario = args?.Length > 0 ? args[0] : "mass-insert-async";

switch (scenario)
{
    case "parallel":
        Console.WriteLine("Parallel task test...");
        ParallelTasks(connection);
        break;
    case "mass-insert":
        Console.WriteLine("Mass insert test...");
        MassInsert(connection);
        break;
    case "mass-insert-async":
        Console.WriteLine("Mass insert (async/pipelined) test...");
        await MassInsertAsync(connection);
        break;
    case "mass-publish":
        Console.WriteLine("Mass publish test...");
        MassPublish(connection);
        break;
    default:
        Console.WriteLine("Scenario " + scenario + " is not recognized");
        break;
}

stopwatch.Stop();

Console.WriteLine("");
Console.WriteLine($"Done. {stopwatch.ElapsedMilliseconds} ms");

var endTime = DateTime.UtcNow;
var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
var totalMsPassed = (endTime - startTime).TotalMilliseconds;
var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
Console.WriteLine("Avg CPU: " + (cpuUsageTotal * 100));
Console.WriteLine("Lib Version: " + GetLibVersion());

static void MassInsert(ConnectionMultiplexer connection)
{
    const int NUM_INSERTIONS = 100_000;
    const int BATCH = 5000;
    int matchErrors = 0;

    var database = connection.GetDatabase(0);

    for (int i = 0; i < NUM_INSERTIONS; i++)
    {
        var key = $"StackExchange.Redis.Test.{i}";
        var value = i.ToString();

        database.StringSet(key, value);
        var retrievedValue = database.StringGet(key);

        if (retrievedValue != value)
        {
            matchErrors++;
        }

        if (i > 0 && i % BATCH == 0)
        {
            Console.WriteLine(i);
        }
    }

    Console.WriteLine($"Match errors: {matchErrors}");
}

static async Task MassInsertAsync(ConnectionMultiplexer connection)
{
    const int NUM_INSERTIONS = 100_000;
    const int BATCH = 5000;
    int matchErrors = 0;

    var database = connection.GetDatabase(0);

    var outstanding = new List<(Task, Task<RedisValue>, string)>(BATCH);

    for (int i = 0; i < NUM_INSERTIONS; i++)
    {
        var key = $"StackExchange.Redis.Test.{i}";
        var value = i.ToString();

        var set = database.StringSetAsync(key, value);
        var get = database.StringGetAsync(key);

        outstanding.Add((set, get, value));

        if (i > 0 && i % BATCH == 0)
        {
            matchErrors += await ValidateAsync(outstanding);
            Console.WriteLine(i);
        }
    }

    matchErrors += await ValidateAsync(outstanding);

    Console.WriteLine($"Match errors: {matchErrors}");

    static async Task<int> ValidateAsync(List<(Task, Task<RedisValue>, string)> outstanding)
    {
        int matchErrors = 0;
        foreach (var row in outstanding)
        {
            var s = await row.Item2;
            await row.Item1;
            if (s != row.Item3)
            {
                matchErrors++;
            }
        }
        outstanding.Clear();
        return matchErrors;
    }
}

static void ParallelTasks(ConnectionMultiplexer connection)
{
    static void ParallelRun(int taskId, ConnectionMultiplexer connection)
    {
        Console.Write($"{taskId} Started, ");
        var database = connection.GetDatabase(0);

        for (int i = 0; i < 100000; i++)
        {
            database.StringSet(i.ToString(), i.ToString());
        }

        Console.Write($"{taskId} Insert completed, ");

        for (int i = 0; i < 100000; i++)
        {
            var result = database.StringGet(i.ToString());
        }
        Console.Write($"{taskId} Completed, ");
    }

    var taskList = new List<Task>();
    for (int i = 0; i < 10; i++)
    {
        var i1 = i;
        var task = new Task(() => ParallelRun(i1, connection));
        task.Start();
        taskList.Add(task);
    }
    Task.WaitAll(taskList.ToArray());
}

static void MassPublish(ConnectionMultiplexer connection)
{
    var subscriber = connection.GetSubscriber();
    Parallel.For(0, 1000, _ => subscriber.Publish(new RedisChannel("cache-events:cache-testing", RedisChannel.PatternMode.Literal), "hey"));
}

static string GetLibVersion()
{
    var assembly = typeof(ConnectionMultiplexer).Assembly;
    return (Attribute.GetCustomAttribute(assembly, typeof(AssemblyFileVersionAttribute)) as AssemblyFileVersionAttribute)?.Version
        ?? assembly.GetName().Version?.ToString()
        ?? "Unknown";
}
