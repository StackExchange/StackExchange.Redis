using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    /// <summary>
    /// Internal interface for executing Redis messages synchronously and asynchronously.
    /// </summary>
    internal interface IMessageExecutor
    {
        /// <summary>
        /// Gets the connection multiplexer.
        /// </summary>
        IInternalConnectionMultiplexer Multiplexer { get; }

        /// <summary>
        /// Gets the command map.
        /// </summary>
        CommandMap CommandMap { get; }

        /// <summary>
        /// Gets the unique identifier for this multiplexer.
        /// </summary>
        ReadOnlyMemory<byte> UniqueId { get; }

        /// <summary>
        /// Select a server for the given message.
        /// </summary>
        /// <param name="message">The message to execute.</param>
        /// <returns>The selected server endpoint.</returns>
        ServerEndPoint? SelectServer(Message message);

        /// <summary>
        /// Select a server for the given command.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        /// <param name="flags">The command flags.</param>
        /// <param name="key">The key being accessed.</param>
        /// <returns>The selected server endpoint.</returns>
        ServerEndPoint? SelectServer(RedisCommand command, CommandFlags flags, in RedisKey key);

        /// <summary>
        /// Execute a message asynchronously with a default value.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="message">The message to execute.</param>
        /// <param name="processor">The result processor.</param>
        /// <param name="state">The async state.</param>
        /// <param name="server">The server to execute on.</param>
        /// <param name="defaultValue">The default value to return if the message is null.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task<T> ExecuteAsyncImpl<T>(Message? message, ResultProcessor<T>? processor, object? state, ServerEndPoint? server, T defaultValue);

        /// <summary>
        /// Execute a message asynchronously.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="message">The message to execute.</param>
        /// <param name="processor">The result processor.</param>
        /// <param name="state">The async state.</param>
        /// <param name="server">The server to execute on.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task<T?> ExecuteAsyncImpl<T>(Message? message, ResultProcessor<T>? processor, object? state, ServerEndPoint? server);

        /// <summary>
        /// Execute a message synchronously.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="message">The message to execute.</param>
        /// <param name="processor">The result processor.</param>
        /// <param name="server">The server to execute on.</param>
        /// <param name="defaultValue">The default value to return if the message is null.</param>
        /// <returns>The result of the operation.</returns>
        [return: NotNullIfNotNull("defaultValue")]
        T? ExecuteSyncImpl<T>(Message message, ResultProcessor<T>? processor, ServerEndPoint? server, T? defaultValue = default);

        /// <summary>
        /// Validates a message before execution.
        /// </summary>
        /// <param name="message">The message to validate.</param>
        void CheckMessage(Message message);
    }
}
