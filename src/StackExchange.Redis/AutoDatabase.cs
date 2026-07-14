using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace StackExchange.Redis;

[Conditional("DEBUG")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
internal sealed class AutoDatabaseAttribute : Attribute
{
}

internal interface IRedisArgs
{
    void Map(IRedisArgsMutator mutator);
    CommandFlags Flags { get; set; }
    object? UnMapper { get; }
}

internal interface IRedisArgsMutator
{
    RedisKey Map(RedisKey key);
    RedisChannel Map(RedisChannel channel);

    RedisKey UnMap(RedisKey key);
    RedisChannel UnMap(RedisChannel channel);
}

internal interface IRedisArgsResult<T>
{
    T UnMap(IRedisArgsMutator mutator, T value);
}

internal static class RedisArgsMutatorExtensions
{
    // these are used by the generated tuple-types via auto-database: each maps the key-bearing parts
    // of an argument through the supplied mutator. They hang off IRedisArgsMutator (rather than the
    // argument) so a call site reads consistently with the interface's own MapKey/MapChannel.
    public static KeyValuePair<RedisKey, RedisValue> Map(
        this IRedisArgsMutator mutator,
        KeyValuePair<RedisKey, RedisValue> value) =>
        new(mutator.Map(value.Key), value.Value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TResult UnMap<TState, TResult>(this IRedisArgsMutator mutator, in TState state, TResult result)
        where TState : struct, IRedisArgs
    {
        if (typeof(TResult) == typeof(RedisKey))
        {
            var tmp = mutator.UnMap(Unsafe.As<TResult, RedisKey>(ref result));
            return Unsafe.As<RedisKey, TResult>(ref tmp);
        }
        else if (typeof(TResult) == typeof(RedisChannel))
        {
            var tmp = mutator.UnMap(Unsafe.As<TResult, RedisChannel>(ref result));
            return Unsafe.As<RedisChannel, TResult>(ref tmp);
        }
        else if (typeof(TResult) == typeof(RedisValue))
        {
            return result; // never mapped
        }
        else
        {
            return state.UnMapper is IRedisArgsResult<TResult> unmap ? unmap.UnMap(mutator, result) : result;
        }
    }

    [return: NotNullIfNotNull("keys")]
    public static RedisKey[]? Map(this IRedisArgsMutator mutator, RedisKey[]? keys)
    {
        if (keys is null || keys.Length is 0) return keys;
        var arr = new RedisKey[keys.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            arr[i] = mutator.Map(keys[i]);
        }
        return arr;
    }

    [return: NotNullIfNotNull("pairs")]
    public static KeyValuePair<RedisKey, RedisValue>[]? Map(
        this IRedisArgsMutator mutator,
        KeyValuePair<RedisKey, RedisValue>[]? pairs)
    {
        if (pairs is null || pairs.Length is 0) return pairs;
        var arr = new KeyValuePair<RedisKey, RedisValue>[pairs.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            ref readonly KeyValuePair<RedisKey, RedisValue> pair = ref pairs[i];
            arr[i] = new(mutator.Map(pair.Key), pair.Value);
        }
        return arr;
    }

    public static StreamPosition Map(this IRedisArgsMutator mutator, StreamPosition value) =>
        new(mutator.Map(value.Key), value.Position);

    [return: NotNullIfNotNull("positions")]
    public static StreamPosition[]? Map(this IRedisArgsMutator mutator, StreamPosition[]? positions)
    {
        if (positions is null || positions.Length is 0) return positions;
        var arr = new StreamPosition[positions.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            ref readonly StreamPosition position = ref positions[i];
            arr[i] = new(mutator.Map(position.Key), position.Position);
        }
        return arr;
    }

    // the Execute/ExecuteAsync escape hatch takes a loosely-typed arg list in which any element may
    // be a boxed RedisKey or RedisChannel; these rewrite just those entries (mirroring
    // KeyPrefixed.ToInner), copying only when there is something to rewrite so the common call allocates nothing.
    [return: NotNullIfNotNull("args")]
    public static object[]? Map(this IRedisArgsMutator mutator, object[]? args)
    {
        if (args is null || args.Length is 0) return args;
        object[]? copy = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is RedisKey key)
            {
                (copy ??= (object[])args.Clone())[i] = mutator.Map(key);
            }
            else if (args[i] is RedisChannel channel)
            {
                (copy ??= (object[])args.Clone())[i] = mutator.Map(channel);
            }
        }
        return copy ?? args;
    }

    [return: NotNullIfNotNull("args")]
    public static ICollection<object>? Map(this IRedisArgsMutator mutator, ICollection<object>? args)
    {
        if (args is null || args.Count is 0) return args;
        bool any = false;
        foreach (var arg in args)
        {
            if (arg is RedisKey or RedisChannel)
            {
                any = true;
                break;
            }
        }
        if (!any) return args;

        var copy = new object[args.Count];
        int i = 0;
        foreach (var arg in args)
        {
            copy[i++] = arg switch
            {
                RedisKey key => mutator.Map(key),
                RedisChannel channel => mutator.Map(channel),
                _ => arg,
            };
        }
        return copy;
    }

    public static SortedSetPopResult UnMap(this IRedisArgsMutator mutator, SortedSetPopResult value) =>
        value.IsNull ? SortedSetPopResult.Null : new(mutator.Map(value.Key), value.Entries);

    public static ListPopResult UnMap(this IRedisArgsMutator mutator, ListPopResult value) =>
        value.IsNull ? ListPopResult.Null : new(mutator.Map(value.Key), value.Values);
}
