using System.Buffers;

namespace RESPite.Streams;

public abstract partial class RespStream(Stream tail)
{
    public Stream Tail => tail;
}
