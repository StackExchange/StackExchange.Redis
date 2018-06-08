using System;

namespace StackExchange.Redis
{
    public partial class ConnectionMultiplexer
    {
        private IProfiler profiler;

        // internal for test purposes
        internal ProfileContextTracker profiledCommands;

        /// <summary>
        /// Sets an IProfiler instance for this ConnectionMultiplexer.
        /// 
        /// An IProfiler instances is used to determine which context to associate an
        /// IProfiledCommand with.  See BeginProfiling(object) and FinishProfiling(object)
        /// for more details.
        /// </summary>
        /// <param name="profiler">The profiler to register.</param>
        public void RegisterProfiler(IProfiler profiler)
        {
            if (this.profiler != null) throw new InvalidOperationException("IProfiler already registered for this ConnectionMultiplexer");

            this.profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
            profiledCommands = new ProfileContextTracker();
        }

        /// <summary>
        /// Begins profiling for the given context.
        /// 
        /// If the same context object is returned by the registered IProfiler, the IProfiledCommands
        /// will be associated with each other.
        /// 
        /// Call FinishProfiling with the same context to get the assocated commands.
        /// 
        /// Note that forContext cannot be a WeakReference or a WeakReference&lt;T&gt;
        /// </summary>
        /// <param name="forContext">The context to begin profiling.</param>
        public void BeginProfiling(object forContext)
        {
            if (profiler == null) throw new InvalidOperationException("Cannot begin profiling if no IProfiler has been registered with RegisterProfiler");
            if (forContext == null) throw new ArgumentNullException(nameof(forContext));
            if (forContext is WeakReference) throw new ArgumentException("Context object cannot be a WeakReference", nameof(forContext));

            if (!profiledCommands.TryCreate(forContext))
            {
                throw ExceptionFactory.BeganProfilingWithDuplicateContext(forContext);
            }
        }

        /// <summary>
        /// Stops profiling for the given context, returns all IProfiledCommands associated.
        /// 
        /// By default this may do a sweep for dead profiling contexts, you can disable this by passing "allowCleanupSweep: false".
        /// </summary>
        /// <param name="forContext">The context to begin profiling.</param>
        /// <param name="allowCleanupSweep">Whether to allow cleanup of old profiling sessions.</param>
        public ProfiledCommandEnumerable FinishProfiling(object forContext, bool allowCleanupSweep = true)
        {
            if (profiler == null) throw new InvalidOperationException("Cannot begin profiling if no IProfiler has been registered with RegisterProfiler");
            if (forContext == null) throw new ArgumentNullException(nameof(forContext));

            if (!profiledCommands.TryRemove(forContext, out ProfiledCommandEnumerable ret))
            {
                throw ExceptionFactory.FinishedProfilingWithInvalidContext(forContext);
            }

            // conditional, because it could hurt and that may sometimes be unacceptable
            if (allowCleanupSweep)
            {
                profiledCommands.TryCleanup();
            }

            return ret;
        }
    }
}
