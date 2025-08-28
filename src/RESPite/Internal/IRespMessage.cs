using System.Buffers;
using System.Threading.Tasks.Sources;

namespace RESPite.Internal;

internal interface IRespMessage : IValueTaskSource
{
    // We only use token for the public-exposed (indirectly) APIs, i.e. "get the result"; for
    // the "set the result" side, trust that we're not idiots.
    void Wait(short token, TimeSpan timeout);
    bool TrySetCanceled(CancellationToken cancellationToken = default);
    bool TrySetException(Exception exception);
    bool TryReserveRequest(out ReadOnlyMemory<byte> payload, bool recordSent = true);
    void ReleaseRequest();
    bool TrySetResult(scoped ReadOnlySpan<byte> payload);
    bool TrySetResult(ReadOnlySequence<byte> payload);
}
