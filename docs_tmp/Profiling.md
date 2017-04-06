Profiling
===

StackExchange.Redis exposes a handful of methods and types to enable performance profiling.  Due to its asynchronous and multiplexing 
behavior profiling is a somewhat complicated topic.

Interfaces
---

The profiling interface is composed of `IProfiler`, `ConnectionMultiplexer.RegisterProfiler(IProfiler)`, `ConnectionMultiplexer.BeginProfiling(object)`,
`ConnectionMultiplexer.FinishProfiling(object)`, and `IProfiledCommand`.

You register a single `IProfiler` with a `ConnectionMultiplexer` instance, it cannot be changed.  You begin profiling for a given context (ie. Thread,
Http Request, and so on) by calling `BeginProfiling(object)`, and finish by calling `FinishProfiling(object)`.  `FinishProfiling(object)` returns
a collection of `IProfiledCommand`s which contain timing information for all commands sent to redis by the configured `ConnectionMultiplexer` between
the `(Begin|Finish)Profiling` calls with the given context.

What "context" object should be used is application specific.

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

Choosing Context
---

Due to StackExchange.Redis's asynchronous interface, profiling requires outside assistance to group related commands together.  This is achieved
by providing context objects when you start and end profiling (via the `BeginProfiling(object)` & `FinishProfiling(object)` methods), and when a
command is sent (via the `IProfiler` interface's `GetContext()` method).

A toy example of associating commands issued from many different threads together

```C#
class ToyProfiler : IProfiler
{
	public ConcurrentDictionary<Thread, object> Contexts = new ConcurrentDictionary<Thread, object>();

	public object GetContext()
	{
		object ctx;
		if(!Contexts.TryGetValue(Thread.CurrentThread, out ctx)) ctx = null;

		return ctx;
	}
}

// ...

ConnectionMultiplexer conn = /* initialization */;
var profiler = new ToyProfiler();
var thisGroupContext = new object();

conn.RegisterProfiler(profiler);

var threads = new List<Thread>();

for (var i = 0; i < 16; i++)
{
    var db = conn.GetDatabase(i);

    var thread =
        new Thread(
            delegate()
            {
                var threadTasks = new List<Task>();

                for (var j = 0; j < 1000; j++)
                {
                    var task = db.StringSetAsync("" + j, "" + j);
                    threadTasks.Add(task);
                }

                Task.WaitAll(threadTasks.ToArray());
            }
        );

	profiler.Contexts[thread] = thisGroupContext;

	threads.Add(thread);
}

conn.BeginProfiling(thisGroupContext);

threads.ForEach(thread => thread.Start());
threads.ForEach(thread => thread.Join());

IEnumerable<IProfiledCommand> timings = conn.FinishProfiling(thisGroupContext);
```

At the end, `timings` will contain 16,000 `IProfiledCommand` objects - one for each command issued to redis.

If instead you did the following:

```C#
ConnectionMultiplexer conn = /* initialization */;
var profiler = new ToyProfiler();

conn.RegisterProfiler(profiler);

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

                conn.BeginProfiling(Thread.CurrentThread);

                for (var j = 0; j < 1000; j++)
                {
                    var task = db.StringSetAsync("" + j, "" + j);
                    threadTasks.Add(task);
                }

                Task.WaitAll(threadTasks.ToArray());

                perThreadTimings[Thread.CurrentThread] = conn.FinishProfiling(Thread.CurrentThread).ToList();
            }
        );

    profiler.Contexts[thread] = thread;

    threads.Add(thread);
}
                
threads.ForEach(thread => thread.Start());
threads.ForEach(thread => thread.Join());
```

`perThreadTimings` would end up with 16 entries of 1,000 `IProfilingCommand`s, keyed by the `Thread` the issued them.

Moving away from toy examples, here's how you can profile StackExchange.Redis in an MVC5 application.

First register the following `IProfiler` against your `ConnectionMultiplexer`:

```C#
public class RedisProfiler : IProfiler
{
    const string RequestContextKey = "RequestProfilingContext";

    public object GetContext()
    {
        var ctx = HttpContext.Current;
        if (ctx == null) return null;

        return ctx.Items[RequestContextKey];
    }

    public object CreateContextForCurrentRequest()
    {
        var ctx = HttpContext.Current;
        if (ctx == null) return null;

        object ret;
        ctx.Items[RequestContextKey] = ret = new object();

        return ret;
    }
}
```

Then, add the following to your Global.asax.cs file:

```C#
protected void Application_BeginRequest()
{
    var ctxObj = RedisProfiler.CreateContextForCurrentRequest();
    if (ctxObj != null)
    {
        RedisConnection.BeginProfiling(ctxObj);
    }
}

protected void Application_EndRequest()
{
    var ctxObj = RedisProfiler.GetContext();
    if (ctxObj != null)
    {
        var timings = RedisConnection.FinishProfiling(ctxObj);
		
		// do what you will with `timings` here
    }
}
```

This implementation will group all redis commands, including `async/await`-ed ones, with the http request that initiated them.