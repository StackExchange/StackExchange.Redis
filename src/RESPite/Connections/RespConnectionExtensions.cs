using RESPite.Connections.Internal;

namespace RESPite.Connections;

public static class RespConnectionExtensions
{
    /// <summary>
    /// Enforces stricter ordering guarantees, so that unawaited async operations cannot cause overlapping writes.
    /// </summary>
    public static RespConnection Synchronized(this RespConnection connection)
        => connection is SynchronizedConnection ? connection : new SynchronizedConnection(in connection.Context);

    public static RespConnection WithConfiguration(this RespConnection connection, RespConfiguration configuration)
        => ReferenceEquals(configuration, connection.Configuration)
            ? connection
            : new ConfiguredConnection(in connection.Context, configuration);
}
