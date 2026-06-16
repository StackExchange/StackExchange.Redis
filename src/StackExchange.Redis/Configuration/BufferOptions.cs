using System.Buffers;

namespace StackExchange.Redis.Configuration;

/// <summary>
/// CycleBuffer BufferOptions.
/// </summary>
public sealed class BufferOptions
{
    /// <summary>
    /// Memory Pool.
    /// </summary>
    public MemoryPool<byte>? MemoryPool { get; set; }

    /// <summary>
    /// Buffer Size.
    /// </summary>
    public int BufferSize { get; set; }

    /// <summary>
    /// Buffer Growth Factor.
    /// </summary>
    public float BufferGrowthFactor { get; set; }
}
