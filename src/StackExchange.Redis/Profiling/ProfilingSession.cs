using System.Threading;

namespace StackExchange.Redis.Profiling
{
    /// <summary>
    /// Lightweight profiling session that can be optionally registered (via ConnectionMultiplexer.RegisterProfiler) to track messages.
    /// </summary>
    public sealed class ProfilingSession
    {
        /// <summary>
        /// Caller-defined state object.
        /// </summary>
        public object? UserToken { get; }
        /// <summary>
        /// Create a new profiling session, optionally including a caller-defined state object.
        /// </summary>
        /// <param name="userToken">The state object to use for this session.</param>
        public ProfilingSession(object? userToken = null) => UserToken = userToken;

        private object? _untypedHead;

        internal void Add(ProfiledCommand command)
        {
            if (command == null) return;

            object? cur = Thread.VolatileRead(ref _untypedHead);
            while (true)
            {
                command.NextElement = (ProfiledCommand?)cur;
                var got = Interlocked.CompareExchange(ref _untypedHead, command, cur);
                if (ReferenceEquals(got, cur)) break; // successful update
                cur = got; // retry; no need to re-fetch the field, we just did that
            }
        }

        /// <summary>
        /// Reset the session and yield the commands that were captured for enumeration; if additional commands
        /// are added, they can be retrieved via additional calls to FinishProfiling.
        /// </summary>
        public ProfiledCommandEnumerable FinishProfiling()
        {
            var head = (ProfiledCommand?)Interlocked.Exchange(ref _untypedHead, null);

            // reverse the list so everything is ordered the way the consumer expected them
            int count = 0;
            ProfiledCommand? previous = null, current = head, next;
            while (current != null)
            {
                next = current.NextElement;
                current.NextElement = previous;
                previous = current;
                current = next;
                count++;
            }

            return new ProfiledCommandEnumerable(count, previous);
        }
    }
}
