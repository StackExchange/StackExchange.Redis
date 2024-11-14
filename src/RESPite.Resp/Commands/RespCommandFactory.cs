using System;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;

using static RESPite.Resp.Commands.Keys;
using static RESPite.Resp.Commands.RespCommandFactory;

namespace RESPite.Resp.Commands;

/// <summary>
/// Allows construction and configuration of <see cref="RespCommand{TRequest, TResponse}"/> types, by supplying readers and writers.
/// </summary>
public partial class RespCommandFactory
{
    /// <summary>
    /// Create a new <see cref="RespCommandFactory" /> instance.
    /// </summary>
    protected RespCommandFactory() { }

    /// <summary>
    /// The common shared <see cref="RespCommandFactory" /> instance.
    /// </summary>
    public static RespCommandFactory Default { get; } = new();

    /// <summary>
    /// When implemented by a <see cref="RespCommandFactory" />, supplies typed writers.
    /// </summary>
    protected interface IRespWriterFactory<TRequest>
    {
        /// <summary>
        /// Create a typed writer for the supplied command.
        /// </summary>
        IRespWriter<TRequest> CreateWriter(string command);
    }

    /// <summary>
    /// When implemented by a <see cref="RespCommandFactory" />, supplies typed readers.
    /// </summary>
    protected interface IRespReaderFactory<TRequest, TResponse>
    {
        /// <summary>
        /// Create a typed reader.
        /// </summary>
        IRespReader<TRequest, TResponse> CreateReader();
    }

    /// <summary>
    /// Create a typed writer for the supplied command.
    /// </summary>
    public virtual IRespWriter<TRequest>? CreateWriter<TRequest>(string command)
        => (this as IRespWriterFactory<TRequest>)?.CreateWriter(command);

    /// <summary>
    /// Create a typed reader.
    /// </summary>
    public IRespReader<TRequest, TResponse>? CreateReader<TRequest, TResponse>()
        => (this as IRespReaderFactory<TRequest, TResponse>)?.CreateReader()
        ?? (this as IRespReader<TRequest, TResponse>)
        ?? RespReaders.Common as IRespReader<TRequest, TResponse>;
}

public partial class RespCommandFactory : IRespWriterFactory<Scan>, IRespReaderFactory<Empty, Scan.Response>, IRespReaderFactory<Empty, KnownType>
{
    IRespReader<Empty, Scan.Response> IRespReaderFactory<Empty, Scan.Response>.CreateReader() => ScanReader.Instance;
    IRespReader<Empty, KnownType> IRespReaderFactory<Empty, KnownType>.CreateReader() => RespReaders.EnumReader<KnownType>.Instance;
    IRespWriter<Scan> IRespWriterFactory<Scan>.CreateWriter(string command) => ScanWriter.Factory.Create(command);

    internal sealed class ScanWriter(string command) : CommandWriter<Scan>(command, -1)
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

        protected override IRespWriter<Scan> Create(string command) => Factory.Create(command);

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

    internal sealed class ScanReader : ResponseReader<Scan.Response>
    {
        public static ScanReader Instance = new();

        public override Scan.Response Read(ref RespReader reader)
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
