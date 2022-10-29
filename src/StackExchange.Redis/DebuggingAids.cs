using System;
using System.Diagnostics;

namespace StackExchange.Redis
{
#if VERBOSE

    partial class ConnectionMultiplexer
    {
        private readonly int epoch = Environment.TickCount;

        partial void OnTrace(string message, string category)
        {
            Debug.WriteLine(message,
                ((Environment.TickCount - epoch)).ToString().PadLeft(5, ' ') + "ms on " +
                Environment.CurrentManagedThreadId + " ~ " + category);
        }
        static partial void OnTraceWithoutContext(string message, string category)
        {
            Debug.WriteLine(message, Environment.CurrentManagedThreadId + " ~ " + category);
        }

        partial void OnTraceLog(LogProxy log, string caller)
        {
            lock (UniqueId)
            {
                Trace(log.ToString(), caller); // note that this won't always be useful, but we only do it in debug builds anyway
            }
        }
    }
#endif

#if LOGOUTPUT
    partial class PhysicalConnection
    {
        partial void OnWrapForLogging(ref System.IO.Pipelines.IDuplexPipe pipe, string name, SocketManager mgr)
        {
            foreach(var c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            pipe = new LoggingPipe(pipe, $"{name}.in.resp", $"{name}.out.resp", mgr);
        }
    }
#endif
}
