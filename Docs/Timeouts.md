Are you getting network or CPU bound?
---------------
Verify what's the maximum bandwidth supported on your client and on the server where redis-server is hosted. If there are requests that are getting bound by bandwidth, it will take longer for them to complete and thereby can cause timeouts.
Similarly, verify you are not getting CPU bound on client or on the server box which would cause requests to be waiting for CPU time and thereby have timeouts.

Are there commands taking long time to process on the redis-server?
---------------
There can be commands that are taking long time to process on the redis-server causing the request to timeout. Few examples of long running commands are mget with large number of keys, keys * or poorly written lua script. You can run the SlowLog command to see if there are requests taking longer than expected. More details regarding the command can be found [here] (http://redis.io/commands/slowlog) 

Was there a big request preceding several small requests to the Redis that timed out?
---------------
The parameter “qs” in the error message tells you how many requests were sent from the client to the server, but have not yet processed a response. For some types of load you might see that this value keeps growing, because StackExchange.Redis uses a single TCP connection and can only read one response at a time.  Even though the first operation timed out, it does not stop the data being sent to/from the server, and other requests are blocked until this is finished. Thereby, causing timeouts. One solution is to minimize the chance of timeouts by ensuring that your redis-server cache is large enough for your workload and splitting large values into smaller chunks. Another possible solution is to use a pool of ConnectionMultiplexer objects in your client, and choose the "least loaded" ConnectionMultiplexer when sending a new request.  This should prevent a single timeout from causing other requests to also timeout.


Are you seeing high number of busyio or busyworker threads in the timeout exception?
---------------
Let's first understand some details on ThreadPool Growth:

The CLR ThreadPool has two types of threads - "Worker" and "I/O Completion Port" (aka IOCP) threads.  

 - Worker threads are used when for things like processing `Task.Run(…)` or `ThreadPool.QueueUserWorkItem(…)` methods.  These threads are also used by various components in the CLR when work needs to happen on a background thread.
 - IOCP threads are used when asynchronous IO happens (e.g. reading from the network).  

The thread pool provides new worker threads or I/O completion threads on demand (without any throttling) until it reaches the "Minimum" setting for each type of thread.  By default, the minimum number of threads is set to the number of processors on a system.  

Once the number of existing (busy) threads hits the "minimum" number of threads, the ThreadPool will throttle the rate at which is injects new threads to one thread per 500 milliseconds.  This means that if your system gets a burst of work needing an IOCP thread, it will process that work very quickly.   However, if the burst of work is more than the configured "Minimum" setting, there will be some delay in processing some of the work as the ThreadPool waits for one of two things to happen
	1. An existing thread becomes free to process the work
	2. No existing thread becomes free for 500ms, so a new thread is created.

Basically, it means that when the number of Busy threads is greater than Min threads, you are likely paying a 500ms delay before network traffic is processed by the application.  Also, it is important to note that when an existing thread stays idle for longer than 15 seconds (based on what I remember), it will be cleaned up and this cycle of growth and shrinkage can repeat.

If we look at an example error message from StackExchange.Redis (build 1.0.450 or later), you will see that it now prints ThreadPool statistics (see IOCP and WORKER details below).

	System.TimeoutException: Timeout performing GET MyKey, inst: 2, mgr: Inactive, 
	queue: 6, qu: 0, qs: 6, qc: 0, wr: 0, wq: 0, in: 0, ar: 0, 
	IOCP: (Busy=6,Free=994,Min=4,Max=1000), 
	WORKER: (Busy=3,Free=997,Min=4,Max=1000)

In the above example, you can see that for IOCP thread there are 6 busy threads and the system is configured to allow 4 minimum threads.  In this case, the client would have likely seen two 500 ms delays because 6 > 4.

Note that StackExchange.Redis can hit timeouts if growth of either IOCP or WORKER threads gets throttled.

Recommendation:
Given the above information, it's recommend to set the minimum configuration value for IOCP and WORKER threads to something larger than the default value.  We can't give one-size-fits-all guidance on what this value should be because the right value for one application will be too high/low for another application.  This setting can also impact the performance of other parts of complicated applications, so you need to fine-tune this setting to your specific needs.  A good starting place is 200 or 300, then test and tweak as needed.

How to configure this setting:

 - In ASP.NET, use the ["minIoThreads" configuration setting](https://msdn.microsoft.com/en-us/library/vstudio/7w2sway1(v=vs.100).aspx) under the `<processModel>` configuration element in web.config.  You should be able to set this programmatically (see below) from your Application_Start method in global.asax.cs.

> **Important Note:** the value specified in this configuration element is a *per-core* setting.  For example, if you have a 4 core machine and want your minIOThreads setting to be 200 at runtime, you would use `<processModel minIoThreads="50"/>`.

 - Outside of ASP.NET, use the [ThreadPool.SetMinThreads(…)](https://msdn.microsoft.com//en-us/library/system.threading.threadpool.setminthreads(v=vs.100).aspx) API.