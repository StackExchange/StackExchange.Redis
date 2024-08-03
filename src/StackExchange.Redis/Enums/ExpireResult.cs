namespace StackExchange.Redis;

/// <summary>
/// Specifies the result of operation to set expire time.
/// </summary>
public enum ExpireResult
{
    /// <summary>
    /// Field deleted because the specified expiration time is due.
    /// </summary>
    Due = 2,

    /// <summary>
    /// Expiration time/duration updated successfully.
    /// </summary>
    Success = 1,

    /// <summary>
    /// Expiration not set because of a specified NX | XX | GT | LT condition not met.
    /// </summary>
    ConditionNotMet = 0,

    /// <summary>
    /// No such field.
    /// </summary>
    NoSuchField = -2,
}
