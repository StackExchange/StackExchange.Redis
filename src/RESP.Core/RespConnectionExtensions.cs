using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Resp;

public interface IRespFormatter<TRequest>
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
{
    void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in TRequest request);
}
public interface IRespSizeEstimator<TRequest> : IRespFormatter<TRequest>
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
{
    int EstimateSize(scoped ReadOnlySpan<byte> command, in TRequest request);
}

public interface IRespParser<out TResponse>
{
    TResponse Parse(ref RespReader reader);
}

internal interface IRespInternalMessage : IRespMessage
{
    bool AllowInlineParsing { get; }
}

internal interface IRespInlineParser // if implemented, parsing is permitted on the IO thread
{
}
public interface IRespMetadataParser // if implemented, the consumer must manually advance to the content
{
}

public abstract class RespCommandMap
{
    /// <summary>
    /// Apply any remapping to the command.
    /// </summary>
    /// <param name="command">The command requested.</param>
    /// <returns>The remapped command; this can be the original command, a remapped command, or an empty instance if the command is not available.</returns>
    public abstract ReadOnlySpan<byte> Map(ReadOnlySpan<byte> command);

    /// <summary>
    /// Indicates whether the specified command is available.
    /// </summary>
    public virtual bool IsAvailable(ReadOnlySpan<byte> command)
        => Map(command).Length != 0;

    public static RespCommandMap Default { get; } = new DefaultRespCommandMap();
    private sealed class DefaultRespCommandMap : RespCommandMap
    {
        public override ReadOnlySpan<byte> Map(ReadOnlySpan<byte> command) => command;
        public override bool IsAvailable(ReadOnlySpan<byte> command) => true;
    }
}

/// <summary>
/// Over-arching configuration for a RESP system.
/// </summary>
public class RespConfiguration
{
    private static readonly TimeSpan DefaultSyncTimeout = TimeSpan.FromSeconds(10);
    public static RespConfiguration Default { get; } = new(RespCommandMap.Default, [], DefaultSyncTimeout);

    public static Builder Create() => default; // for discoverability

    public struct Builder // intentionally mutable
    {
        public TimeSpan? SyncTimeout { get; set; }
        public RespCommandMap? CommandMap { get; set; }
        public object? KeyPrefix { get; set; } // can be a string or byte[]

        public Builder(RespConfiguration? source)
        {
            if (source is not null)
            {
                CommandMap = source.RespCommandMap;
                SyncTimeout = source.SyncTimeout;
                KeyPrefix = source.KeyPrefix.ToArray();
            }
        }

        public RespConfiguration Create()
        {
            byte[] prefix = KeyPrefix switch
            {
                null => [],
                string { Length: 0 } => [],
                string s => Encoding.UTF8.GetBytes(s),
                byte[] { Length: 0 } => [],
                byte[] b => b.AsSpan().ToArray(), // create isolated copy for mutability reasons
                _ => throw new ArgumentException("KeyPrefix must be a string or byte[]", nameof(KeyPrefix)),
            };

            if (prefix.Length == 0 && SyncTimeout is null && CommandMap is null) return Default;

            return new(
                CommandMap ?? RespCommandMap.Default,
                prefix,
                SyncTimeout ?? DefaultSyncTimeout);
        }
    }
    private RespConfiguration(RespCommandMap respCommandMap, byte[] keyPrefix, TimeSpan syncTimeout)
    {
        RespCommandMap = respCommandMap;
        SyncTimeout = syncTimeout;
        _keyPrefix = keyPrefix; // create isolated copy
    }
    private readonly byte[] _keyPrefix;

    public RespCommandMap RespCommandMap { get; }
    public TimeSpan SyncTimeout { get; }
    public ReadOnlySpan<byte> KeyPrefix => _keyPrefix;

    public Builder AsBuilder() => new(this);
}

