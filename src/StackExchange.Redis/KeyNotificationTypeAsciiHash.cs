using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Internal helper type for fast parsing of key notification types, using [AsciiHash].
/// </summary>
internal static partial class KeyNotificationTypeAsciiHash
{
    // these are checked by KeyNotificationTypeAsciiHash_MinMaxBytes_ReflectsActualLengths
    public const int MinBytes = 3, MaxBytes = 21;

    public static KeyNotificationType Parse(ReadOnlySpan<byte> value)
    {
        var hashCS = AsciiHash.HashCS(value);
        return hashCS switch
        {
            append.HashCS when append.IsCS(value, hashCS) => KeyNotificationType.Append,
            copy.HashCS when copy.IsCS(value, hashCS) => KeyNotificationType.Copy,
            del.HashCS when del.IsCS(value, hashCS) => KeyNotificationType.Del,
            expire.HashCS when expire.IsCS(value, hashCS) => KeyNotificationType.Expire,
            hdel.HashCS when hdel.IsCS(value, hashCS) => KeyNotificationType.HDel,
            hexpired.HashCS when hexpired.IsCS(value, hashCS) => KeyNotificationType.HExpired,
            hincrbyfloat.HashCS when hincrbyfloat.IsCS(value, hashCS) => KeyNotificationType.HIncrByFloat,
            hincrby.HashCS when hincrby.IsCS(value, hashCS) => KeyNotificationType.HIncrBy,
            hpersist.HashCS when hpersist.IsCS(value, hashCS) => KeyNotificationType.HPersist,
            hset.HashCS when hset.IsCS(value, hashCS) => KeyNotificationType.HSet,
            incrbyfloat.HashCS when incrbyfloat.IsCS(value, hashCS) => KeyNotificationType.IncrByFloat,
            incrby.HashCS when incrby.IsCS(value, hashCS) => KeyNotificationType.IncrBy,
            linsert.HashCS when linsert.IsCS(value, hashCS) => KeyNotificationType.LInsert,
            lpop.HashCS when lpop.IsCS(value, hashCS) => KeyNotificationType.LPop,
            lpush.HashCS when lpush.IsCS(value, hashCS) => KeyNotificationType.LPush,
            lrem.HashCS when lrem.IsCS(value, hashCS) => KeyNotificationType.LRem,
            lset.HashCS when lset.IsCS(value, hashCS) => KeyNotificationType.LSet,
            ltrim.HashCS when ltrim.IsCS(value, hashCS) => KeyNotificationType.LTrim,
            move_from.HashCS when move_from.IsCS(value, hashCS) => KeyNotificationType.MoveFrom,
            move_to.HashCS when move_to.IsCS(value, hashCS) => KeyNotificationType.MoveTo,
            persist.HashCS when persist.IsCS(value, hashCS) => KeyNotificationType.Persist,
            rename_from.HashCS when rename_from.IsCS(value, hashCS) => KeyNotificationType.RenameFrom,
            rename_to.HashCS when rename_to.IsCS(value, hashCS) => KeyNotificationType.RenameTo,
            restore.HashCS when restore.IsCS(value, hashCS) => KeyNotificationType.Restore,
            rpop.HashCS when rpop.IsCS(value, hashCS) => KeyNotificationType.RPop,
            rpush.HashCS when rpush.IsCS(value, hashCS) => KeyNotificationType.RPush,
            sadd.HashCS when sadd.IsCS(value, hashCS) => KeyNotificationType.SAdd,
            set.HashCS when set.IsCS(value, hashCS) => KeyNotificationType.Set,
            setrange.HashCS when setrange.IsCS(value, hashCS) => KeyNotificationType.SetRange,
            sortstore.HashCS when sortstore.IsCS(value, hashCS) => KeyNotificationType.SortStore,
            srem.HashCS when srem.IsCS(value, hashCS) => KeyNotificationType.SRem,
            spop.HashCS when spop.IsCS(value, hashCS) => KeyNotificationType.SPop,
            xadd.HashCS when xadd.IsCS(value, hashCS) => KeyNotificationType.XAdd,
            xdel.HashCS when xdel.IsCS(value, hashCS) => KeyNotificationType.XDel,
            xgroup_createconsumer.HashCS when xgroup_createconsumer.IsCS(value, hashCS) => KeyNotificationType.XGroupCreateConsumer,
            xgroup_create.HashCS when xgroup_create.IsCS(value, hashCS) => KeyNotificationType.XGroupCreate,
            xgroup_delconsumer.HashCS when xgroup_delconsumer.IsCS(value, hashCS) => KeyNotificationType.XGroupDelConsumer,
            xgroup_destroy.HashCS when xgroup_destroy.IsCS(value, hashCS) => KeyNotificationType.XGroupDestroy,
            xgroup_setid.HashCS when xgroup_setid.IsCS(value, hashCS) => KeyNotificationType.XGroupSetId,
            xsetid.HashCS when xsetid.IsCS(value, hashCS) => KeyNotificationType.XSetId,
            xtrim.HashCS when xtrim.IsCS(value, hashCS) => KeyNotificationType.XTrim,
            zadd.HashCS when zadd.IsCS(value, hashCS) => KeyNotificationType.ZAdd,
            zdiffstore.HashCS when zdiffstore.IsCS(value, hashCS) => KeyNotificationType.ZDiffStore,
            zinterstore.HashCS when zinterstore.IsCS(value, hashCS) => KeyNotificationType.ZInterStore,
            zunionstore.HashCS when zunionstore.IsCS(value, hashCS) => KeyNotificationType.ZUnionStore,
            zincr.HashCS when zincr.IsCS(value, hashCS) => KeyNotificationType.ZIncr,
            zrembyrank.HashCS when zrembyrank.IsCS(value, hashCS) => KeyNotificationType.ZRemByRank,
            zrembyscore.HashCS when zrembyscore.IsCS(value, hashCS) => KeyNotificationType.ZRemByScore,
            zrem.HashCS when zrem.IsCS(value, hashCS) => KeyNotificationType.ZRem,
            expired.HashCS when expired.IsCS(value, hashCS) => KeyNotificationType.Expired,
            evicted.HashCS when evicted.IsCS(value, hashCS) => KeyNotificationType.Evicted,
            _new.HashCS when _new.IsCS(value, hashCS) => KeyNotificationType.New,
            overwritten.HashCS when overwritten.IsCS(value, hashCS) => KeyNotificationType.Overwritten,
            type_changed.HashCS when type_changed.IsCS(value, hashCS) => KeyNotificationType.TypeChanged,
            _ => KeyNotificationType.Unknown,
        };
    }

