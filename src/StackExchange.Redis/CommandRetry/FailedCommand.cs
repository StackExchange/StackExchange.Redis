using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Command retry policy to act as a no-op for all commands.
    /// </summary>
    public sealed class FailedCommand
    {
        /// <summary>
        /// The original/inner message that failed.
        /// </summary>
        internal Message Message;

        /// <summary>
        /// Status of the command.
        /// </summary>
        public CommandStatus Status => Message.Status;

        /// <summary>
        /// The redis command sent.
        /// </summary>
        public string CommandAndKey => Message.CommandAndKey;

        /// <summary>
        /// The reason this command failed, e.g. no connection, timeout, etc.
        /// </summary>
        public CommandFailureReason FailureReason { get; }

        /// <summary>
        /// The exception that happened to create this failed command.
        /// </summary>
        public Exception Exception { get; }

        internal static FailedCommand FromWriteFail(Message message, Exception exception) =>
            new FailedCommand(message, CommandFailureReason.WriteFailure, exception);

        internal FailedCommand(Message message, CommandFailureReason reason, Exception exception)
        {
            Message = message;
            FailureReason = reason;
            Exception = exception;
        }
    }
}