/// <summary>
/// Transient state for a RESP operation.
/// </summary>
public readonly struct RespContext(IRespConnection connection, int database = -1, CancellationToken cancellationToken = default)
{
    public RespContext(IRespConnection connection) : this(connection, -1, CancellationToken.None)
    {
    }
    public RespContext(IRespConnection connection, CancellationToken cancellationToken) : this(connection, -1, cancellationToken)
    {
    }

    public IRespConnection Connection => connection;
    public int Database => database;
    public CancellationToken CancellationToken => cancellationToken;

    public RespMessageBuilder<T> Command<T>(ReadOnlySpan<byte> command, T value, IRespFormatter<T> formatter)
        => new(this, command, value, formatter);
    public RespMessageBuilder<Void> Command(ReadOnlySpan<byte> command)
        => new(this, command, Void.Instance, RespFormatters.Void);

    public RespMessageBuilder<string> Command(ReadOnlySpan<byte> command, string value)
        => new(this, command, value, RespFormatters.String);

    public TResponse Send<TRequest, TResponse>(
        scoped ReadOnlySpan<byte> command,
        TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<TResponse> parser)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
    {
        var bytes = Serialize(RespCommandMap, command, request, formatter, out int length);
        var msg = SyncInternalRespMessage<TResponse>.Create(bytes, length, parser);
        connection.Send(msg);
        return msg.WaitAndRecycle(connection.Configuration.SyncTimeout);
    }

    public Task<TResponse> SendTaskAsync<TRequest, TResponse>(
        scoped ReadOnlySpan<byte> command,
        TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<TResponse> parser)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
        => SendAsync(command, request, formatter, parser).WaitTypedTaskAsync(cancellationToken);

    public ValueTask<TResponse> SendValueTaskAsync<TRequest, TResponse>(
        scoped ReadOnlySpan<byte> command,
        TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<TResponse> parser)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
        => SendAsync(command, request, formatter, parser).WaitTypedValueTaskAsync(cancellationToken);

    public Task SendTaskAsync<TRequest>(
        scoped ReadOnlySpan<byte> command,
        TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<Void> parser)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
        => SendAsync(command, request, formatter, parser).WaitUntypedTaskAsync(cancellationToken);

    public ValueTask SendValueTaskAsync<TRequest>(
        scoped ReadOnlySpan<byte> command,
        TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<Void> parser)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
        => SendAsync(command, request, formatter, parser).WaitUntypedValueTaskAsync(cancellationToken);

    private AsyncInternalRespMessage<TResponse> SendAsync<TRequest, TResponse>(
        ReadOnlySpan<byte> command,
        TRequest request,
        IRespFormatter<TRequest> formatter,
        IRespParser<TResponse> parser)
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Serialize(RespCommandMap, command, request, formatter, out int length);
        var msg = AsyncInternalRespMessage<TResponse>.Create(bytes, length, parser);
        connection.Send(msg);
        return msg;
    }

    public RespCommandMap RespCommandMap => connection.Configuration.RespCommandMap;

    private static byte[] Serialize<TRequest>(RespCommandMap commandMap, ReadOnlySpan<byte> command, TRequest request, IRespFormatter<TRequest> formatter, out int length)
#if NET9_0_OR_GREATER
        where TRequest : allows ref struct
#endif
    {
        int size = 0;
        if (formatter is IRespSizeEstimator<TRequest> estimator)
        {
            size = estimator.EstimateSize(command, request);
        }
        var buffer = AmbientBufferWriter.Get(size);
        try
        {
            var writer = new RespWriter(buffer);
            if (!ReferenceEquals(commandMap, RespCommandMap.Default))
            {
                writer.CommandMap = commandMap;
            }
            formatter.Format(command, ref writer, request);
            writer.Flush();
            return buffer.Detach(out length);
        }
        catch
        {
            buffer.Reset();
            throw;
        }
    }
}
public static class RespConnectionExtensions
{
    /// <summary>
    /// Enforces stricter ordering guarantees, so that unawaited async operations cannot cause overlapping writes.
    /// </summary>
    public static IRespConnection ForPipeline(this IRespConnection connection)
        => connection is PipelinedConnection ? connection : new PipelinedConnection(connection);

    public static IRespConnection WithConfiguration(this IRespConnection connection, RespConfiguration configuration)
        => ReferenceEquals(configuration, connection.Configuration) ? connection : new ConfiguredConnection(connection, configuration);

    private sealed class ConfiguredConnection(IRespConnection tail, RespConfiguration configuration) : IRespConnection
    {
        public void Dispose() => tail.Dispose();

        public ValueTask DisposeAsync() => tail.DisposeAsync();

        public RespConfiguration Configuration => configuration;

        public bool CanWrite => tail.CanWrite;

        public int Outstanding => tail.Outstanding;

        public void Send(IRespMessage message) => tail.Send(message);

        public Task SendAsync(IRespMessage message, CancellationToken cancellationToken = default) => tail.SendAsync(message, cancellationToken);
    }
}
