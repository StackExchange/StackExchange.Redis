using System;
using System.Buffers;
using System.ComponentModel;
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
/// <typeparam name="TRequest">The type used to represent the parameters of this operation.</typeparam>
/// <typeparam name="TState">The state associated with this operation.</typeparam>
/// <typeparam name="TResponse">The type returned by this operation.</typeparam>
public readonly partial struct StatefulRespCommand<TRequest, TState, TResponse>
{
    private readonly IRespWriter<(TRequest Request, TState State)> writer;
    private readonly IRespReader<(TRequest Request, TState State), TResponse> reader;

    internal StatefulRespCommand(
        IRespWriter<TRequest> writer,
        IRespReader<TState, TResponse> reader)
    {
        this.writer = new WrappedWriter(writer);
        this.reader = new WrappedReader(reader);
    }

    private StatefulRespCommand(
        IRespWriter<(TRequest Request, TState State)> writer,
        IRespReader<(TRequest Request, TState State), TResponse> reader)
    {
        this.writer = writer;
        this.reader = reader;
    }

    /// <inheritdoc/>
    public override string? ToString() => writer.ToString();

    /// <inheritdoc cref="ISyncMessageTransport.Send{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{TRequest, TResponse})"/>
    public TResponse Send(ISyncMessageTransport transport, in TRequest request, in TState state)
        => transport.Send<(TRequest, TState), TResponse>((request, state), writer, reader);

    /// <inheritdoc cref="IAsyncMessageTransport.SendAsync{TRequest, TResponse}(in TRequest, IWriter{TRequest}, IReader{TRequest, TResponse}, CancellationToken)"/>
    public ValueTask<TResponse> SendAsync(IAsyncMessageTransport transport, in TRequest request, in TState state, CancellationToken token = default)
        => transport.SendAsync<(TRequest, TState), TResponse>((request, state), writer, reader, token);

    /// <summary>
    /// Create a new command instance.
    /// </summary>
    [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete($"This will result in an unconfigured command; instead, pass 'default' to the secondary constructor, or use the {nameof(RespCommand<int, int>)}.{nameof(RespCommand<int, int>.WithState)}() API.", true)]
    public StatefulRespCommand() => throw new NotSupportedException();

    /// <summary>
    /// Change the command associated with this operation.
    /// </summary>
    public StatefulRespCommand<TRequest, TState, TResponse> WithAlias(string command)
        => new(writer.WithAlias(command), reader);

    private sealed class WrappedWriter(IRespWriter<TRequest> tail) : IRespWriter<(TRequest Request, TState State)>
    {
        bool IRespWriter<(TRequest Request, TState State)>.IsDisabled => tail.IsDisabled;

        IRespWriter<(TRequest Request, TState State)> IRespWriter<(TRequest Request, TState State)>.WithAlias(string command)
        {
            var newTail = tail.WithAlias(command);
            return ReferenceEquals(tail, newTail) ? this : new WrappedWriter(newTail);
        }

        void IRespWriter<(TRequest Request, TState State)>.Write(in (TRequest Request, TState State) request, ref RespWriter writer)
            => tail.Write(in request.Request, ref writer);

        void IWriter<(TRequest Request, TState State)>.Write(in (TRequest Request, TState State) request, IBufferWriter<byte> target)
            => tail.Write(in request.Request, target);
    }

    private sealed class WrappedReader(IRespReader<TState, TResponse> tail) : IRespReader<(TRequest Request, TState State), TResponse>
    {
        public TResponse Read(in (TRequest Request, TState State) request, ref RespReader reader)
            => tail.Read(in request.State, ref reader);

        public TResponse Read(in (TRequest Request, TState State) request, in ReadOnlySequence<byte> content)
            => tail.Read(in request.State, in content);
    }
}
