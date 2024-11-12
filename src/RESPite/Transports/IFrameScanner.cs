using System.Buffers;

namespace RESPite.Transports;

/// <summary>
/// When implemented by a <see cref="IFrameScanner{TState}"/>, verifies that a message is valid.
/// </summary>
/// <remarks>This is typically used for debugging or other scenarios where correctness is in question.</remarks>
public interface IFrameValidator
{
    /// <summary>
    /// Verify the provided payload.
    /// </summary>
    void Validate(in ReadOnlySequence<byte> message);
}

/// <summary>
/// Lightweight frame scanner; the role of the scanner is not to *process* data,
/// but simply to split data into frames; this could be by reading a frame
/// header and capturing the required body size, or it could be by looking
/// for a sentinel that indicates the end of a frame.
/// </summary>
/// <typeparam name="TState">Additional data required during scanning, for incremental deframing.</typeparam>
public interface IFrameScanner<TState>
{
    /// <summary>
    /// Prepare for a new frame, initializing any required parse state.
    /// </summary>
    void OnBeforeFrame(ref TState? state, ref FrameScanInfo info);

    /// <summary>
    /// Attempt to read a single frame. When completing a frame, the <see cref="FrameScanInfo.BytesRead"/> must
    /// be set. Incremental frame parsers can elect to increment <see cref="FrameScanInfo.BytesRead"/> multiple
    /// times; that portion of the frame is not passed to the frame parser again. The entire frame will then
    /// be passed to <see cref="Trim(ref TState?, ref ReadOnlySequence{byte}, ref FrameScanInfo)"/>, providing
    /// an opportunity to remove head/trail data from the final payload.
    /// </summary>
    OperationStatus TryRead(ref TState? state, in ReadOnlySequence<byte> data, ref FrameScanInfo info);

    /// <summary>
    /// Remove head/trail data from the final payload.
    /// </summary>
    void Trim(ref TState? state, ref ReadOnlySequence<byte> data, ref FrameScanInfo info);
}

/// <summary>
/// Additional state lifetime support.
/// </summary>
public interface IFrameScannerLifetime<TState> : IFrameScanner<TState>
{
    /// <summary>
    /// Initial creation.
    /// </summary>
    void OnInitialize(out TState? state);

    /// <summary>
    /// Cleanup any parse state.
    /// </summary>
    void OnComplete(ref TState? state);
}

/// <summary>
/// Additional information about a frame parsing operation.
/// </summary>
public struct FrameScanInfo
{
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
