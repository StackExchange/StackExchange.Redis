namespace Resp;

/// <summary>
/// Additional information about a frame parsing operation.
/// </summary>
public struct FrameScanInfo
{
    /// <summary>
    /// Initialize an instance.
    /// </summary>
    public FrameScanInfo(bool isOutbound) => IsOutbound = isOutbound;

    /// <summary>
    /// Indicates whether the data operation is outbound.
    /// </summary>
    public bool IsOutbound { get; }

    /// <summary>
    /// The amount of data, in bytes, to read before attempting to read the next frame.
    /// </summary>
    public int ReadHint { get; set; }

    /// <summary>
    /// Gets the total number of bytes processed.
    /// </summary>
    public long BytesRead { get; set; }

    /// <summary>
    /// Indicates whether this is an out-of-band payload.
    /// </summary>
    public bool IsOutOfBand { get; set; }
}
