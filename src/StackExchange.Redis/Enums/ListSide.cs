namespace StackExchange.Redis
{
    /// <summary>
    /// Specifies what side of the list to refer to.
    /// </summary>
    public enum ListSide
    {
        /// <summary>
        /// The head of the list.
        /// </summary>
        Left,
        /// <summary>
        /// The tail of the list.
        /// </summary>
        Right,
    }
}
