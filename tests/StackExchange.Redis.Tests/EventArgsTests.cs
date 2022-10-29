﻿using System;
using NSubstitute;
using Xunit;

namespace StackExchange.Redis.Tests;

public class EventArgsTests
{
    [Fact]
    public void EventArgsCanBeSubstituted()
    {
        EndPointEventArgs endpointArgsMock
            = Substitute.For<EndPointEventArgs>(default, default);

        RedisErrorEventArgs redisErrorArgsMock
            = Substitute.For<RedisErrorEventArgs>(default, default, default);

        ConnectionFailedEventArgs connectionFailedArgsMock
            = Substitute.For<ConnectionFailedEventArgs>(default, default, default, default, default, default);

        InternalErrorEventArgs internalErrorArgsMock
            = Substitute.For<InternalErrorEventArgs>(default, default, default, default, default);

        HashSlotMovedEventArgs hashSlotMovedArgsMock
            = Substitute.For<HashSlotMovedEventArgs>(default, default, default, default);

        DiagnosticStub stub = new DiagnosticStub();

        stub.ConfigurationChangedBroadcastHandler(default, endpointArgsMock);
        Assert.Equal(stub.Message,DiagnosticStub.ConfigurationChangedBroadcastHandlerMessage);

        stub.ErrorMessageHandler(default, redisErrorArgsMock);
        Assert.Equal(stub.Message, DiagnosticStub.ErrorMessageHandlerMessage);

        stub.ConnectionFailedHandler(default, connectionFailedArgsMock);
        Assert.Equal(stub.Message, DiagnosticStub.ConnectionFailedHandlerMessage);

        stub.InternalErrorHandler(default, internalErrorArgsMock);
        Assert.Equal(stub.Message, DiagnosticStub.InternalErrorHandlerMessage);

        stub.ConnectionRestoredHandler(default, connectionFailedArgsMock);
        Assert.Equal(stub.Message, DiagnosticStub.ConnectionRestoredHandlerMessage);

        stub.ConfigurationChangedHandler(default, endpointArgsMock);
        Assert.Equal(stub.Message, DiagnosticStub.ConfigurationChangedHandlerMessage);

        stub.HashSlotMovedHandler(default, hashSlotMovedArgsMock);
        Assert.Equal(stub.Message, DiagnosticStub.HashSlotMovedHandlerMessage);
    }

    public class DiagnosticStub
    {
        public const string ConfigurationChangedBroadcastHandlerMessage = "ConfigurationChangedBroadcastHandler invoked";
        public const string ErrorMessageHandlerMessage = "ErrorMessageHandler invoked";
        public const string ConnectionFailedHandlerMessage = "ConnectionFailedHandler invoked";
        public const string InternalErrorHandlerMessage = "InternalErrorHandler invoked";
        public const string ConnectionRestoredHandlerMessage = "ConnectionRestoredHandler invoked";
        public const string ConfigurationChangedHandlerMessage = "ConfigurationChangedHandler invoked";
        public const string HashSlotMovedHandlerMessage = "HashSlotMovedHandler invoked";

        public DiagnosticStub()
        {
            ConfigurationChangedBroadcastHandler = (obj, args) => Message = ConfigurationChangedBroadcastHandlerMessage;
            ErrorMessageHandler = (obj, args) => Message = ErrorMessageHandlerMessage;
            ConnectionFailedHandler = (obj, args) => Message = ConnectionFailedHandlerMessage;
            InternalErrorHandler = (obj, args) => Message = InternalErrorHandlerMessage;
            ConnectionRestoredHandler = (obj, args) => Message = ConnectionRestoredHandlerMessage;
            ConfigurationChangedHandler = (obj, args) => Message = ConfigurationChangedHandlerMessage;
            HashSlotMovedHandler = (obj, args) => Message = HashSlotMovedHandlerMessage;
        }

        public string? Message { get; private set; }
        public Action<object?, EndPointEventArgs> ConfigurationChangedBroadcastHandler { get; }
        public Action<object?, RedisErrorEventArgs> ErrorMessageHandler { get; }
        public Action<object?, ConnectionFailedEventArgs> ConnectionFailedHandler { get; }
        public Action<object?, InternalErrorEventArgs> InternalErrorHandler { get; }
        public Action<object?, ConnectionFailedEventArgs> ConnectionRestoredHandler { get; }
        public Action<object?, EndPointEventArgs> ConfigurationChangedHandler { get; }
        public Action<object?, HashSlotMovedEventArgs> HashSlotMovedHandler { get; }
    }
}
