using System;
using System.Runtime.CompilerServices;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;

namespace RESPite.Resp.Commands;

/// <summary>
/// Queries keys in a RESP database.
/// </summary>
public readonly record struct Scan(long Cursor = 0, SimpleString Match = default, int Count = 10, string? Type = null)
{
    /// <summary>
    /// Process the result of a scan operation to update the <see cref="Cursor"/>.
    /// </summary>
    public Scan Next(in Response reply) => this with { Cursor = reply.Cursor };

    /// <summary>
    /// Provides the keys associated with a single iteration of a scan operation,
    /// and the cursor to continue the scan operation.
    /// </summary>
    /// <remarks>The keys can be any number, including zero and more than was requested in the request.</remarks>
    public readonly struct Response(long cursor, LeasedStrings keys) : IDisposable
    {
        private readonly LeasedStrings _keys = keys;

        /// <summary>
        /// Gets the cursor to use to continue this scan operation.
        /// </summary>
        public long Cursor { get; } = cursor;

        /// <summary>
        /// Gets the keys returned from this iteration of the scan operation.
        /// </summary>
        public LeasedStrings Keys => _keys;

        /// <inheritdoc/>
        public void Dispose()
        {
            var keys = _keys;
            Unsafe.AsRef(in _keys) = default;
            keys.Dispose();
        }
    }

    internal sealed class ScanWriter : RespWriterBase<Scan>
    {
        public static ScanWriter Instance = new();

        public override void Write(in Scan request, ref RespWriter writer)
        {
            const int DEFAULT_SERVER_COUNT = 10;
            int args = 1 + (request.Match.IsEmpty ? 0 : 2) + (request.Count == DEFAULT_SERVER_COUNT ? 0 : 2) + (string.IsNullOrEmpty(request.Type) ? 0 : 2);
            writer.WriteCommand("SCAN"u8, args);
            writer.WriteBulkString(request.Cursor);

            if (!request.Match.IsEmpty)
            {
                writer.WriteBulkString("MATCH"u8);
                writer.WriteBulkString(request.Match);
            }

            if (request.Count != DEFAULT_SERVER_COUNT)
            {
                writer.WriteBulkString("COUNT"u8);
                writer.WriteBulkString(request.Count);
            }

            if (!string.IsNullOrEmpty(request.Type))
            {
                writer.WriteBulkString("TYPE"u8);
                writer.WriteBulkString(request.Type!);
            }
        }
    }

    internal sealed class ScanReader : RespReaderBase<Response>
    {
        public static ScanReader Instance = new();

        public override Response Read(ref RespReader reader)
        {
            reader.Demand(RespPrefix.Array);
            if (reader.ChildCount < 2 || !reader.TryReadNext()) Throw();

            var cursor = reader.ReadInt64();
            if (!reader.TryReadNext(RespPrefix.Array)) Throw();
            var keys = RespReaders.ReadLeasedStrings(ref reader);
            return new(cursor, keys);

            static void Throw() => throw new InvalidOperationException("Unable to parse SCAN result");
        }
    }
}
