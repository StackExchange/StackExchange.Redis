using System.Runtime.CompilerServices;
using RESPite.Messages;

namespace RESPite.Internal;

internal sealed class RespMultiMessage : RespMessageBase<int>
{
    private RespOperation[] _oversized;
    private int _count = 0;

    [ThreadStatic]
    // used for object recycling of the async machinery
    private static RespMultiMessage? _threadStaticSpare;

    internal static RespMultiMessage Get(RespOperation[] oversized, int count)
    {
        RespMultiMessage obj = _threadStaticSpare ?? new();
        _threadStaticSpare = null;
        obj._oversized = oversized;
        obj._count = count;
        obj.SetFlag(StateFlags.HasParser | StateFlags.MetadataParser);
        return obj;
    }

    protected override void Recycle() => _threadStaticSpare = this;

    private RespMultiMessage() => Unsafe.SkipInit(out _oversized);

    protected override int Parse(ref RespReader reader)
        => MultiMessageParser.Default.Parse(new ReadOnlySpan<RespOperation>(_oversized, 0, _count), ref reader);

    public override void Reset(bool recycle)
    {
        _oversized = [];
        _count = 0;
        base.Reset(recycle);
    }

    public override int MessageCount => _count;

    private sealed class MultiMessageParser
    {
        private MultiMessageParser() { }
        public static readonly MultiMessageParser Default = new();

        public int Parse(ReadOnlySpan<RespOperation> operations, ref RespReader reader)
        {
            int count = 0;
            foreach (var op in operations)
            {
                // we need to give each sub-operation an isolated reader - no bleeding
                // data between misbehaving readers (for example, that don't consume
                // all of their data)
                var clone = reader; // track the start position
                if (!reader.TryMoveNext(checkError: false)) ThrowEOF(); // we definitely expected enough for all

                reader.SkipChildren(); // track the end position (for scalar, this is "move past current")

                // now clamp this sub-reader, passing *that* to the operation
                clone.TrimToTotal(reader.BytesConsumed);
                if (op.Message.TrySetResult(op.Token, ref clone))
                {
                    // track how many we successfully processed, ignoring things
                    // that, for example, failed due to cancellation before we got here
                    count++;
                }
            }

            if (reader.TryMoveNext()) ThrowTrailing();
            return count;

            static void ThrowTrailing() => throw new FormatException("Unexpected trailing data");
            static void ThrowEOF() => throw new EndOfStreamException();
        }
    }
}
