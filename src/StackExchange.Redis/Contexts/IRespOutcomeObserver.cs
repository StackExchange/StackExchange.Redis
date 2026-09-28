using System;

namespace StackExchange.Redis
{
    /// <summary>Told how each command ended, for availability accounting.</summary>
    /// <remarks>
    /// <b>An interface rather than the executor type</b>, because the thing being observed is an outcome
    /// and the thing counting them is a policy: a test breaker, a group's shared breaker, or nothing at
    /// all. The operation should not know which.
    /// </remarks>
    internal interface IRespOutcomeObserver
    {
        /// <summary>Record how one command ended.</summary>
        /// <param name="fault">The failure, or null if it succeeded.</param>
        void ObserveOutcome(Exception? fault);
    }
}
