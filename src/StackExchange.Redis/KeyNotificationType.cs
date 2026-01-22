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
    Copy = 1,
    Del = 2,
    Expire = 3,
    HDel = 4,
    HExpired = 5,
    HIncrByFloat = 6,
    HIncrBy = 7,
    HPersist = 8,
    HSet = 9,
    IncrByFloat = 10,
    IncrBy = 11,
    LInsert = 12,
    LPop = 13,
    LPush = 14,
    LRem = 15,
    LSet = 16,
    LTrim = 17,
    MoveFrom = 18,
    MoveTo = 19,
    Persist = 20,
    RenameFrom = 21,
    RenameTo = 22,
    Restore = 23,
    RPop = 24,
    RPush = 25,
    SAdd = 26,
    Set = 27,
    SetRange = 28,
    SortStore = 29,
    SRem = 30,
    SPop = 31,
    XAdd = 32,
    XDel = 33,
    XGroupCreateConsumer = 34,
    XGroupCreate = 35,
    XGroupDelConsumer = 36,
    XGroupDestroy = 37,
    XGroupSetId = 38,
    XSetId = 39,
    XTrim = 40,
    ZAdd = 41,
    ZDiffStore = 42,
    ZInterStore = 43,
    ZUnionStore = 44,
    ZIncr = 45,
    ZRemByRank = 46,
    ZRemByScore = 47,
    ZRem = 48,

    // side-effect notifications
    Expired = 1000,
    Evicted = 1001,
    New = 1002,
    Overwritten = 1003,
    TypeChanged = 1004, // type_changed
#pragma warning restore CS1591 // docs, redundant
}
