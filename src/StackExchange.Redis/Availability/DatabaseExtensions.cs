using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis.Availability
{
    /// <summary>
    ///     Provides availability-related extension methods (such as <see cref="WithRetry"/>) to database instances.
    /// </summary>
    public static class DatabaseExtensions
    {
        /// <summary>
        /// Automatically retry operations when connection failure occurs. This has deep integration with
        /// SE.Redis concepts, so can respond to server failover events, apply circuit-breaker rules, and
        /// respect command effect categorization.
        /// </summary>
        [Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
        public static IDatabaseAsync WithRetry(this IDatabaseAsync database, RetryPolicy retryPolicy)
            => new RetryDatabase(database, retryPolicy);
    }
}
