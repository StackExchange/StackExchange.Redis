Profiling
===

StackExchange.Redis exposes a handful of methods and types to enable performance profiling.  Due to its asynchronous and multiplexing
behavior profiling is a somewhat complicated topic.

Interfaces
---

The profiling API is composed of `ProfilingSession`, `ConnectionMultiplexer.RegisterProfiler(Func<ProfilingSession>)`,
`ProfilingSession.FinishProfiling()`, and `IProfiledCommand`.

You register a callback (`Func<ProfilingSession>`) that provides an ambient `ProfilingSession` with a `ConnectionMultiplexer` instance. When needed,
the library invokes this callback, and *if* a non-null session is returned: operations are attached to that session. Calling `FinishProfiling` on
a particular profiling sesssion returns a collection of `IProfiledCommand`s which contain timing information for all commands sent to redis by the
configured `ConnectionMultiplexer`. It is the callback's responsibility to maintain any state required to track individual sessions.

Available Timings
---

StackExchange.Redis exposes information about:

 - The redis server involved
 - The redis DB being queried
 - The redis command run
 - The flags used to route the command
 - The initial creation time of a command
 - How long it took to enqueue the command
 - How long it took to send the command, after it was enqueued
 - How long it took the response from redis to be received, after the command was sent
 - How long it took for the response to be processed, after it was received
 - If the command was sent in response to a cluster ASK or MOVED response
   - If so, what the original command was

`TimeSpan`s are high resolution, if supported by the runtime.  `DateTime`s are only as precise as `DateTime.UtcNow`.

Example profilers
---

Due to StackExchange.Redis's asynchronous interface, profiling requires outside assistance to group related commands together.
This is achieved by providing the desired `ProfilingSession` object via the callback, and (later) calling `FinishProfiling()` on that session.

Probably the most useful general-purpose session-provider is one that provides sessions automatically and works between `async` calls; this is simply:

```csharp
class AsyncLocalProfiler
{
    private readonly AsyncLocal<ProfilingSession> perThreadSession = new AsyncLocal<ProfilingSession>();

    public ProfilingSession GetSession()
    {
        var val = perThreadSession.Value;
        if (val == null)
        {
            perThreadSession.Value = val = new ProfilingSession();
        }
        return val;
    }
}
...
var profiler = new AsyncLocalProfiler();
multiplexer.RegisterProfiler(profiler.GetSession);
```

This will automatically create a profiling session per async-context (re-using the existing session if there is one). At the end of some unit of work, the
calling code can use `var commands = profiler.GetSession().FinishProfiling();` to get the operations performed and timings data.


---


A toy example of associating commands issued from many different threads together (while still allowing unrelated work not to be profiled)

1.*

```csharp
class ToyProfiler
{
    // note this won't work over "await" boundaries; "AsyncLocal" would be necessary there
    private readonly ThreadLocal<ProfilingSession> perThreadSession = new ThreadLocal<ProfilingSession>();
    public ProfilingSession PerThreadSession
    {
        get => perThreadSession.Value;
        set => perThreadSession.Value = value;
    }
}

// ...

ConnectionMultiplexer conn = /* initialization */;
var profiler = new ToyProfiler();
var sharedSession = new ProfilingSession();

conn.RegisterProfiler(() => profiler.PerThreadSession);

var threads = new List<Thread>();

for (var i = 0; i < 16; i++)
{
    var db = conn.GetDatabase(i);

    var thread =
        new Thread(
            delegate()
            {
                // set each thread to share a session
            	profiler.PerThreadSession = sharedSession;

                var threadTasks = new List<Task>();

                for (var j = 0; j < 1000; j++)
                {
                    var task = db.StringSetAsync("" + j, "" + j);
                    threadTasks.Add(task);
                }

                Task.WaitAll(threadTasks.ToArray());
            }
        );

	threads.Add(thread);
}

threads.ForEach(thread => thread.Start());
threads.ForEach(thread => thread.Join());

var timings = sharedSession.FinishProfiling();
```

At the end, `timings` will contain 16,000 `IProfiledCommand` objects - one for each command issued to redis.

If instead you did the following:

```csharp
ConnectionMultiplexer conn = /* initialization */;
var profiler = new ToyProfiler();

conn.RegisterProfiler(() => profiler.PerThreadSession);

var threads = new List<Thread>();

var perThreadTimings = new ConcurrentDictionary<Thread, List<IProfiledCommand>>();

for (var i = 0; i < 16; i++)
{
    var db = conn.GetDatabase(i);

    var thread =
        new Thread(
            delegate()
            {
                var threadTasks = new List<Task>();
                profiler.PerThreadSession = new ProfilingSession();

                for (var j = 0; j < 1000; j++)
                {
                    var task = db.StringSetAsync("" + j, "" + j);
                    threadTasks.Add(task);
                }

                Task.WaitAll(threadTasks.ToArray());

                perThreadTimings[Thread.CurrentThread] = profiler.PerThreadSession.FinishProfiling().ToList();
            }
        );
    threads.Add(thread);
}

threads.ForEach(thread => thread.Start());
threads.ForEach(thread => thread.Join());
```

`perThreadTimings` would end up with 16 entries of 1,000 `IProfilingCommand`s, keyed by the `Thread` that issued them.

Moving away from toy examples, here's how you can profile StackExchange.Redis in an MVC5 application.

First register the following `IProfiler` against your `ConnectionMultiplexer`:

```csharp
public class RedisProfiler
{
    const string RequestContextKey = "RequestProfilingContext";

    public ProfilingSession GetSession()
    {
        var ctx = HttpContext.Current;
        if (ctx == null) return null;

        return (ProfilingSession)ctx.Items[RequestContextKey];
    }

    public void CreateSessionForCurrentRequest()
    {
        var ctx = HttpContext.Current;
        if (ctx != null)
        {
            ctx.Items[RequestContextKey] = new ProfilingSession();
        }
    }
}
```

Then, add the following to your Global.asax.cs file (where `_redisProfiler` is the *instance* of the profiler):

```csharp
protected void Application_BeginRequest()
{
    _redisProfiler.CreateSessionForCurrentRequest();
}

protected void Application_EndRequest()
{
    var session = _redisProfiler.GetSession();
    if (session != null)
    {
        var timings = session.FinishProfiling();

		// do what you will with `timings` here
    }
}
```

and ensure that the connection has the profiler registered when the connection is created:

```csharp
connection.RegisterProfiler(() => _redisProfiler.GetSession());
```

This implementation will group all redis commands, including `async/await`-ed ones, with the http request that initiated them.
