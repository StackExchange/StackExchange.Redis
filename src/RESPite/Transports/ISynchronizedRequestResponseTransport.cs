using System;

namespace RESPite.Transports;

/// <summary>
/// Marker interface for request/response connections supporting synchronous and asynchronous access.
/// </summary>
public interface ISynchronizedRequestResponseTransport : IAsyncSynchronizedRequestResponseTransport, ISyncSynchronizedRequestResponseTransport,
    IRequestResponseTransport { } // diamond

/// <summary>
/// Marker interface for asynchronous request/response connections.
/// </summary>
public interface IAsyncSynchronizedRequestResponseTransport : IAsyncRequestResponseTransport, ISynchronizedRequestResponseBase { }

/// <summary>
/// Marker interface for synchronous request/response connections.
/// </summary>
public interface ISyncSynchronizedRequestResponseTransport : ISyncRequestResponseTransport, ISynchronizedRequestResponseBase { }

/// <summary>
/// Base marker interface for request/response connections.
/// </summary>
public interface ISynchronizedRequestResponseBase : IRequestResponseBase { }
