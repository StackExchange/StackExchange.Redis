using System;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;

namespace RESPite.Resp.Commands;

/// <summary>
/// Allows construction and configuration of <see cref="RespCommand{TRequest, TResponse}"/> types, by supplying readers and writers.
/// </summary>
public abstract partial class RespCommandFactory
{
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

    /// <summary>
    /// Reads values as an enum of type <typeparamref name="T"/>.
    /// </summary>
    public static IRespReader<Empty, T> EnumReader<T>() where T : struct, Enum
        => RespReaders.EnumReader<T>.Instance;
}

/// <summary>
/// When implemented by a <see cref="RespCommandFactory" />, supplies typed writers.
/// </summary>
public interface IRespWriterFactory<TRequest>
{
    /// <summary>
    /// Create a typed writer for the supplied command.
    /// </summary>
    IRespWriter<TRequest> CreateWriter(string command);
}

/// <summary>
/// When implemented by a <see cref="RespCommandFactory" />, supplies typed readers.
/// </summary>
public interface IRespReaderFactory<TRequest, TResponse>
{
    /// <summary>
    /// Create a typed reader.
    /// </summary>
    IRespReader<TRequest, TResponse> CreateReader();
}
