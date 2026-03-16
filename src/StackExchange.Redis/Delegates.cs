using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace StackExchange.Redis;

/// <summary>
/// Provides utility methods for working with delegates.
/// </summary>
internal static class Delegates
{
    /// <summary>
    /// Iterate over the individual elements of a multicast delegate (without allocation).
    /// </summary>
    /// <typeparam name="T">The type of delegate being enumerated.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DelegateEnumerator<T> GetEnumerator<T>(this T? handler) where T : MulticastDelegate
        => handler is null ? default : new(handler);

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
    #if NET8_0_OR_GREATER
        => s_getArr(handler) is null;
    #else
        => s_getArr is not null && s_getArr(handler) is null;
    #endif

    /// <summary>
    /// Indicates whether optimized usage is supported on this environment; without this, it may still
    /// work, but with additional overheads at runtime.
    /// </summary>
#if !NET8_0_OR_GREATER
    [MemberNotNullWhen(true, nameof(s_getArr))]
    [MemberNotNullWhen(true, nameof(s_getCount))]
#endif
    public static bool IsSupported => s_isAvailable;

#if NET8_0_OR_GREATER
#pragma warning disable SA1300
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_invocationList")]
    private static extern object? s_getArr(MulticastDelegate handler);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_invocationCount")]
    private static extern IntPtr s_getCount(MulticastDelegate handler);

    // ReSharper disable once InconsistentNaming
#pragma warning disable SA1303
    private const bool s_isAvailable = true;
#pragma warning restore SA1303

#pragma warning restore SA1300
#else
#pragma warning disable SA1300
    private static readonly Func<MulticastDelegate, object?>? s_getArr = GetGetter<object>("_invocationList");
    private static readonly Func<MulticastDelegate, IntPtr>? s_getCount = GetGetter<IntPtr>("_invocationCount");

    private static readonly bool s_isAvailable = s_getArr is not null & s_getCount is not null;

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
        => handler => (T)field.GetValue(handler);
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
        public DelegateEnumerator<T> GetEnumerator()
            => _handler is null ? default : new DelegateEnumerator<T>(_handler);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Allows allocation-free enumerator over the individual elements of a multicast delegate.
    /// </summary>
    /// <typeparam name="T">The type of delegate being enumerated.</typeparam>
    public struct DelegateEnumerator<T> : IEnumerator<T> where T : MulticastDelegate
    {
        private readonly T _handler;
        private readonly object[]? _arr;
        private readonly int _count;
        private int _index;
        private T? _current;
        internal DelegateEnumerator(T handler)
        {
            // Debug.Assert(handler is not null);
            _handler = handler;
            if (IsSupported)
            {
                _arr = (object[]?)s_getArr(handler);
                if (_arr is null)
                {
                    _count = 1;
                }
                else
                {
                    _count = (int)s_getCount(handler);
                }
            }
            else
            {
                _arr = handler.GetInvocationList();
                _count = _arr.Length;
            }
            _current = null;
            _index = -1;
        }

        /// <summary>
        /// Provides the current value of the sequence.
        /// </summary>
        public T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current!;
        }

        object? IEnumerator.Current => Current;

        void IDisposable.Dispose() { }

        /// <summary>
        /// Move to the next item in the sequence.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            var next = _index + 1;
            if (next >= _count)
            {
                _current = null;
                return false;
            }
            _current = _arr is null ? _handler : (T)_arr[next];
            _index = next;
            return true;
        }

        /// <summary>
        /// Reset the enumerator, allowing the sequence to be repeated.
        /// </summary>
        public void Reset()
        {
            _current = null;
            _index = -1;
        }
    }
}
