namespace RESPite.Transports;

/// <summary>
/// Marker interface for pipelined connections supporting synchronous and asynchronous access.
/// </summary>
public interface IPipelinedTransport : IAsyncPipelinedTransport, ISyncPipelinedTransport { } // diamond

/// <summary>
/// Marker interface for asynchronous pipelined connections.
/// </summary>
public interface IAsyncPipelinedTransport : IAsyncMessageTransport { }

/// <summary>
/// Marker interface for synchronous pipelined connections.
/// </summary>
public interface ISyncPipelinedTransport : ISyncMessageTransport { }

/// <summary>
/// Base marker interface for pipelined connections.
/// </summary>
public interface IPipelinedBase : IMessageTransportBase { }
