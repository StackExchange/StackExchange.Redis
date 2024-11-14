using System;
using RESPite.Resp.Commands;
using RESPite.Resp.KeyValueStore;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;

namespace RESPite.Resp.Client;

/// <summary>
/// Provides a factory with support for all required types.
/// </summary>
public sealed partial class CommandFactory : RespCommandFactory
{
    /// <summary>
    /// Provides a factory with support for all required types.
    /// </summary>
    public static CommandFactory Default = new();
    private CommandFactory() { }
}

public partial class CommandFactory : IRespWriterFactory<Scan.Request>, IRespReaderFactory<Empty, Scan.Response>, IRespReaderFactory<Empty, Keys.KnownType>
{
    IRespReader<Empty, Scan.Response> IRespReaderFactory<Empty, Scan.Response>.CreateReader() => ScanReader.Instance;
    IRespReader<Empty, Keys.KnownType> IRespReaderFactory<Empty, Keys.KnownType>.CreateReader() => EnumReader<Keys.KnownType>();
    IRespWriter<Scan.Request> IRespWriterFactory<Scan.Request>.CreateWriter(string command) => ScanWriter.Factory.Create(command);

    internal sealed class ScanWriter(string command) : CommandWriter<Scan.Request>(command, -1)
    {
        public static class Factory
        {
            private static ScanWriter? __SCAN;

            public static ScanWriter Create(string command) => command switch
            {
                "SCAN" => __SCAN ??= new("SCAN"),
                _ => new(command),
            };
        }

        protected override IRespWriter<Scan.Request> Create(string command) => Factory.Create(command);

        public override void Write(in Scan.Request request, ref RespWriter writer)
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

    internal sealed class ScanReader : ResponseReader<Scan.Response>
    {
        public static ScanReader Instance = new();

        public override Scan.Response Read(ref RespReader reader)
        {
            reader.Demand(RespPrefix.Array);
            if (reader.ChildCount < 2 || !reader.TryReadNext()) Throw();

            var cursor = reader.ReadInt64();
            if (!reader.TryReadNext(RespPrefix.Array)) Throw();

            var keys = reader.ReadLeasedStrings();
            return new(cursor, keys);

            static void Throw() => throw new InvalidOperationException("Unable to parse SCAN result");
        }
    }
}
