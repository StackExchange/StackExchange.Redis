using System.Buffers;

namespace RESPite.Transports;

/// <summary>
/// Invoked for complete message payloads.
/// </summary>
public delegate void MessageCallback(in ReadOnlySequence<byte> payload);
