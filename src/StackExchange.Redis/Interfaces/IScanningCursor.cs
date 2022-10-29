namespace StackExchange.Redis
{
    /// <summary>
    /// Represents a resumable, cursor-based scanning operation.
    /// </summary>
    public interface IScanningCursor
    {
        /// <summary>
        /// Returns the cursor that represents the *active* page of results (not the pending/next page of results as returned by SCAN/HSCAN/ZSCAN/SSCAN).
        /// </summary>
        long Cursor { get; }

        /// <summary>
        /// The page size of the current operation.
        /// </summary>
        int PageSize { get; }

        /// <summary>
        /// The offset into the current page.
        /// </summary>
        int PageOffset { get; }
    }
}
