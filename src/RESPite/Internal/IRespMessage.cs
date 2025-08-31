using System.Buffers;
using System.Threading.Tasks.Sources;

namespace RESPite.Internal;

internal interface IRespMessage : IValueTaskSource
{
    void Wait(short token, TimeSpan timeout);
    void TrySetCanceled(); // only intended for use from cancellation callbacks
    bool TrySetCanceled(short token, CancellationToken cancellationToken = default);
    bool TrySetException(short token, Exception exception);
    bool TrySetResult(short token, scoped ReadOnlySpan<byte> response);
    bool TrySetResult(short token, in ReadOnlySequence<byte> response);
    bool TryReserveRequest(short token, out ReadOnlyMemory<byte> payload, bool recordSent = true);
    void ReleaseRequest();
    bool AllowInlineParsing { get; }
    short Token { get; }
    ref readonly CancellationToken CancellationToken { get; }
    bool IsSent(short token);

    void OnCompletedWithNotSentDetection(
        Action<object?> continuation,
        object? state,
        short token,
        ValueTaskSourceOnCompletedFlags flags);
}
