using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Messages;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;
using RESPite.Transports;

namespace RESPite.Resp.Commands;

/// <summary>
/// Represents a RESP command that sends message of type <typeparamref name="TRequest"/>, and
/// receives values of type <typeparamref name="TResponse"/>, using the request during the read.
/// </summary>
/// <param name="writer">The writer for this operation.</param>
/// <param name="reader">The reader for this operation.</param>
/// <typeparam name="TRequest">The type used to represent the parameters of this operation.</typeparam>
/// <typeparam name="TResponse">The type returned by this operation.</typeparam>
public readonly partial struct StatefulRespCommand<TRequest, TResponse>(IRespWriter<TRequest> writer, IRespReader<TRequest, TResponse> reader)
{
    /// <inheritdoc/>
    public override string? ToString() => writer.ToString();

    internal readonly IRespWriter<TRequest> writer = writer;
    internal readonly IRespReader<TRequest, TResponse> reader = reader;

    /// <inheritdoc cref="ISyncMessageTransport.Send{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{TRequest, TResponse})"/>
    public TResponse Send(ISyncMessageTransport transport, in TRequest request)
        => transport.Send<TRequest, TResponse>(in request, writer, reader);

    /// <inheritdoc cref="IAsyncMessageTransport.SendAsync{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{TRequest, TResponse}, CancellationToken)"/>
    public ValueTask<TResponse> SendAsync(IAsyncMessageTransport transport, in TRequest request, CancellationToken token = default)
        => transport.SendAsync<TRequest, TResponse>(in request, writer, reader, token);
}

/// <summary>
/// Represents a RESP command that sends message of type <typeparamref name="TRequest"/>, and
/// receives values of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TRequest">The type used to represent the parameters of this operation.</typeparam>
/// <typeparam name="TResponse">The type returned by this operation.</typeparam>
public readonly struct RespCommand<TRequest, TResponse>
{
    /// <inheritdoc/>
    public override string? ToString() => writer.ToString();
    internal readonly IRespWriter<TRequest> writer;
    internal readonly IRespReader<Empty, TResponse> reader;

    /// <summary>
    /// Create a new command instance.
    /// </summary>
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("This will result in an unconfigured command; instead, pass 'default' to the secondary constructor.", true)]
    public RespCommand() => throw new NotSupportedException();

    /// <summary>
    /// Create a new command instance.
    /// </summary>
    public RespCommand(
        RespCommandFactory? factory,
        [CallerMemberName] string command = "",
        IRespWriter<TRequest>? writer = null,
        IRespReader<Empty, TResponse>? reader = null)
    {
        factory ??= RespCommandFactory.Default;
        this.reader = reader ?? factory.CreateReader<Empty, TResponse>() ?? throw new ArgumentNullException(nameof(reader), $"No suitable reader available for '{typeof(TResponse).Name}'");
        if (string.IsNullOrWhiteSpace(command))
        {
            this.writer = CommandWriter.Disabled<TRequest>();
        }
        else if (writer is null)
        {
            writer = factory.CreateWriter<TRequest>(command);
        }
        else
        {
            writer = writer.WithAlias(command);
        }
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer), $"No suitable writer available for '{typeof(TRequest).Name}'");
    }

    /// <inheritdoc cref="ISyncMessageTransport.Send{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{Empty, TResponse})"/>
    public TResponse Send(ISyncMessageTransport transport, in TRequest request)
        => transport.Send<TRequest, TResponse>(in request, writer, reader);

    /// <inheritdoc cref="IAsyncMessageTransport.SendAsync{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{Empty, TResponse}, CancellationToken)"/>
    public ValueTask<TResponse> SendAsync(IAsyncMessageTransport transport, in TRequest request, CancellationToken token = default)
        => transport.SendAsync<TRequest, TResponse>(in request, writer, reader, token);
}

/// <summary>
/// Additional methods for <see cref="RespCommand{TRequest, TResponse}"/> values.
/// </summary>
public static class RespCommandExtensions
{
    /// <inheritdoc cref="ISyncMessageTransport.Send{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{Empty, TResponse})"/>
    public static TResponse Send<TResponse>(this in RespCommand<Empty, TResponse> command, ISyncMessageTransport transport)
        => transport.Send<Empty, TResponse>(in Empty.Value, command.writer, command.reader);

    /// <inheritdoc cref="IAsyncMessageTransport.SendAsync{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{Empty, TResponse}, CancellationToken)"/>
    public static ValueTask<TResponse> SendAsync<TResponse>(this in RespCommand<Empty, TResponse> command, IAsyncMessageTransport transport, CancellationToken token = default)
        => transport.SendAsync<Empty, TResponse>(in Empty.Value, command.writer, command.reader, token);
}
