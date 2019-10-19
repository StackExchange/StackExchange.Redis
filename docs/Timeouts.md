Are you getting network or CPU bound?
---------------
Verify what's the maximum bandwidth supported on your client and on the server where redis-server is hosted. If there are requests that are getting bound by bandwidth, it will take longer for them to complete and thereby can cause timeouts.
Similarly, verify you are not getting CPU bound on client or on the server box which would cause requests to be waiting for CPU time and thereby have timeouts.

Are there commands taking long time to process on the redis-server?
---------------
There can be commands that are taking long time to process on the redis-server causing the request to timeout. Few examples of long running commands are mget with large number of keys, keys * or poorly written lua script. You can run the SlowLog command to see if there are requests taking longer than expected. More details regarding the command can be found [here](https://redis.io/commands/slowlog) 

Was there a big request preceding several small requests to the Redis that timed out?
---------------
The parameter “`qs`” in the error message tells you how many requests were sent from the client to the server, but have not yet processed a response. For some types of load you might see that this value keeps growing, because StackExchange.Redis uses a single TCP connection and can only read one response at a time.  Even though the first operation timed out, it does not stop the data being sent to/from the server, and other requests are blocked until this is finished. Thereby, causing timeouts. One solution is to minimize the chance of timeouts by ensuring that your redis-server cache is large enough for your workload and splitting large values into smaller chunks. Another possible solution is to use a pool of ConnectionMultiplexer objects in your client, and choose the "least loaded" ConnectionMultiplexer when sending a new request.  This should prevent a single timeout from causing other requests to also timeout.


Are you seeing high number of busyio or busyworker threads in the timeout exception?
---------------

Asynchronous operations in StackExchange.Redis can come back in 3 different ways:

- IOCP threads are used when asynchronous IO happens (e.g. reading from the network).
- From 2.0 onwards, StackExchange.Redis maintains a dedicated thread-pool that it uses for completing most `async` operations; the error message may include a an indication of how many of these workers are currently available - if this is zero, it may suggest that your system is particularly busy with asynchronous operations
- .NET also has a global thread-pool; if the dedicated thread-pool is failing to keep up, additional work will be offered to the global thread-pool, so the message may include details of the global thread-pool



The StackExchange.Redis dedicated thread-pool has a fixed size suitable for many common scenarios, which is shared between multiple connection instances (this can be customized by explicitly providing a `SocketManager` when creating a `ConnectionMultiplexer`). In many scenarios when using 2.0 and above, the vast majority of asynchronous operations will be services by this dedicated pool. This pool exists to avoid contention, as we've frequently seen cases where the global thread-pool becomes jammed with threads that need redis results to unblock them.

.NET itself provides new global thread pool worker threads or I/O completion threads on demand (without any throttling) until it reaches the "Minimum" setting for each type of thread.  By default, the minimum number of threads is set to the number of processors on a system.

For these .NET-provided global thread pools: once the number of existing (busy) threads hits the "minimum" number of threads, the ThreadPool will throttle the rate at which is injects new threads to one thread per 500 milliseconds.  This means that if your system gets a burst of work needing an IOCP thread, it will process that work very quickly.   However, if the burst of work is more than the configured "Minimum" setting, there will be some delay in processing some of the work as the ThreadPool waits for one of two things to happen
	1. An existing thread becomes free to process the work
	2. No existing thread becomes free for 500ms, so a new thread is created.

Basically, *if* you're hitting the global thread pool (rather than the dedicated StackExchange.Redis thread-pool) it means that when the number of Busy threads is greater than Min threads, you are likely paying a 500ms delay before network traffic is processed by the application.  Also, it is important to note that when an existing thread stays idle for longer than 15 seconds (based on what I remember), it will be cleaned up and this cycle of growth and shrinkage can repeat.

If we look at an example error message from StackExchange.Redis 2.0, you will see that it now prints ThreadPool statistics (see IOCP and WORKER details below).

	Timeout performing GET MyKey (1000ms), inst: 2, qs: 6, in: 0, mgr: 9 of 10 available,
	IOCP: (Busy=6,Free=994,Min=4,Max=1000), 
	WORKER: (Busy=3,Free=997,Min=4,Max=1000)

In the above example, there are 6 operations currently awaiting replies from redis ("`qs`"), there are 0 bytes waiting to be read from the input stream from redis ("`in`"), and the dedicated thread-pool is almost fully available to service asynchronous completions ("`mgr`"). You can also see that for IOCP thread there are 6 busy threads and the system is configured to allow 4 minimum threads. 

In 1.*, the information is similar but slightly different:

	System.TimeoutException: Timeout performing GET MyKey, inst: 2, mgr: Inactive, 
	queue: 6, qu: 0, qs: 6, qc: 0, wr: 0, wq: 0, in: 0, ar: 0, 
	IOCP: (Busy=6,Free=994,Min=4,Max=1000), 
	WORKER: (Busy=3,Free=997,Min=4,Max=1000)

It may seem contradictory that there are *less* numbers in 2.0 - this is because the 2.0 code has been redesigned not to require some additional steps.

Note that StackExchange.Redis can hit timeouts if either the IOCP threadss or the worker threads (.NET global thread-pool, or the dedicated thread-pool) become saturated without the ability to grow.

Also note that the IOCP and WORKER threads will not be shown on .NET Core if using `netstandard` < 2.0.

Note that You shouldn't need a much fine-tuning of this from 2.0, since the dedicated thread-pool should be servicing most of the load.

Recommendation:
Given the above information, in 1.* it's recommend to set the minimum configuration value for IOCP and WORKER threads to something larger than the default value.  We can't give one-size-fits-all guidance on what this value should be because the right value for one application will be too high/low for another application.  This setting can also impact the performance of other parts of complicated applications, so you need to fine-tune this setting to your specific needs.  A good starting place is 200 or 300, then test and tweak as needed.

How to configure this setting:

 - In ASP.NET, use the ["minIoThreads" configuration setting](https://msdn.microsoft.com/en-us/library/7w2sway1(v=vs.71).aspx) under the `<processModel>` configuration element in `machine.config`. According to Microsoft, you can't change this value per site by editing your web.config (even when you could do it in the past), so the value that you choose here is the value that all your .NET sites will use. Please note that you don't need to add every property if you put autoConfig in false, just putting autoConfig="false" and overriding the value is enough:

```xml
<processModel autoConfig="false" minIoThreads="250" />
```

> **Important Note:** the value specified in this configuration element is a *per-core* setting.  For example, if you have a 4 core machine and want your minIOThreads setting to be 200 at runtime, you would use `<processModel minIoThreads="50"/>`.

 - Outside of ASP.NET, use the [ThreadPool.SetMinThreads(…)](https://docs.microsoft.com/en-us/dotnet/api/system.threading.threadpool.setminthreads?view=netcore-2.0#System_Threading_ThreadPool_SetMinThreads_System_Int32_System_Int32_) API.

- In .Net Core, add Environment Variable COMPlus_ThreadPool_ForceMinWorkerThreads to overwrite default MinThreads setting, according to [Environment/Registry Configuration Knobs](https://github.com/dotnet/coreclr/blob/master/Documentation/project-docs/clr-configuration-knobs.md) - You can also use the same ThreadPool.SetMinThreads() Method as described above.
