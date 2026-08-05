#if NET9_0_OR_GREATER
// .NET 9 added Delegate.EnumerateInvocationList, which is exactly the allocation-free
// enumerator we want, is runtime-agnostic, and works on NativeAOT; prefer it
#define BCL_INVOCATION_LIST
#elif NET8_0_OR_GREATER
#define UNSAFE_ACCESSOR // retain ability to disable easily
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

#if !UNSAFE_ACCESSOR && !BCL_INVOCATION_LIST
using System.Reflection;
using System.Reflection.Emit;
#endif

namespace StackExchange.Redis;

/// <summary>
/// Provides utility methods for working *efficiently* with multicast delegates.
/// </summary>
internal static class Delegates
{
    /// <summary>
    /// Iterate over the individual elements of a multicast delegate (without allocation).
    /// </summary>
    /// <typeparam name="T">The type of delegate being enumerated.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DelegateEnumerator<T> GetEnumerator<T>(this T? handler) where T : MulticastDelegate
        => new(handler);

    /// <summary>
    /// Iterate over the individual elements of a multicast delegate (without allocation).
    /// </summary>
    /// <typeparam name="T">The type of delegate being enumerated.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DelegateEnumerable<T> AsEnumerable<T>(this T? handler) where T : MulticastDelegate
        => new(handler);

    /// <summary>
    /// Indicates whether a particular delegate is known to be a single-target delegate.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSingle(this MulticastDelegate handler)
    {
#if BCL_INVOCATION_LIST
        var iterator = Delegate.EnumerateInvocationList(handler);
        return iterator.MoveNext() && !iterator.MoveNext();
#elif UNSAFE_ACCESSOR
        if (s_isAvailable)
        {
            return s_getArr(handler) is null;
        }
        return handler.GetInvocationList().Length == 1;
#else
        if (s_isAvailable)
        {
            if (s_getArr is not null)
            {
                return s_getArr(handler) is null;
            }

            return s_delegates!(handler) is null;
        }
        return handler.GetInvocationList().Length == 1;
#endif
    }

    /// <summary>
    /// Indicates whether optimized usage is supported on this environment; without this, it may still
    /// work, but with additional overheads at runtime.
    /// </summary>
    public static bool IsSupported => s_isAvailable;

#if BCL_INVOCATION_LIST
#pragma warning disable SA1303
    private const bool s_isAvailable = true;
#pragma warning restore SA1303
#elif UNSAFE_ACCESSOR
#pragma warning disable SA1300
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_invocationList")]
    private static extern ref readonly object? s_getArr(MulticastDelegate handler);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_invocationCount")]
    private static extern ref readonly nint s_getCount(MulticastDelegate handler);

    // ReSharper disable once InconsistentNaming
#pragma warning disable SA1303
    private static readonly bool s_isAvailable = IsAvailable();
#pragma warning restore SA1303

#pragma warning restore SA1300

    private static bool IsAvailable()
    {
        // these fields are an implementation detail of the CoreCLR/.NET Framework delegate layout;
        // other runtimes differ (NativeAOT keeps the invocation list on Delegate, in a different
        // shape), in which case the accessors above throw MissingFieldException on first use - so:
        // validate before we trust them, and fall back to GetInvocationList() when unsure (see #3157)
        try
        {
            Action probe = Probe;
            if (s_getArr(probe) is not null) return false; // expect: single-target => no list

            probe += Probe; // (combine does not de-duplicate, so this is genuinely two targets)
            return s_getArr(probe) is object[] arr && arr.Length >= 2 // (the array can have spare capacity)
                && (int)s_getCount(probe) == 2
                && arr[0] is Action && arr[1] is Action;
        }
        catch
        {
            return false;
        }

        static void Probe() { }
    }
#else
#pragma warning disable SA1300
    private static readonly Func<MulticastDelegate, object?>? s_getArr;
    private static readonly Func<MulticastDelegate, nint>? s_getCount;
    private static readonly Func<MulticastDelegate, Delegate[]>? s_delegates;

    private static readonly bool s_isAvailable = IsAvailable(out s_getArr, out s_getCount, out s_delegates);

