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
