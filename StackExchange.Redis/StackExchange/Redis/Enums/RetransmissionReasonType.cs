namespace StackExchange.Redis
{
    /// <summary>
    /// If an IProfiledCommand is a retransmission of a previous command, this enum
    /// is used to indicate what prompted the retransmission.
    /// 
    /// This can be used to distinguish between transient causes (moving hashslots, joining nodes, etc.)
    /// and incorrect routing.
    /// </summary>
    public enum RetransmissionReasonType
    {
        /// <summary>
        /// No stated reason
        /// </summary>
        None = 0,
        /// <summary>
        /// Issued to investigate which node owns a key
        /// </summary>
        Ask,
        /// <summary>
        /// A node has indicated that it does *not* own the given key
        /// </summary>
        Moved
    }
}
