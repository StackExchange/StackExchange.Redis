namespace StackExchange.Redis;

/// <summary>
/// Possible commands to use on a bitfield.
/// </summary>
public enum BitfieldCommand
{
    /// <summary>
    /// retrieves the specified integer from the bitfield.
    /// </summary>
    GET,
    /// <summary>
    /// Set's the bitfield.
    /// </summary>
    SET,
    /// <summary>
    /// Increments the bitfield.
    /// </summary>
    INCRBY
}
