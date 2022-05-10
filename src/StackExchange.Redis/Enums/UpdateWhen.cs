namespace StackExchange.Redis
{
    /// <summary>
    /// Indicates when this operation should be performed (only some variations are legal in a given context).
    /// </summary>
    public enum UpdateWhen
    {
        /// <summary>
        /// The operation won't be prevented.
        /// </summary>
        Always,
        /// <summary>
        /// The operation should only occur when the new score is greater than the current score.
        /// </summary>
        GreaterThan,
        /// <summary>
        /// The operation should only occur when the new score is less than the current score.
        /// </summary>
        LessThan,
    }
}