    internal static ReadOnlySpan<byte> GetRawBytes(KeyNotificationType type)
    {
        return type switch
        {
            KeyNotificationType.Append => append.U8,
            KeyNotificationType.Copy => copy.U8,
            KeyNotificationType.Del => del.U8,
            KeyNotificationType.Expire => expire.U8,
            KeyNotificationType.HDel => hdel.U8,
            KeyNotificationType.HExpired => hexpired.U8,
            KeyNotificationType.HIncrByFloat => hincrbyfloat.U8,
            KeyNotificationType.HIncrBy => hincrby.U8,
            KeyNotificationType.HPersist => hpersist.U8,
            KeyNotificationType.HSet => hset.U8,
            KeyNotificationType.IncrByFloat => incrbyfloat.U8,
            KeyNotificationType.IncrBy => incrby.U8,
            KeyNotificationType.LInsert => linsert.U8,
            KeyNotificationType.LPop => lpop.U8,
            KeyNotificationType.LPush => lpush.U8,
            KeyNotificationType.LRem => lrem.U8,
            KeyNotificationType.LSet => lset.U8,
            KeyNotificationType.LTrim => ltrim.U8,
            KeyNotificationType.MoveFrom => move_from.U8,
            KeyNotificationType.MoveTo => move_to.U8,
            KeyNotificationType.Persist => persist.U8,
            KeyNotificationType.RenameFrom => rename_from.U8,
            KeyNotificationType.RenameTo => rename_to.U8,
            KeyNotificationType.Restore => restore.U8,
            KeyNotificationType.RPop => rpop.U8,
            KeyNotificationType.RPush => rpush.U8,
            KeyNotificationType.SAdd => sadd.U8,
            KeyNotificationType.Set => set.U8,
            KeyNotificationType.SetRange => setrange.U8,
            KeyNotificationType.SortStore => sortstore.U8,
            KeyNotificationType.SRem => srem.U8,
            KeyNotificationType.SPop => spop.U8,
            KeyNotificationType.XAdd => xadd.U8,
            KeyNotificationType.XDel => xdel.U8,
            KeyNotificationType.XGroupCreateConsumer => xgroup_createconsumer.U8,
            KeyNotificationType.XGroupCreate => xgroup_create.U8,
            KeyNotificationType.XGroupDelConsumer => xgroup_delconsumer.U8,
            KeyNotificationType.XGroupDestroy => xgroup_destroy.U8,
            KeyNotificationType.XGroupSetId => xgroup_setid.U8,
            KeyNotificationType.XSetId => xsetid.U8,
            KeyNotificationType.XTrim => xtrim.U8,
            KeyNotificationType.ZAdd => zadd.U8,
            KeyNotificationType.ZDiffStore => zdiffstore.U8,
            KeyNotificationType.ZInterStore => zinterstore.U8,
            KeyNotificationType.ZUnionStore => zunionstore.U8,
            KeyNotificationType.ZIncr => zincr.U8,
            KeyNotificationType.ZRemByRank => zrembyrank.U8,
            KeyNotificationType.ZRemByScore => zrembyscore.U8,
            KeyNotificationType.ZRem => zrem.U8,
            KeyNotificationType.Expired => expired.U8,
            KeyNotificationType.Evicted => evicted.U8,
            KeyNotificationType.New => _new.U8,
            KeyNotificationType.Overwritten => overwritten.U8,
            KeyNotificationType.TypeChanged => type_changed.U8,
            _ => Throw(),
        };
        static ReadOnlySpan<byte> Throw() => throw new ArgumentOutOfRangeException(nameof(type));
    }

#pragma warning disable SA1300, CS8981
    // ReSharper disable InconsistentNaming
    [AsciiHash]
    internal static partial class append
    {
    }

