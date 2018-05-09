using System;
#if FEATURE_SERIALIZATION
using System.Runtime.Serialization;
#endif

#pragma warning disable RCS1194 // Implement exception constructors.
namespace StackExchange.Redis
{
#if FEATURE_SERIALIZATION
    [Serializable]
    public sealed partial class RedisCommandException : Exception
    {
        private RedisCommandException(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }
    }

    [Serializable]
    public sealed partial class RedisTimeoutException : TimeoutException
    {
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

    [Serializable]
    public sealed partial class RedisConnectionException : RedisException
    {
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

    [Serializable]
    public partial class RedisException : Exception
    {
        /// <summary>
        /// Deserialization constructor; not intended for general usage
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="ctx">Serialization context.</param>
        protected RedisException(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }
    }

    [Serializable]
    public sealed partial class RedisServerException : RedisException
    {
        private RedisServerException(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }
    }
#endif

    /// <summary>
    /// Indicates that a command was illegal and was not sent to the server
    /// </summary>
    public sealed partial class RedisCommandException : Exception
    {
        internal RedisCommandException(string message) : base(message) { }
        internal RedisCommandException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Indicates the time allotted for a command or operation has expired.
    /// </summary>
    public sealed partial class RedisTimeoutException : TimeoutException
    {
        internal RedisTimeoutException(string message, CommandStatus commandStatus) : base(message)
        {
            Commandstatus = commandStatus;
        }

        /// <summary>
        /// status of the command while communicating with Redis
        /// </summary>
        public CommandStatus Commandstatus { get; }
    }

    /// <summary>
    /// Indicates a connection fault when communicating with redis
    /// </summary>
    public sealed partial class RedisConnectionException : RedisException
    {
        internal RedisConnectionException(ConnectionFailureType failureType, string message) : this(failureType, message, null, CommandStatus.Unknown) {}

        internal RedisConnectionException(ConnectionFailureType failureType, string message, Exception innerException) : this(failureType, message, innerException, CommandStatus.Unknown) {}

        internal RedisConnectionException(ConnectionFailureType failureType, string message, Exception innerException, CommandStatus commandStatus) : base(message, innerException)
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
    }

    /// <summary>
    /// Indicates an issue communicating with redis
    /// </summary>
    public partial class RedisException : Exception
    {
        internal RedisException(string message) : base(message) { }
        internal RedisException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Indicates an exception raised by a redis server
    /// </summary>
    public sealed partial class RedisServerException : RedisException
    {
        internal RedisServerException(string message) : base(message) { }
    }
}
