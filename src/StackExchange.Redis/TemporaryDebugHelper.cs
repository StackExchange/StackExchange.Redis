using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Temporary class to help debug an issue, see https://github.com/StackExchange/StackExchange.Redis/issues/1438
    /// </summary>
    public static class TemporaryDebugHelper
    {
        /// <summary>
        /// The last time a sync flush had timed out, if at all
        /// </summary>
        public static DateTime? LastThrownFlushSyncTimeoutUtc { get; set; }

        /// <summary>
        /// Create a new error to be thrown with extra debugging information included in its message
        /// </summary>
        public static InvalidOperationException CreateDetailedException(string additionalErrorInformation, Exception innerException)
        {
            var lastTimedOutAgo = LastThrownFlushSyncTimeoutUtc != null ? DateTime.UtcNow - LastThrownFlushSyncTimeoutUtc.Value : (TimeSpan?) null;

            throw new InvalidOperationException($"Encountered error discussed on SE.Redis GitHub #1438 [{additionalErrorInformation} | LastThrownFlushSyncTimeoutUtc: {LastThrownFlushSyncTimeoutUtc} ({lastTimedOutAgo})]", innerException);
        }
    }
}
