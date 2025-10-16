#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
#else
using System.Reflection;
#endif
using RESPite.Internal;

namespace RESPite;

/// <summary>
/// The results of asynchronous RESPite operations can be treated interchangeably as either <see Type="ValueTask"/> or
/// <see Type="RespOperation"/> (or their generic twins: <see Type="ValueTask{T}"/> and <see Type="RespOperation{T}"/>).
/// <see Type="ValueTask"/> is a more familiar, and is convenient in pre-existing code; <see Type="RespOperation"/>
/// is more context-aware, and adds a few additional capabilities, such as:
/// - most notably: automatic detection if attempting to wait/await before a message has been sent.
/// - <see cref="RespOperation.IsSent"/> to check whether the message has been sent to a server.
/// - <see cref="RespOperation.CancellationToken"/> to access cancellation information about this message.
/// - <see cref="RespOperation.Wait(TimeSpan)"/> to wait synchronously for the operation to complete.
/// - a <see Type="RespOperation{T}"/> can be implicitly converted to a <see Type="RespOperation"/> (unlike <see Type="ValueTask{T}"/> to <see Type="ValueTask"/>).
///
/// Neither representation is more efficient, and the semantics are identical - the result can only be waited/awaited once
/// (unless hoisted into a <see Type="System.Threading.Tasks.Task"/>).
/// </summary>
public static class ValueTaskExtensions
{
    public static bool TryGetRespOperation<T>(this ValueTask<T> value, out RespOperation<T> operation)
    {
        if (FieldAccessor<T>.Object(value) is not RespMessageBase<T> msg)
        {
            operation = default;
            return false;
        }

        short token = FieldAccessor<T>.Token(value);
        bool continueOnCapturedContext = FieldAccessor<T>.ContinueOnCapturedContext(value);
        operation = new RespOperation<T>(msg, token, continueOnCapturedContext);
        return true;
    }

    public static RespOperation<T> AsRespOperation<T>(this ValueTask<T> value)
    {
        if (!TryGetRespOperation(value, out var operation)) Throw(typeof(T));
        return operation;
    }

    public static bool TryGetRespOperation(this ValueTask value, out RespOperation operation)
    {
        if (FieldAccessor.Object(value) is not RespMessageBase msg)
        {
            operation = default;
            return false;
        }

        short token = FieldAccessor.Token(value);
        bool continueOnCapturedContext = FieldAccessor.ContinueOnCapturedContext(value);
        operation = new RespOperation(msg, token, continueOnCapturedContext);
        return true;
    }

    public static RespOperation AsRespOperation(this ValueTask value)
    {
        if (!TryGetRespOperation(value, out var operation)) Throw();
        return operation;
    }

    private static void Throw(Type type)
        => throw new ArgumentException(
            $"The {nameof(ValueTask)}<{type.Name}> does not wrap does not wrap a {nameof(RespMessageBase)}<{type.Name}>");

    private static void Throw() =>
        throw new ArgumentException($"The {nameof(ValueTask)} does not wrap a {nameof(RespMessageBase)}");

    // from here on: evil reflection to peek inside ValueTask[<T>] and extract the fields we need
    private static class FieldAccessor<T>
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

    private static class FieldAccessor
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
}
