using System;
using System.Collections.Generic;
using System.Linq;

namespace StackExchange.Redis;

/// <summary>
///  Specifies the options to the HSETF command when to create hash/fields and the return data
/// </summary>
[Flags]
public enum HashFieldFlags
{

    /// <summary>
    /// No options specified.
    /// </summary>
    None = 0,
    /// <summary>
    /// When DC (“Don’t Create”) is specified: if key does not exist: do nothing (don’t create key)  
    /// </summary>
    DC = 1,
    /// <summary>
    ///  When DCF (“Don’t Create Fields”) is specified: for each specified field: if the field already exists: set the field's value and expiration time; ignore fields that do not exist
    /// </summary>
    DCF = 2,
    /// <summary>
    ///   When DOF (“Don’t Overwrite Fields”) is specified: for each specified field: if such field does not exist: create field and set its value and expiration time; ignore fields that already exists  
    /// </summary>
    DOF = 4,
    /// <summary>
    /// When GETNEW is specified: returns the new value of given fields  
    /// </summary>
    GETNEW = 8,
    /// <summary>
    /// When GETOLD is specified: returns the old value of given fields  
    /// </summary>
    GETOLD = 16,
}

internal static class HashFieldFlagsExtensions
{
    internal static bool isNone(this HashFieldFlags flags) =>
        flags == HashFieldFlags.None;
    internal static bool isDC(this HashFieldFlags flags) =>
        flags.HasFlag(HashFieldFlags.DC);

    internal static bool isDCF(this HashFieldFlags flags) =>
        flags.HasFlag(HashFieldFlags.DCF);

    internal static bool isDOF(this HashFieldFlags flags) =>
        flags.HasFlag(HashFieldFlags.DOF);

    internal static bool isGETNEW(this HashFieldFlags flags) =>
        flags.HasFlag(HashFieldFlags.GETNEW);

    internal static bool isGETOLD(this HashFieldFlags flags) =>
        flags.HasFlag(HashFieldFlags.GETOLD);

    internal static List<RedisValue> ToRedisValueList(this HashFieldFlags flags) =>
        flags.isNone() ? new List<RedisValue>() : flags.ToString().Split(',').Select(v => (RedisValue)v).ToList();

}