    [AsciiHash]
    internal static partial class copy
    {
    }

    [AsciiHash]
    internal static partial class del
    {
    }

    [AsciiHash]
    internal static partial class expire
    {
    }

    [AsciiHash]
    internal static partial class hdel
    {
    }

    [AsciiHash]
    internal static partial class hexpired
    {
    }

    [AsciiHash]
    internal static partial class hincrbyfloat
    {
    }

    [AsciiHash]
    internal static partial class hincrby
    {
    }

    [AsciiHash]
    internal static partial class hpersist
    {
    }

    [AsciiHash]
    internal static partial class hset
    {
    }

    [AsciiHash]
    internal static partial class incrbyfloat
    {
    }

    [AsciiHash]
    internal static partial class incrby
    {
    }

    [AsciiHash]
    internal static partial class linsert
    {
    }

    [AsciiHash]
    internal static partial class lpop
    {
    }

    [AsciiHash]
    internal static partial class lpush
    {
    }

    [AsciiHash]
    internal static partial class lrem
    {
    }

    [AsciiHash]
    internal static partial class lset
    {
    }

    [AsciiHash]
    internal static partial class ltrim
    {
    }

    [AsciiHash("move_from")] // by default, the generator interprets underscore as hyphen
    internal static partial class move_from
    {
    }

    [AsciiHash("move_to")] // by default, the generator interprets underscore as hyphen
    internal static partial class move_to
    {
    }

    [AsciiHash]
    internal static partial class persist
    {
    }

    [AsciiHash("rename_from")] // by default, the generator interprets underscore as hyphen
    internal static partial class rename_from
    {
    }

    [AsciiHash("rename_to")] // by default, the generator interprets underscore as hyphen
    internal static partial class rename_to
    {
    }

    [AsciiHash]
    internal static partial class restore
    {
    }

    [AsciiHash]
    internal static partial class rpop
    {
    }

    [AsciiHash]
    internal static partial class rpush
    {
    }

    [AsciiHash]
    internal static partial class sadd
    {
    }

    [AsciiHash]
    internal static partial class set
    {
    }

    [AsciiHash]
    internal static partial class setrange
    {
    }

    [AsciiHash]
    internal static partial class sortstore
    {
    }

    [AsciiHash]
    internal static partial class srem
    {
    }

    [AsciiHash]
    internal static partial class spop
    {
    }

    [AsciiHash]
    internal static partial class xadd
    {
    }

    [AsciiHash]
    internal static partial class xdel
    {
    }

    [AsciiHash] // note: becomes hyphenated
    internal static partial class xgroup_createconsumer
    {
    }

    [AsciiHash] // note: becomes hyphenated
    internal static partial class xgroup_create
    {
    }

    [AsciiHash] // note: becomes hyphenated
    internal static partial class xgroup_delconsumer
    {
    }

    [AsciiHash] // note: becomes hyphenated
    internal static partial class xgroup_destroy
    {
    }

    [AsciiHash] // note: becomes hyphenated
    internal static partial class xgroup_setid
    {
    }

    [AsciiHash]
    internal static partial class xsetid
    {
    }

    [AsciiHash]
    internal static partial class xtrim
    {
    }

    [AsciiHash]
    internal static partial class zadd
    {
    }

    [AsciiHash]
    internal static partial class zdiffstore
    {
    }

    [AsciiHash]
    internal static partial class zinterstore
    {
    }

    [AsciiHash]
    internal static partial class zunionstore
    {
    }

    [AsciiHash]
    internal static partial class zincr
    {
    }

    [AsciiHash]
    internal static partial class zrembyrank
    {
    }

    [AsciiHash]
    internal static partial class zrembyscore
    {
    }

    [AsciiHash]
    internal static partial class zrem
    {
    }

    [AsciiHash]
    internal static partial class expired
    {
    }

    [AsciiHash]
    internal static partial class evicted
    {
    }

    [AsciiHash("new")]
    internal static partial class _new // it isn't worth making the code-gen keyword aware
    {
    }

    [AsciiHash]
    internal static partial class overwritten
    {
    }

    [AsciiHash("type_changed")] // by default, the generator interprets underscore as hyphen
    internal static partial class type_changed
    {
    }

    // ReSharper restore InconsistentNaming
#pragma warning restore SA1300, CS8981
}
