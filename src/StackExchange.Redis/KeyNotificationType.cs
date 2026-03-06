using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// The type of keyspace or keyevent notification.
/// </summary>
[AsciiHash(nameof(KeyNotificationTypeMetadata))]
public enum KeyNotificationType
{
    // note: initially presented alphabetically, but: new values *must* be appended, not inserted
    // (to preserve values of existing elements)
#pragma warning disable CS1591 // docs, redundant
    [AsciiHash("")]
    Unknown = 0,
    [AsciiHash("append")]
    Append = 1,
    [AsciiHash("copy")]
    Copy = 2,
    [AsciiHash("del")]
    Del = 3,
    [AsciiHash("expire")]
    Expire = 4,
    [AsciiHash("hdel")]
    HDel = 5,
    [AsciiHash("hexpired")]
    HExpired = 6,
    [AsciiHash("hincrbyfloat")]
    HIncrByFloat = 7,
    [AsciiHash("hincrby")]
    HIncrBy = 8,
    [AsciiHash("hpersist")]
    HPersist = 9,
    [AsciiHash("hset")]
    HSet = 10,
    [AsciiHash("incrbyfloat")]
    IncrByFloat = 11,
    [AsciiHash("incrby")]
    IncrBy = 12,
    [AsciiHash("linsert")]
    LInsert = 13,
    [AsciiHash("lpop")]
    LPop = 14,
    [AsciiHash("lpush")]
    LPush = 15,
    [AsciiHash("lrem")]
    LRem = 16,
    [AsciiHash("lset")]
    LSet = 17,
    [AsciiHash("ltrim")]
    LTrim = 18,
    [AsciiHash("move_from")]
    MoveFrom = 19,
    [AsciiHash("move_to")]
    MoveTo = 20,
    [AsciiHash("persist")]
    Persist = 21,
    [AsciiHash("rename_from")]
    RenameFrom = 22,
    [AsciiHash("rename_to")]
    RenameTo = 23,
    [AsciiHash("restore")]
    Restore = 24,
    [AsciiHash("rpop")]
    RPop = 25,
    [AsciiHash("rpush")]
    RPush = 26,
    [AsciiHash("sadd")]
    SAdd = 27,
    [AsciiHash("set")]
    Set = 28,
    [AsciiHash("setrange")]
    SetRange = 29,
    [AsciiHash("sortstore")]
    SortStore = 30,
    [AsciiHash("srem")]
    SRem = 31,
    [AsciiHash("spop")]
    SPop = 32,
    [AsciiHash("xadd")]
    XAdd = 33,
    [AsciiHash("xdel")]
    XDel = 34,
    [AsciiHash("xgroup-createconsumer")]
    XGroupCreateConsumer = 35,
    [AsciiHash("xgroup-create")]
    XGroupCreate = 36,
    [AsciiHash("xgroup-delconsumer")]
    XGroupDelConsumer = 37,
    [AsciiHash("xgroup-destroy")]
    XGroupDestroy = 38,
    [AsciiHash("xgroup-setid")]
    XGroupSetId = 39,
    [AsciiHash("xsetid")]
    XSetId = 40,
    [AsciiHash("xtrim")]
    XTrim = 41,
    [AsciiHash("zadd")]
    ZAdd = 42,
    [AsciiHash("zdiffstore")]
    ZDiffStore = 43,
    [AsciiHash("zinterstore")]
    ZInterStore = 44,
    [AsciiHash("zunionstore")]
    ZUnionStore = 45,
    [AsciiHash("zincr")]
    ZIncr = 46,
    [AsciiHash("zrembyrank")]
    ZRemByRank = 47,
    [AsciiHash("zrembyscore")]
    ZRemByScore = 48,
    [AsciiHash("zrem")]
    ZRem = 49,

    // side-effect notifications
    [AsciiHash("expired")]
    Expired = 1000,
    [AsciiHash("evicted")]
    Evicted = 1001,
    [AsciiHash("new")]
    New = 1002,
    [AsciiHash("overwritten")]
    Overwritten = 1003,
    [AsciiHash("type_changed")]
    TypeChanged = 1004,
#pragma warning restore CS1591 // docs, redundant
}
