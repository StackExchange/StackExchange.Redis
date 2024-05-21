using System;

namespace StackExchange.Redis;

/// <summary>
/// Specifies the result of operation to remove the expire time.
/// </summary>
public enum PersistResult
{
    /// <summary>
    /// Expiration removed successfully
    /// </summary>
    Success = 1,
    /// <summary>
    /// Expiration not removed because of a specified NX | XX | GT | LT condition not met
    /// </summary>
    ConditionNotMet = -1,
    /// <summary>
    /// No such field.
    /// </summary>
    NoSuchField = -2,
}
