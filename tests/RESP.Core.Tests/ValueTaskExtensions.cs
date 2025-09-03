using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using RESPite;
using RESPite.Internal;

namespace RESP.Core.Tests;

public static class ValueTaskExtensions
{
    private static class FieldCache<T>
    {
#if NET8_0_OR_GREATER
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_obj")]
        public static extern ref readonly object? Object(in ValueTask<T> task);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_token")]
        public static extern ref readonly short Token(in ValueTask<T> task);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_continueOnCapturedContext")]
        public static extern ref readonly bool ContinueOnCapturedContext(in ValueTask<T> task);
#else
        private static readonly FieldInfo _obj =
            typeof(ValueTask<T>).GetField(nameof(_obj), BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly FieldInfo _token =
            typeof(ValueTask<T>).GetField(nameof(_token), BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly FieldInfo? _continueOnCapturedContext = typeof(ValueTask<T>).GetField(
            nameof(_continueOnCapturedContext),
            BindingFlags.NonPublic | BindingFlags.Instance);

        public static object? Object(ValueTask<T> task) => _obj.GetValue(task);

        public static short Token(ValueTask<T> task) => (short)_token.GetValue(task)!;

        public static bool ContinueOnCapturedContext(ValueTask<T> task)
            => _continueOnCapturedContext is not null
               && (bool)_continueOnCapturedContext.GetValue(task)!;
#endif
    }

    private static class FieldCache
    {
#if NET8_0_OR_GREATER
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_obj")]
        public static extern ref readonly object? Object(in ValueTask task);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_token")]
        public static extern ref readonly short Token(in ValueTask task);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_continueOnCapturedContext")]
        public static extern ref readonly bool ContinueOnCapturedContext(in ValueTask task);
#else
        private static readonly FieldInfo _obj =
            typeof(ValueTask).GetField(nameof(_obj), BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly FieldInfo _token =
            typeof(ValueTask).GetField(nameof(_token), BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static readonly FieldInfo? _continueOnCapturedContext = typeof(ValueTask).GetField(
            nameof(_continueOnCapturedContext),
            BindingFlags.NonPublic | BindingFlags.Instance);

        public static object? Object(ValueTask task) => _obj.GetValue(task);

        public static short Token(ValueTask task) => (short)_token.GetValue(task)!;

        public static bool ContinueOnCapturedContext(ValueTask task)
            => _continueOnCapturedContext is not null
               && (bool)_continueOnCapturedContext.GetValue(task)!;
#endif
    }

    public static RespOperation<T> Unwrap<T>(this ValueTask<T> value)
    {
        if (FieldCache<T>.Object(value) is not RespMessageBase<T> msg)
            throw new ArgumentException($"ValueTask does not wrap a {nameof(RespMessageBase)}<{typeof(T).Name}>");
        short token = FieldCache<T>.Token(value);
        bool continueOnCapturedContext = FieldCache<T>.ContinueOnCapturedContext(value);
        return new RespOperation<T>(msg, token, continueOnCapturedContext);
    }

    public static RespOperation Unwrap(this ValueTask value)
    {
        if (FieldCache.Object(value) is not RespMessageBase msg)
            throw new ArgumentException($"ValueTask does not wrap a {nameof(RespMessageBase)}");
        short token = FieldCache.Token(value);
        bool continueOnCapturedContext = FieldCache.ContinueOnCapturedContext(value);
        return new RespOperation(msg, token, continueOnCapturedContext);
    }
}
