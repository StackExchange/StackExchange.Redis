using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    internal event Action<string?, Exception?, string>? MessageFaulted;
    internal event Action<bool>? Closing;
    internal event Action<string>? PreTransactionExec, TransactionLog, InfoMessage;
    internal event Action<EndPoint, ConnectionType>? Connecting;
    internal event Action<EndPoint, ConnectionType>? Resurrecting;

    partial void OnTrace(string message, string? category);
    static partial void OnTraceWithoutContext(string message, string? category);

    [Conditional("VERBOSE")]
    internal void Trace(string message, [CallerMemberName] string? category = null) => OnTrace(message, category);

    [Conditional("VERBOSE")]
    internal void Trace(bool condition, string message, [CallerMemberName] string? category = null)
    {
        if (condition) OnTrace(message, category);
    }

    [Conditional("VERBOSE")]
    internal static void TraceWithoutContext(string message, [CallerMemberName] string? category = null) => OnTraceWithoutContext(message, category);

    [Conditional("VERBOSE")]
    internal static void TraceWithoutContext(bool condition, string message, [CallerMemberName] string? category = null)
    {
        if (condition) OnTraceWithoutContext(message, category);
    }

    [Conditional("VERBOSE")]
    internal void OnMessageFaulted(Message? msg, Exception? fault, [CallerMemberName] string? origin = default, [CallerFilePath] string? path = default, [CallerLineNumber] int lineNumber = default) =>
        MessageFaulted?.Invoke(msg?.CommandAndKey, fault, $"{origin} ({path}#{lineNumber})");

    [Conditional("VERBOSE")]
    internal void OnInfoMessage(string message) => InfoMessage?.Invoke(message);

    [Conditional("VERBOSE")]
    internal void OnClosing(bool complete) => Closing?.Invoke(complete);

    [Conditional("VERBOSE")]
    internal void OnConnecting(EndPoint endpoint, ConnectionType connectionType) => Connecting?.Invoke(endpoint, connectionType);

    [Conditional("VERBOSE")]
    internal void OnResurrecting(EndPoint endpoint, ConnectionType connectionType) => Resurrecting?.Invoke(endpoint, connectionType);

    [Conditional("VERBOSE")]
    internal void OnPreTransactionExec(Message message) => PreTransactionExec?.Invoke(message.CommandAndKey);

    [Conditional("VERBOSE")]
    internal void OnTransactionLog(string message) => TransactionLog?.Invoke(message);
}
