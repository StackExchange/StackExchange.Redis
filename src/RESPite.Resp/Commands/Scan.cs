using System;
using System.Text.RegularExpressions;
using RESPite.Buffers;
using RESPite.Messages;

namespace RESPite.Resp.Commands;

/// <summary>
/// Queries keys in a RESP database.
/// </summary>
public readonly record struct Scan(long Cursor = 0, ReadOnlyMemory<RespKey> Match = default, int Count = 10, string? Type = null)
    : IRespCommand<Scan, Scan.Response>
{
    IWriter<Scan> IRespCommand<Scan, Response>.Writer => ScanWriter.Instance;

    IReader<Empty, Response> IRespCommand<Scan, Response>.Reader => ScanReader.Instance;

    public readonly struct Response(long Cursor, RefCountedBuffer<RespKey> Keys)
    {
        public long Cursor { get; }
    }

    private sealed class ScanWriter : CommandWriter<Scan>
    {
        public static ScanWriter Instance = new();

        protected override void Write(in Scan request, ref RespWriter writer)
        {
            writer.WriteCommand(
                "SCAN"u8,
                1 + (request.Match.IsEmpty ? 0 : 2) + (request.Count > 0 ? 0 : 2) + (request.Type is null ? 0 : 2));
            writer.WriteBulkString(Cursor);

            if (request.Match.IsEmpty)
            {
                writer.WriteBulkString("MATCH"u8);
                writer.WriteBulkString(request.Match);
            }

            if (request.Count != 10)
            {
                writer.WriteBulkString("COUNT"u8);
                writer.WriteBulkString(request.Count);
            }

            if (request.Type is not null)
            {
                writer.WriteBulkString("TYPE"u8);
                writer.WriteBulkString(request.Type);
            }
        }
    }

    private sealed class ScanReader : CommandReader<Response>
    {
        public static ScanReader Instance = new();

        protected override Response Read(ref RespReader reader) => throw new System.NotImplementedException();
    }
}
