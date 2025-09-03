using System.Diagnostics;
using System.Runtime.CompilerServices;
using RESPite.Connections.Internal;
using RESPite.Messages;

namespace RESPite.Internal;

internal sealed class RespMultiMessage : RespMessageBase<int>
{
    private RespOperation[] _oversized;
    private int _count;

    [ThreadStatic]
    // used for object recycling of the async machinery
    private static RespMultiMessage? _threadStaticSpare;

    private ReadOnlySpan<RespOperation> Operations => new(_oversized, 0, _count);

    internal static RespMultiMessage Get(RespOperation[] oversized, int count)
    {
        RespMultiMessage obj = _threadStaticSpare ?? new();
        _threadStaticSpare = null;
        obj._oversized = oversized;
        obj._count = count;
        return obj;
    }

    public override bool TryGetSubMessages(short token, out ReadOnlySpan<RespOperation> operations)
    {
        operations = token == Token ? Operations : default;
        return true; // always return true; this means that flush gets called
    }

    public override bool TrySetResultAfterUnloadingSubMessages(short token)
    {
        if (token == Token && TrySetResultPrecheckedToken(_count))
        {
            // release the buffer immediately - it isn't needed any more
            _count = 0;
            BufferingBatchConnection.Return(ref _oversized);
            return true;
        }

        return false;
    }

    protected override void Recycle() => _threadStaticSpare = this;

    private RespMultiMessage() => Unsafe.SkipInit(out _oversized);

    protected override int Parse(ref RespReader reader)
    {
        Debug.Fail("Not expecting to see results, since unrolled during write");
        return _count;
    }

    protected override void OnSent()
    {
        base.OnSent();
        foreach (var op in Operations)
        {
            op.OnSent();
        }
    }

    protected override void Reset(bool recycle)
    {
        _count = 0;
        BufferingBatchConnection.Return(ref _oversized);
        base.Reset(recycle);
    }

    public override int MessageCount => _count;
}
