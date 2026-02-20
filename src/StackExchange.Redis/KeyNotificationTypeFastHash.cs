using System;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Internal helper type for fast parsing of key notification types, using [FastHash].
/// </summary>
internal static partial class KeyNotificationTypeFastHash
{
    // these are checked by KeyNotificationTypeFastHash_MinMaxBytes_ReflectsActualLengths
    public const int MinBytes = 3, MaxBytes = 21;

    public static KeyNotificationType Parse(ReadOnlySpan<byte> value)
    {
        var hash = value.HashCS();
        return hash switch
        {
            append.Hash when append.Is(hash, value) => KeyNotificationType.Append,
            copy.Hash when copy.Is(hash, value) => KeyNotificationType.Copy,
            del.Hash when del.Is(hash, value) => KeyNotificationType.Del,
            expire.Hash when expire.Is(hash, value) => KeyNotificationType.Expire,
            hdel.Hash when hdel.Is(hash, value) => KeyNotificationType.HDel,
            hexpired.Hash when hexpired.Is(hash, value) => KeyNotificationType.HExpired,
            hincrbyfloat.Hash when hincrbyfloat.Is(hash, value) => KeyNotificationType.HIncrByFloat,
            hincrby.Hash when hincrby.Is(hash, value) => KeyNotificationType.HIncrBy,
            hpersist.Hash when hpersist.Is(hash, value) => KeyNotificationType.HPersist,
            hset.Hash when hset.Is(hash, value) => KeyNotificationType.HSet,
            incrbyfloat.Hash when incrbyfloat.Is(hash, value) => KeyNotificationType.IncrByFloat,
            incrby.Hash when incrby.Is(hash, value) => KeyNotificationType.IncrBy,
            linsert.Hash when linsert.Is(hash, value) => KeyNotificationType.LInsert,
            lpop.Hash when lpop.Is(hash, value) => KeyNotificationType.LPop,
            lpush.Hash when lpush.Is(hash, value) => KeyNotificationType.LPush,
            lrem.Hash when lrem.Is(hash, value) => KeyNotificationType.LRem,
            lset.Hash when lset.Is(hash, value) => KeyNotificationType.LSet,
            ltrim.Hash when ltrim.Is(hash, value) => KeyNotificationType.LTrim,
            move_from.Hash when move_from.Is(hash, value) => KeyNotificationType.MoveFrom,
            move_to.Hash when move_to.Is(hash, value) => KeyNotificationType.MoveTo,
            persist.Hash when persist.Is(hash, value) => KeyNotificationType.Persist,
            rename_from.Hash when rename_from.Is(hash, value) => KeyNotificationType.RenameFrom,
            rename_to.Hash when rename_to.Is(hash, value) => KeyNotificationType.RenameTo,
            restore.Hash when restore.Is(hash, value) => KeyNotificationType.Restore,
            rpop.Hash when rpop.Is(hash, value) => KeyNotificationType.RPop,
            rpush.Hash when rpush.Is(hash, value) => KeyNotificationType.RPush,
            sadd.Hash when sadd.Is(hash, value) => KeyNotificationType.SAdd,
            set.Hash when set.Is(hash, value) => KeyNotificationType.Set,
            setrange.Hash when setrange.Is(hash, value) => KeyNotificationType.SetRange,
            sortstore.Hash when sortstore.Is(hash, value) => KeyNotificationType.SortStore,
            srem.Hash when srem.Is(hash, value) => KeyNotificationType.SRem,
            spop.Hash when spop.Is(hash, value) => KeyNotificationType.SPop,
            xadd.Hash when xadd.Is(hash, value) => KeyNotificationType.XAdd,
            xdel.Hash when xdel.Is(hash, value) => KeyNotificationType.XDel,
            xgroup_createconsumer.Hash when xgroup_createconsumer.Is(hash, value) => KeyNotificationType.XGroupCreateConsumer,
            xgroup_create.Hash when xgroup_create.Is(hash, value) => KeyNotificationType.XGroupCreate,
            xgroup_delconsumer.Hash when xgroup_delconsumer.Is(hash, value) => KeyNotificationType.XGroupDelConsumer,
            xgroup_destroy.Hash when xgroup_destroy.Is(hash, value) => KeyNotificationType.XGroupDestroy,
            xgroup_setid.Hash when xgroup_setid.Is(hash, value) => KeyNotificationType.XGroupSetId,
            xsetid.Hash when xsetid.Is(hash, value) => KeyNotificationType.XSetId,
            xtrim.Hash when xtrim.Is(hash, value) => KeyNotificationType.XTrim,
            zadd.Hash when zadd.Is(hash, value) => KeyNotificationType.ZAdd,
            zdiffstore.Hash when zdiffstore.Is(hash, value) => KeyNotificationType.ZDiffStore,
            zinterstore.Hash when zinterstore.Is(hash, value) => KeyNotificationType.ZInterStore,
            zunionstore.Hash when zunionstore.Is(hash, value) => KeyNotificationType.ZUnionStore,
            zincr.Hash when zincr.Is(hash, value) => KeyNotificationType.ZIncr,
            zrembyrank.Hash when zrembyrank.Is(hash, value) => KeyNotificationType.ZRemByRank,
            zrembyscore.Hash when zrembyscore.Is(hash, value) => KeyNotificationType.ZRemByScore,
            zrem.Hash when zrem.Is(hash, value) => KeyNotificationType.ZRem,
            expired.Hash when expired.Is(hash, value) => KeyNotificationType.Expired,
            evicted.Hash when evicted.Is(hash, value) => KeyNotificationType.Evicted,
            _new.Hash when _new.Is(hash, value) => KeyNotificationType.New,
            overwritten.Hash when overwritten.Is(hash, value) => KeyNotificationType.Overwritten,
            type_changed.Hash when type_changed.Is(hash, value) => KeyNotificationType.TypeChanged,
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
    [FastHash]
    internal static partial class append
    {
    }

    [FastHash]
    internal static partial class copy
    {
    }

    [FastHash]
    internal static partial class del
    {
    }

    [FastHash]
    internal static partial class expire
    {
    }

    [FastHash]
    internal static partial class hdel
    {
    }

    [FastHash]
    internal static partial class hexpired
    {
    }

    [FastHash]
    internal static partial class hincrbyfloat
    {
    }

    [FastHash]
    internal static partial class hincrby
    {
    }

    [FastHash]
    internal static partial class hpersist
    {
    }

    [FastHash]
    internal static partial class hset
    {
    }

    [FastHash]
    internal static partial class incrbyfloat
    {
    }

    [FastHash]
    internal static partial class incrby
    {
    }

    [FastHash]
    internal static partial class linsert
    {
    }

    [FastHash]
    internal static partial class lpop
    {
    }

    [FastHash]
    internal static partial class lpush
    {
    }

    [FastHash]
    internal static partial class lrem
    {
    }

    [FastHash]
    internal static partial class lset
    {
    }

    [FastHash]
    internal static partial class ltrim
    {
    }

    [FastHash("move_from")] // by default, the generator interprets underscore as hyphen
    internal static partial class move_from
    {
    }

    [FastHash("move_to")] // by default, the generator interprets underscore as hyphen
    internal static partial class move_to
    {
    }

    [FastHash]
    internal static partial class persist
    {
    }

    [FastHash("rename_from")] // by default, the generator interprets underscore as hyphen
    internal static partial class rename_from
    {
    }

    [FastHash("rename_to")] // by default, the generator interprets underscore as hyphen
    internal static partial class rename_to
    {
    }

    [FastHash]
    internal static partial class restore
    {
    }

    [FastHash]
    internal static partial class rpop
    {
    }

    [FastHash]
    internal static partial class rpush
    {
    }

    [FastHash]
    internal static partial class sadd
    {
    }

    [FastHash]
    internal static partial class set
    {
    }

    [FastHash]
    internal static partial class setrange
    {
    }

    [FastHash]
    internal static partial class sortstore
    {
    }

    [FastHash]
    internal static partial class srem
    {
    }

    [FastHash]
    internal static partial class spop
    {
    }

    [FastHash]
    internal static partial class xadd
    {
    }

    [FastHash]
    internal static partial class xdel
    {
    }

    [FastHash] // note: becomes hyphenated
    internal static partial class xgroup_createconsumer
    {
    }

    [FastHash] // note: becomes hyphenated
    internal static partial class xgroup_create
    {
    }

    [FastHash] // note: becomes hyphenated
    internal static partial class xgroup_delconsumer
    {
    }

    [FastHash] // note: becomes hyphenated
    internal static partial class xgroup_destroy
    {
    }

    [FastHash] // note: becomes hyphenated
    internal static partial class xgroup_setid
    {
    }

    [FastHash]
    internal static partial class xsetid
    {
    }

    [FastHash]
    internal static partial class xtrim
    {
    }

    [FastHash]
    internal static partial class zadd
    {
    }

    [FastHash]
    internal static partial class zdiffstore
    {
    }

    [FastHash]
    internal static partial class zinterstore
    {
    }

    [FastHash]
    internal static partial class zunionstore
    {
    }

    [FastHash]
    internal static partial class zincr
    {
    }

    [FastHash]
    internal static partial class zrembyrank
    {
    }

    [FastHash]
    internal static partial class zrembyscore
    {
    }

    [FastHash]
    internal static partial class zrem
    {
    }

    [FastHash]
    internal static partial class expired
    {
    }

    [FastHash]
    internal static partial class evicted
    {
    }

    [FastHash("new")]
    internal static partial class _new // it isn't worth making the code-gen keyword aware
    {
    }

    [FastHash]
    internal static partial class overwritten
    {
    }

    [FastHash("type_changed")] // by default, the generator interprets underscore as hyphen
    internal static partial class type_changed
    {
    }

    // ReSharper restore InconsistentNaming
#pragma warning restore SA1300, CS8981
}
