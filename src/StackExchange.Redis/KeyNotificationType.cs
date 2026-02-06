namespace StackExchange.Redis;

/// <summary>
/// The type of keyspace or keyevent notification.
/// </summary>
public enum KeyNotificationType
{
    // note: initially presented alphabetically, but: new values *must* be appended, not inserted
    // (to preserve values of existing elements)
#pragma warning disable CS1591 // docs, redundant
    Unknown = 0,
    Append = 1,
    Copy = 2,
    Del = 3,
    Expire = 4,
    HDel = 5,
    HExpired = 6,
    HIncrByFloat = 7,
    HIncrBy = 8,
    HPersist = 9,
    HSet = 10,
    IncrByFloat = 11,
    IncrBy = 12,
    LInsert = 13,
    LPop = 14,
    LPush = 15,
    LRem = 16,
    LSet = 17,
    LTrim = 18,
    MoveFrom = 19,
    MoveTo = 20,
    Persist = 21,
    RenameFrom = 22,
    RenameTo = 23,
    Restore = 24,
    RPop = 25,
    RPush = 26,
    SAdd = 27,
    Set = 28,
    SetRange = 29,
    SortStore = 30,
    SRem = 31,
    SPop = 32,
    XAdd = 33,
    XDel = 34,
    XGroupCreateConsumer = 35,
    XGroupCreate = 36,
    XGroupDelConsumer = 37,
    XGroupDestroy = 38,
    XGroupSetId = 39,
    XSetId = 40,
    XTrim = 41,
    ZAdd = 42,
    ZDiffStore = 43,
    ZInterStore = 44,
    ZUnionStore = 45,
    ZIncr = 46,
    ZRemByRank = 47,
    ZRemByScore = 48,
    ZRem = 49,

    // side-effect notifications
    Expired = 1000,
    Evicted = 1001,
    New = 1002,
    Overwritten = 1003,
    TypeChanged = 1004, // type_changed
#pragma warning restore CS1591 // docs, redundant
}
