using System.Buffers;
using System.Threading.Tasks.Sources;

namespace RESPite.Internal;

internal interface IRespMessage : IValueTaskSource
{
    void Wait(short token, TimeSpan timeout);
    bool TryCancel(short token, CancellationToken cancellationToken = default);
    bool TrySetException(short token, Exception exception);
    bool TryReserveRequest(short token, out ReadOnlyMemory<byte> payload, bool recordSent = true);
    void ReleaseRequest(short token);
}
