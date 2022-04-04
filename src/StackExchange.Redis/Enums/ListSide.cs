namespace StackExchange.Redis
{
    /// <summary>
    /// Specifies what side of the list to refer to.
    /// </summary>
    public enum ListSide
    {
        /// <summary>
        /// Referce to the head of the list.
        /// </summary>
        Left,
        /// <summary>
        /// Referce to the tail of the list.
        /// </summary>
        Right
    }
}
