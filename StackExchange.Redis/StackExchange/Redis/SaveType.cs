namespace StackExchange.Redis
{
    /// <summary>
    /// The type of save operation to perform; note that foreground saving is not offered, as this is basically never a good thing to do through regular code.
    /// </summary>
    public enum SaveType
    {
        /// <summary>
        /// Instruct Redis to start an Append Only File rewrite process. The rewrite will create a small optimized version of the current Append Only File.
        /// </summary>
        /// <remarks>http://redis.io/commands/bgrewriteaof</remarks>
        BackgroundRewriteAppendOnlyFile,
        /// <summary>
        /// Save the DB in background. The OK code is immediately returned. Redis forks, the parent continues to serve the clients, the child saves the DB on disk then exits. A client my be able to check if the operation succeeded using the LASTSAVE command.
        /// </summary>
        /// <remarks>http://redis.io/commands/bgsave</remarks>
        BackgroundSave,
    }
}
