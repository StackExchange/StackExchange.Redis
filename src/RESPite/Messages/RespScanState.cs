using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace RESPite.Messages;

/// <summary>
/// Holds state used for RESP frame parsing, i.e. detecting the RESP for an entire top-level message.
/// </summary>
[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public struct RespScanState
{
    /*
    The key point of ScanState is to skim over a RESP stream with minimal frame processing, to find the
    end of a single top-level RESP message. We start by expecting 1 message, and then just read, with the
    rules that the end of a message subtracts one, and aggregates add N. Streaming scalars apply zero offset
    until the scalar stream terminator. Attributes also apply zero offset.
    Note that streaming aggregates change the rules - when at least one streaming aggregate is in effect,
    no offsets are applied until we get back out of the outermost streaming aggregate - we achieve this
    by simply counting the streaming aggregate depth, which is usually zero.
    Note that in reality streaming (scalar and aggregates) and attributes are non-existent; in addition
    to being specific to RESP3, no known server currently implements these parts of the RESP3 specification,
    so everything here is theoretical, but: works according to the spec.
    */
    private int _delta; // when this becomes -1, we have fully read a top-level message;
    private ushort _streamingAggregateDepth;
    private RespPrefix _prefix;

    public RespPrefix Prefix => _prefix;

    private long _totalBytes;
#if DEBUG
    private int _elementCount;

    /// <inheritdoc/>
    public override string ToString() => $"{_prefix}, consumed: {_totalBytes} bytes, {_elementCount} nodes, complete: {IsComplete}";
#else
    /// <inheritdoc/>
    public override string ToString() => _prefix.ToString();
#endif

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int GetHashCode() => throw new NotSupportedException();

    /// <summary>
    /// Gets whether an entire top-level RESP message has been consumed.
    /// </summary>
    public bool IsComplete => _delta == -1;

    /// <summary>
    /// Gets the total length of the payload read (or read so far, if it is not yet complete); this combines payloads from multiple
    /// <c>TryRead</c> operations.
    /// </summary>
    public long TotalBytes => _totalBytes;

    // used when spotting common replies - we entirely bypass the usual reader/delta mechanism
    internal void SetComplete(int totalBytes, RespPrefix prefix)
    {
        _totalBytes = totalBytes;
        _delta = -1;
        _prefix = prefix;
#if DEBUG
        _elementCount = 1;
#endif
    }

    /// <summary>
    /// The amount of data, in bytes, to read before attempting to read the next frame.
    /// </summary>
    public const int MinBytes = 3; // minimum legal RESP frame is: _\r\n

    /// <summary>
    /// Create a new value that can parse the supplied node (and subtree).
    /// </summary>
    internal RespScanState(in RespReader reader)
    {
        Debug.Assert(reader.Prefix != RespPrefix.None, "missing RESP prefix");
        _totalBytes = 0;
        _delta = reader.GetInitialScanCount(out _streamingAggregateDepth);
    }

    /// <summary>
    /// Scan as far as possible, stopping when an entire top-level RESP message has been consumed or the data is exhausted.
    /// </summary>
    /// <returns>True if a top-level RESP message has been consumed.</returns>
    public bool TryRead(ref RespReader reader, out long bytesRead)
    {
        bytesRead = ReadCore(ref reader, reader.BytesConsumed);
        return IsComplete;
    }

    /// <summary>
    /// Scan as far as possible, stopping when an entire top-level RESP message has been consumed or the data is exhausted.
    /// </summary>
    /// <returns>True if a top-level RESP message has been consumed.</returns>
    public bool TryRead(ReadOnlySpan<byte> value, out int bytesRead)
    {
        var reader = new RespReader(value);
        bytesRead = (int)ReadCore(ref reader);
        return IsComplete;
    }

    /// <summary>
    /// Scan as far as possible, stopping when an entire top-level RESP message has been consumed or the data is exhausted.
    /// </summary>
    /// <returns>True if a top-level RESP message has been consumed.</returns>
    public bool TryRead(in ReadOnlySequence<byte> value, out long bytesRead)
    {
        var reader = new RespReader(in value);
        bytesRead = ReadCore(ref reader);
        return IsComplete;
    }

    /// <summary>
    /// Scan as far as possible, stopping when an entire top-level RESP message has been consumed or the data is exhausted.
    /// </summary>
    /// <returns>The number of bytes consumed in this operation.</returns>
    private long ReadCore(ref RespReader reader, long startOffset = 0)
    {
#pragma warning disable CS0618 // avoid TryReadNext unless you know what you're doing
        while (_delta >= 0 && reader.TryReadNext())
#pragma warning restore CS0618
        {
#if DEBUG
            _elementCount++;
#endif
            if (!reader.IsAttribute & _prefix == RespPrefix.None)
            {
                _prefix = reader.Prefix;
            }

            if (reader.IsAggregate) ApplyAggregateRules(ref reader);

            if (_streamingAggregateDepth == 0) _delta += reader.Delta();
        }

        var bytesRead = reader.BytesConsumed - startOffset;
        _totalBytes += bytesRead;
        return bytesRead;
    }

    private void ApplyAggregateRules(ref RespReader reader)
    {
        Debug.Assert(reader.IsAggregate, "RESP aggregate expected");
        if (reader.IsStreaming)
        {
            // entering an aggregate stream
            if (_streamingAggregateDepth == ushort.MaxValue) ThrowTooDeep();
            _streamingAggregateDepth++;
        }
        else if (reader.Prefix == RespPrefix.StreamTerminator)
        {
            // exiting an aggregate stream
            if (_streamingAggregateDepth == 0) ThrowUnexpectedTerminator();
            _streamingAggregateDepth--;
        }
        static void ThrowTooDeep() => throw new InvalidOperationException("Maximum streaming aggregate depth exceeded.");
        static void ThrowUnexpectedTerminator() => throw new InvalidOperationException("Unexpected streaming aggregate terminator.");
    }
}
