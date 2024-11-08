﻿using System;

namespace RESPite.Transports;

/// <summary>
/// Marker interface for request/response connections supporting synchronous and asynchronous access.
/// </summary>
public interface IRequestResponseTransport : IAsyncRequestResponseTransport, ISyncRequestResponseTransport, IMessageTransport { } // diamond

/// <summary>
/// Marker interface for asynchronous request/response connections.
/// </summary>
public interface IAsyncRequestResponseTransport : IAsyncMessageTransport, IRequestResponseBase, IAsyncDisposable { }

/// <summary>
/// Marker interface for synchronous request/response connections.
/// </summary>
public interface ISyncRequestResponseTransport : ISyncMessageTransport, IRequestResponseBase, IDisposable { }

/// <summary>
/// Base marker interface for request/response connections.
/// </summary>
public interface IRequestResponseBase : IMessageTransportBase { }
