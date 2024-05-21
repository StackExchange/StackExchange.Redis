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
    DontCreate = 1,

    /// <summary>
    ///  When DCF (“Don’t Create Fields”) is specified: for each specified field: if the field already exists: set the field's value and expiration time; ignore fields that do not exist
    /// </summary>
    DontCreateFields = 2,

    /// <summary>
    ///   When DOF (“Don’t Overwrite Fields”) is specified: for each specified field: if such field does not exist: create field and set its value and expiration time; ignore fields that already exists  
    /// </summary>
    DontOverwriteFields = 4,

    /// <summary>
    /// When GETNEW is specified: returns the new value of given fields  
    /// </summary>
    GetNew = 8,

    /// <summary>
    /// When GETOLD is specified: returns the old value of given fields  
    /// </summary>
    GetOld = 16,
}

internal static class HashFieldFlagsExtensions
{
    internal static bool HasAny(this HashFieldFlags value, HashFieldFlags flag) => (value & flag) != 0;
    internal static List<RedisValue> ToRedisValueList(this HashFieldFlags flags)
    {
        List<RedisValue> values = new();
        if (flags == HashFieldFlags.None) return values;
        if (flags.HasAny(HashFieldFlags.DontCreate)) values.Add(RedisLiterals.DC);
        if (flags.HasAny(HashFieldFlags.DontCreateFields)) values.Add(RedisLiterals.DCF);
        if (flags.HasAny(HashFieldFlags.DontOverwriteFields)) values.Add(RedisLiterals.DOF);
        if (flags.HasAny(HashFieldFlags.GetNew)) values.Add(RedisLiterals.GETNEW);
        if (flags.HasAny(HashFieldFlags.GetOld)) values.Add(RedisLiterals.GETOLD);
        return values;
    }
}