    private static bool IsAvailable(
        out Func<MulticastDelegate, object?>? getArr,
        out Func<MulticastDelegate, nint>? getCount,
        out Func<MulticastDelegate, Delegate[]>? delegates)
    {
        // look for .NET's convention
        getArr = GetGetter<object>("_invocationList");
        getCount = GetGetter<IntPtr>("_invocationCount");
        if (getArr is not null & getCount is not null)
        {
            delegates = null;
            return true;
        }

        // try for Mono
        getArr = null;
        getCount = null;
        delegates = GetGetter<Delegate[]>("delegates");
        return delegates is not null;
    }

#pragma warning restore SA1300

    private static Func<MulticastDelegate, T>? GetGetter<T>(string fieldName)
    {
        try
        {
            var field = typeof(MulticastDelegate).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);

            if (field is null || field.FieldType != typeof(T)) return null;

#if !NETSTANDARD2_0
            try // we can try use ref-emit
            {
                var dm = new DynamicMethod(fieldName, typeof(T), new[] { typeof(MulticastDelegate) }, typeof(MulticastDelegate), true);
                var il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
                il.Emit(OpCodes.Ret);
                return (Func<MulticastDelegate, T>)dm.CreateDelegate(typeof(Func<MulticastDelegate, T>));
            }
            catch { }
#endif
            return GetViaReflection<T>(field);
        }
        catch
        {
            return null;
        }
    }
    private static Func<MulticastDelegate, T> GetViaReflection<T>(FieldInfo field)
        => handler => (T)field.GetValue(handler)!;
#endif

    /// <summary>
    /// Allows allocation-free enumerator over the individual elements of a multicast delegate.
    /// </summary>
    /// <typeparam name="T">The type of delegate being enumerated.</typeparam>
    public readonly struct DelegateEnumerable<T> : IEnumerable<T> where T : MulticastDelegate
    {
        private readonly T? _handler;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal DelegateEnumerable(T? handler) => _handler = handler;

        /// <summary>
        /// Iterate over the individual elements of a multicast delegate (without allocation).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DelegateEnumerator<T> GetEnumerator() => new(_handler);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Allows allocation-free enumerator over the individual elements of a multicast delegate.
    /// </summary>
    /// <typeparam name="T">The type of delegate being enumerated.</typeparam>
    public struct DelegateEnumerator<T> : IEnumerator<T> where T : MulticastDelegate
    {
        private readonly T? _handler;
#if BCL_INVOCATION_LIST
        private Delegate.InvocationListEnumerator<T> _iterator;
#else
        private readonly object[]? _arr;
        private readonly int _count;
        private int _index;
        private T? _current;
#endif

        internal DelegateEnumerator(T? handler)
        {
            _handler = handler;
#if BCL_INVOCATION_LIST
            _iterator = Delegate.EnumerateInvocationList(handler);
#else
            if (handler is null)
            {
                _arr = null;
                _count = 0;
            }
            else if (s_isAvailable)
            {
#if UNSAFE_ACCESSOR
                _arr = s_getArr(handler) as object[];
                _count = _arr is null ? 1 : (int)s_getCount(handler);
#else
                if (s_delegates is null)
                {
                    _arr = s_getArr!(handler) as object[];
                    _count = _arr is null ? 1 : (int)s_getCount!(handler);
                }
                else
                {
                    // ReSharper disable once CoVariantArrayConversion
                    _arr = s_delegates(handler);
                    _count = _arr?.Length ?? 1;
                }
#endif
            }
            else
            {
                // ReSharper disable once CoVariantArrayConversion
                _arr = handler.GetInvocationList();
                _count = _arr.Length;
            }

            _current = null;
            _index = -1;
#endif
        }

        /// <summary>
        /// Provides the current value of the sequence.
        /// </summary>
        public T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if BCL_INVOCATION_LIST
            get => _iterator.Current;
#else
            get => _current!;
#endif
        }

        object? IEnumerator.Current => Current;

        void IDisposable.Dispose() { }

        /// <summary>
        /// Move to the next item in the sequence.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
#if BCL_INVOCATION_LIST
            return _iterator.MoveNext();
#else
            var next = _index + 1;
            if (next >= _count)
            {
                _current = null;
                return false;
            }
            _current = _arr is null ? _handler! : (T)_arr[next];
            _index = next;
            return true;
#endif
        }

        /// <summary>
        /// Reset the enumerator, allowing the sequence to be repeated.
        /// </summary>
        public void Reset()
        {
#if BCL_INVOCATION_LIST
            _iterator = Delegate.EnumerateInvocationList(_handler);
#else
            _current = null;
            _index = -1;
#endif
        }
    }
}
