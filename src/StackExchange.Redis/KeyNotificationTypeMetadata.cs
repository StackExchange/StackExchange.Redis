using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Metadata and parsing methods for KeyNotificationType.
/// </summary>
internal static partial class KeyNotificationTypeMetadata
{
    [AsciiHash]
    internal static partial bool TryParse(ReadOnlySpan<byte> value, out KeyNotificationType keyNotificationType);

    public static KeyNotificationType Parse(ReadOnlySpan<byte> value)
    {
        return TryParse(value, out var result) ? result : KeyNotificationType.Unknown;
    }

    internal static ReadOnlySpan<byte> GetRawBytes(KeyNotificationType type) => type switch
    {
        KeyNotificationType.Append => "append"u8,
        KeyNotificationType.Copy => "copy"u8,
        KeyNotificationType.Del => "del"u8,
        KeyNotificationType.Expire => "expire"u8,
        KeyNotificationType.HDel => "hdel"u8,
        KeyNotificationType.HExpired => "hexpired"u8,
        KeyNotificationType.HIncrByFloat => "hincrbyfloat"u8,
        KeyNotificationType.HIncrBy => "hincrby"u8,
        KeyNotificationType.HPersist => "hpersist"u8,
        KeyNotificationType.HSet => "hset"u8,
        KeyNotificationType.IncrByFloat => "incrbyfloat"u8,
        KeyNotificationType.IncrBy => "incrby"u8,
        KeyNotificationType.LInsert => "linsert"u8,
        KeyNotificationType.LPop => "lpop"u8,
        KeyNotificationType.LPush => "lpush"u8,
        KeyNotificationType.LRem => "lrem"u8,
        KeyNotificationType.LSet => "lset"u8,
        KeyNotificationType.LTrim => "ltrim"u8,
        KeyNotificationType.MoveFrom => "move_from"u8,
        KeyNotificationType.MoveTo => "move_to"u8,
        KeyNotificationType.Persist => "persist"u8,
        KeyNotificationType.RenameFrom => "rename_from"u8,
        KeyNotificationType.RenameTo => "rename_to"u8,
        KeyNotificationType.Restore => "restore"u8,
        KeyNotificationType.RPop => "rpop"u8,
        KeyNotificationType.RPush => "rpush"u8,
        KeyNotificationType.SAdd => "sadd"u8,
        KeyNotificationType.Set => "set"u8,
        KeyNotificationType.SetRange => "setrange"u8,
        KeyNotificationType.SortStore => "sortstore"u8,
        KeyNotificationType.SRem => "srem"u8,
        KeyNotificationType.SPop => "spop"u8,
        KeyNotificationType.XAdd => "xadd"u8,
        KeyNotificationType.XDel => "xdel"u8,
        KeyNotificationType.XGroupCreateConsumer => "xgroup-createconsumer"u8,
        KeyNotificationType.XGroupCreate => "xgroup-create"u8,
        KeyNotificationType.XGroupDelConsumer => "xgroup-delconsumer"u8,
        KeyNotificationType.XGroupDestroy => "xgroup-destroy"u8,
        KeyNotificationType.XGroupSetId => "xgroup-setid"u8,
        KeyNotificationType.XSetId => "xsetid"u8,
        KeyNotificationType.XTrim => "xtrim"u8,
        KeyNotificationType.ZAdd => "zadd"u8,
        KeyNotificationType.ZDiffStore => "zdiffstore"u8,
        KeyNotificationType.ZInterStore => "zinterstore"u8,
        KeyNotificationType.ZUnionStore => "zunionstore"u8,
        KeyNotificationType.ZIncr => "zincr"u8,
        KeyNotificationType.ZRemByRank => "zrembyrank"u8,
        KeyNotificationType.ZRemByScore => "zrembyscore"u8,
        KeyNotificationType.ZRem => "zrem"u8,
        KeyNotificationType.HExpire => "hexpire"u8,
        KeyNotificationType.Expired => "expired"u8,
        KeyNotificationType.Evicted => "evicted"u8,
        KeyNotificationType.New => "new"u8,
        KeyNotificationType.Overwritten => "overwritten"u8,
        KeyNotificationType.TypeChanged => "type_changed"u8,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
