using System;
using System.Runtime.Serialization;

#pragma warning disable RCS1194 // Implement exception constructors.
namespace StackExchange.Redis
{
    /// <summary>
    /// Indicates that a command was illegal and was not sent to the server
    /// </summary>
    [Serializable]
    public sealed partial class RedisCommandException : Exception
    {
        /// <summary>
        /// Creates a new <see cref="RedisCommandException"/>.
        /// </summary>
        /// <param name="message">The message for the exception.</param>
        public RedisCommandException(string message) : base(message) { }

        /// <summary>
        /// Creates a new <see cref="RedisCommandException"/>.
        /// </summary>
        /// <param name="message">The message for the exception.</param>
        /// <param name="innerException">The inner exception.</param>
        public RedisCommandException(string message, Exception innerException) : base(message, innerException) { }

        private RedisCommandException(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }
    }

    /// <summary>
    /// Indicates the time allotted for a command or operation has expired.
    /// </summary>
    [Serializable]
    public sealed partial class RedisTimeoutException : TimeoutException
    {
        /// <summary>
        /// Creates a new <see cref="RedisTimeoutException"/>.
        /// </summary>
        /// <param name="message">The message for the exception.</param>
        /// <param name="commandStatus">The command status, as of when the timeout happened.</param>
        public RedisTimeoutException(string message, CommandStatus commandStatus) : base(message)
        {
            Commandstatus = commandStatus;
        }

        /// <summary>
        /// status of the command while communicating with Redis
        /// </summary>
        public CommandStatus Commandstatus { get; }

        private RedisTimeoutException(SerializationInfo info, StreamingContext ctx) : base(info, ctx)
        {
            Commandstatus = (CommandStatus)info.GetValue("commandStatus", typeof(CommandStatus));
        }
        /// <summary>
        /// Serialization implementation; not intended for general usage
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Serialization context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("commandStatus", Commandstatus);
        }
    }

    /// <summary>
    /// Indicates a connection fault when communicating with redis
    /// </summary>
    [Serializable]
    public sealed partial class RedisConnectionException : RedisException
    {
        /// <summary>
        /// Creates a new <see cref="RedisConnectionException"/>.
        /// </summary>
        /// <param name="failureType">The type of connection failure.</param>
        /// <param name="message">The message for the exception.</param>
        public RedisConnectionException(ConnectionFailureType failureType, string message) : this(failureType, message, null, CommandStatus.Unknown) {}

        /// <summary>
        /// Creates a new <see cref="RedisConnectionException"/>.
        /// </summary>
        /// <param name="failureType">The type of connection failure.</param>
        /// <param name="message">The message for the exception.</param>
        /// <param name="innerException">The inner exception.</param>
        public RedisConnectionException(ConnectionFailureType failureType, string message, Exception innerException) : this(failureType, message, innerException, CommandStatus.Unknown) {}

        /// <summary>
        /// Creates a new <see cref="RedisConnectionException"/>.
        /// </summary>
        /// <param name="failureType">The type of connection failure.</param>
        /// <param name="message">The message for the exception.</param>
        /// <param name="innerException">The inner exception.</param>
        /// <param name="commandStatus">The status of the command.</param>
        public RedisConnectionException(ConnectionFailureType failureType, string message, Exception innerException, CommandStatus commandStatus) : base(message, innerException)
        {
            FailureType = failureType;
            CommandStatus = commandStatus;
        }

        /// <summary>
        /// The type of connection failure
        /// </summary>
        public ConnectionFailureType FailureType { get; }

        /// <summary>
        /// status of the command while communicating with Redis
        /// </summary>
        public CommandStatus CommandStatus { get; }

        private RedisConnectionException(SerializationInfo info, StreamingContext ctx) : base(info, ctx)
        {
            FailureType = (ConnectionFailureType)info.GetInt32("failureType");
            CommandStatus = (CommandStatus)info.GetValue("commandStatus", typeof(CommandStatus));
        }
        /// <summary>
        /// Serialization implementation; not intended for general usage
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Serialization context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("failureType", (int)FailureType);
            info.AddValue("commandStatus", CommandStatus);
        }
    }

    /// <summary>
    /// Indicates an issue communicating with redis
    /// </summary>
    [Serializable]
    public partial class RedisException : Exception
    {
        /// <summary>
        /// Creates a new <see cref="RedisException"/>.
        /// </summary>
        /// <param name="message">The message for the exception.</param>
        public RedisException(string message) : base(message) { }

        /// <summary>
        /// Creates a new <see cref="RedisException"/>.
        /// </summary>
        /// <param name="message">The message for the exception.</param>
        /// <param name="innerException">The inner exception.</param>
        public RedisException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// Deserialization constructor; not intended for general usage
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="ctx">Serialization context.</param>
        protected RedisException(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }
    }

    /// <summary>
    /// Indicates an exception raised by a redis server
    /// </summary>
    [Serializable]
    public sealed partial class RedisServerException : RedisException
    {
        /// <summary>
        /// Creates a new <see cref="RedisServerException"/>.
        /// </summary>
        /// <param name="message">The message for the exception.</param>
        public RedisServerException(string message) : base(message) { }

        private RedisServerException(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }
    }
}
