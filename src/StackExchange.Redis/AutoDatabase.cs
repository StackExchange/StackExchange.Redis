using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace StackExchange.Redis;

[Conditional("DEBUG")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
internal sealed class AutoDatabaseAttribute : Attribute
{
}

// Implemented by a generated captured-arguments struct only when the owning auto-database implements
// IRedisArgsMutator - i.e. only when something actually rewrites keys/channels. Everything else captures its
// arguments into a plain readonly struct with no interface at all; a funnel that wants to map opts in by
// constraining its TState to this (which is how KeyPrefixedDatabase will work once it moves onto
// [AutoDatabase]).
//
// Map mutates the struct in place, so a funnel that maps MUST take its state BY VALUE - never by `in`, because
// calling a non-readonly member through an `in` reference silently takes a defensive copy, mutates that, and
// throws it away. Invoke it via RedisArgsMutatorExtensions.MapInPlace, which makes that mistake a compile
// error (CS8329) instead of a silent loss of prefixing. (`ref` on the funnel is not an alternative: the
// generated call site passes a constructed temporary, which `ref` rejects with CS1510, and async funnels
// cannot have by-ref parameters at all.) The by-value copy is not waste here - a mapping funnel needs its own
// mutable copy by definition. Only non-mapping funnels take `in`, and their structs are readonly, so for them
// no defensive copy is even possible.
internal interface IMappableRedisArgs
{
    [Obsolete("Use MapInPlace instead; calling Map directly does nothing when the state is held by `in`.")]
    void Map(IRedisArgsMutator mutator);

    object? UnMapper { get; }
}

// Implemented by a generated captured-arguments struct whenever the captured call has a CommandFlags
// parameter - which is very nearly all of them - so that a funnel holding an opaque TState can still ask the
// one question that changes what it must hand back: was this fire-and-forget?
//
// It exists for RetryTransaction. That funnel records each call and returns a durable proxy task completed
// later from the attempt, which is wrong for fire-and-forget: a plain RedisTransaction hands back an
// *already-completed* task there (the caller has explicitly declined the reply), so without this the same
// source awaiting the same result returns instantly on one and hangs on the other. The flags are captured
// inside TState and are otherwise invisible to a funnel, which is the whole reason this interface is needed
// rather than the funnel simply reading a parameter.
internal interface IFlaggedRedisArgs
{
    CommandFlags Flags { get; }
}

// The projections used by the auto-database funnels. There are exactly four real shapes, so each is named
// rather than being expressed generically over the target/return type: that lets the type system encode the
// invariants (sync goes to IDatabase, async goes to IDatabaseAsync and returns a Task) instead of leaving
// nonsense combinations like "sync database, Task result" expressible.
//
// They exist at all (rather than Func<,,>/Action<,>) purely to pass the captured state by readonly-reference:
// some of the generated state structs are chunky (a dozen-plus arguments), and by-value would copy the whole
// thing on every call. Note that a lambda only binds to an `in` parameter if it says so - the generator emits
// `static (in state, inner) => ...` accordingly.
//
// TState is necessarily invariant: it is passed by readonly-ref, and variant type parameters cannot carry
// the struct constraint. TResult is covariant on the sync projection (it is the direct return); it cannot be
// on the async one, because there it appears inside the invariant Task<TResult>.
internal delegate void AutoDatabaseSyncOperation<TState>(in TState state, IDatabase database)
    where TState : struct;

internal delegate TResult AutoDatabaseSyncOperation<TState, out TResult>(in TState state, IDatabase database)
    where TState : struct;

internal delegate Task AutoDatabaseAsyncOperation<TState>(in TState state, IDatabaseAsync database)
    where TState : struct;

internal delegate Task<TResult> AutoDatabaseAsyncOperation<TState, TResult>(in TState state, IDatabaseAsync database)
    where TState : struct;

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

    // Funnels should map via this rather than calling state.Map(mutator) directly. Mapping mutates the struct
    // in place, so it is only correct on a writable variable; the `ref this` here means that applying it to an
    // `in` parameter fails to compile (CS8329) instead of silently taking a defensive copy, mutating that, and
    // discarding it - which would leave keys unprefixed, i.e. a wrong-keyspace bug rather than a perf nit.
    // (Deliberately not named Map: an extension cannot shadow the instance member, so the guard would be lost.)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MapInPlace<TState>(this ref TState state, IRedisArgsMutator mutator)
        where TState : struct, IMappableRedisArgs
#pragma warning disable CS0618 // the one sanctioned call site: `state` is a genuine `ref`, so this mutates in place
        => state.Map(mutator);
#pragma warning restore CS0618

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TResult UnMap<TState, TResult>(this IRedisArgsMutator mutator, in TState state, TResult result)
        where TState : struct, IMappableRedisArgs
    {
        // note JIT will elide these tests using per-TResult value-type rules
        if (typeof(TResult) == typeof(RedisKey))
        {
            var tmp = mutator.UnMap(Unsafe.As<TResult, RedisKey>(ref result));
            return Unsafe.As<RedisKey, TResult>(ref tmp);
        }

        if (typeof(TResult) == typeof(RedisChannel))
        {
            var tmp = mutator.UnMap(Unsafe.As<TResult, RedisChannel>(ref result));
            return Unsafe.As<RedisChannel, TResult>(ref tmp);
        }

        if (typeof(TResult) == typeof(RedisValue))
        {
            return result; // never mapped
        }

        return state.UnMapper is IRedisArgsResult<TResult> unmap ? unmap.UnMap(mutator, result) : result;
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
