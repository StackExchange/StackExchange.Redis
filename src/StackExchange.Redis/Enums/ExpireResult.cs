using System;

namespace StackExchange.Redis;

/// <summary>
/// Specifies when to set the expiry for a key.
/// </summary>
public enum ExpireResult
{
    /// <summary>
    /// Set expiry whether or not there is an existing expiry.
    /// </summary>
    Due = 2,
    /// <summary>
    /// Set expiry only when the new expiry is greater than current one.
    /// </summary>
    Success = 1,
    /// <summary>
    /// Set expiry only when the key has an existing expiry.
    /// </summary>
    ConditionNotMet = 0,
    /// <summary>
    /// Set expiry only when the key has no expiry.
    /// </summary>
    NoSuchField = -2,

}
