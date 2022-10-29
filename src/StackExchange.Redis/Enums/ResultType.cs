namespace StackExchange.Redis
{
    /// <summary>
    /// The underlying result type as defined by Redis.
    /// </summary>
    public enum ResultType : byte
    {
        /// <summary>
        /// No value was received.
        /// </summary>
        None = 0,
        /// <summary>
        /// Basic strings typically represent status results such as "OK".
        /// </summary>
        SimpleString = 1,
        /// <summary>
        /// Error strings represent invalid operation results from the server.
        /// </summary>
        Error = 2,
        /// <summary>
        /// Integers are returned for count operations and some integer-based increment operations.
        /// </summary>
        Integer = 3,
        /// <summary>
        /// Bulk strings represent typical user content values.
        /// </summary>
        BulkString = 4,
        /// <summary>
        /// Multi-bulk replies represent complex results such as arrays.
        /// </summary>
        MultiBulk = 5,
    }
}
