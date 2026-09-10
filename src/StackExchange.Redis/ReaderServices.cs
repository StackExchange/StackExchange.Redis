using System.Buffers;
using RESPite.Buffers;

namespace StackExchange.Redis;

/// <summary>
/// The services offered to a <see cref="RESPite.Messages.RespReader"/> reading from a connection: the
/// buffers on that path are owned by the read loop rather than by the reply, so the only thing on offer
/// is where to rent from when a caller has to take a copy.
/// </summary>
/// <remarks>
/// One instance per multiplexer, held by each physical connection, and handed to every reader it creates.
/// Note that this deliberately reads through to the configuration rather than capturing the pool, so that
/// it stays a pure indirection - the pool is resolved at the point of use, exactly as it was when callers
/// passed it explicitly.
/// </remarks>
internal sealed class ReaderServices(ConfigurationOptions config) : IBufferPoolProvider
{
    public MemoryPool<byte>? BufferPool => config.ResponseBufferPool;
}

public partial class ConnectionMultiplexer
{
    private ReaderServices? _readerServices;

    /// <summary>
    /// Services offered to readers created against connections belonging to this multiplexer.
    /// </summary>
    internal ReaderServices ReaderServices => _readerServices ??= new ReaderServices(RawConfig);
}
