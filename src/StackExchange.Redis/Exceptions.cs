using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace StackExchange.Redis
{
    /// <summary>
    /// Indicates that a command was illegal and was not sent to the server.
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

#if NET8_0_OR_GREATER
        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId)]
#endif
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
        [Obsolete("Prefer the overload that specifies CommandFlags")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public RedisTimeoutException(string message, CommandStatus commandStatus) : this(CommandFlags.CommandRetryNever, message, commandStatus) { }

        /// <summary>
        /// Creates a new <see cref="RedisTimeoutException"/>.
        /// </summary>
        /// <param name="flags">The command-flags associated with the faulting operation.</param>
        /// <param name="message">The message for the exception.</param>
        /// <param name="commandStatus">The command status, as of when the timeout happened.</param>
        public RedisTimeoutException(CommandFlags flags, string message, CommandStatus commandStatus) : base(message)
        {
            Flags = flags;
            Commandstatus = commandStatus;
        }

        /// <summary>
        /// status of the command while communicating with Redis.
        /// </summary>
        public CommandStatus Commandstatus { get; }

        /// <summary>
        /// The command-flags associated with the faulting operation (including its retry category).
        /// </summary>
        public CommandFlags Flags { get; }

#if NET8_0_OR_GREATER
        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId)]
#endif
        private RedisTimeoutException(SerializationInfo info, StreamingContext ctx) : base(info, ctx)
        {
            Commandstatus = info.GetValue("commandStatus", typeof(CommandStatus)) as CommandStatus? ?? CommandStatus.Unknown;
        }

        /// <summary>
        /// Serialization implementation; not intended for general usage.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Serialization context.</param>
#if NET8_0_OR_GREATER
        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId)]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
#endif
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("commandStatus", Commandstatus);
        }
    }

    /// <summary>
    /// Indicates a connection fault when communicating with redis.
    /// </summary>
    [Serializable]
    public sealed partial class RedisConnectionException : RedisException
    {
        /// <summary>
        /// Creates a new <see cref="RedisConnectionException"/>.
        /// </summary>
        /// <param name="failureType">The type of connection failure.</param>
        /// <param name="message">The message for the exception.</param>
        [Obsolete("Prefer the overload that specifies CommandFlags")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public RedisConnectionException(ConnectionFailureType failureType, string message) : this(failureType, CommandFlags.CommandRetryNever, message, null, CommandStatus.Unknown) { }

        /// <summary>
        /// Creates a new <see cref="RedisConnectionException"/>.
        /// </summary>
        /// <param name="failureType">The type of connection failure.</param>
        /// <param name="message">The message for the exception.</param>
        /// <param name="innerException">The inner exception.</param>
        [Obsolete("Prefer the overload that specifies CommandFlags")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public RedisConnectionException(ConnectionFailureType failureType, string message, Exception? innerException) : this(failureType, CommandFlags.CommandRetryNever, message, innerException, CommandStatus.Unknown) { }

        /// <summary>
        /// Creates a new <see cref="RedisConnectionException"/>.
        /// </summary>
        /// <param name="failureType">The type of connection failure.</param>
        /// <param name="message">The message for the exception.</param>
        /// <param name="innerException">The inner exception.</param>
        /// <param name="commandStatus">The status of the command.</param>
        [Obsolete("Prefer the overload that specifies CommandFlags")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public RedisConnectionException(ConnectionFailureType failureType, string message, Exception? innerException, CommandStatus commandStatus) : this(failureType, CommandFlags.CommandRetryNever, message, innerException, commandStatus) { }

        /// <summary>
        /// Creates a new <see cref="RedisConnectionException"/>.
        /// </summary>
        /// <param name="failureType">The type of connection failure.</param>
        /// <param name="flags">The command-flags associated with the faulting operation.</param>
        /// <param name="message">The message for the exception.</param>
        /// <param name="innerException">The inner exception.</param>
        /// <param name="commandStatus">The status of the command.</param>
        public RedisConnectionException(ConnectionFailureType failureType, CommandFlags flags, string message, Exception? innerException = null, CommandStatus commandStatus = CommandStatus.Unknown) : base(message, innerException)
        {
            FailureType = failureType;
            Flags = flags;
            CommandStatus = commandStatus;
        }

        /// <summary>
        /// The type of connection failure.
        /// </summary>
        public ConnectionFailureType FailureType { get; }

        /// <summary>
        /// The command-flags associated with the faulting operation (including its retry category).
        /// </summary>
        public CommandFlags Flags { get; }

        /// <summary>
        /// Status of the command while communicating with Redis.
        /// </summary>
        public CommandStatus CommandStatus { get; }

#if NET8_0_OR_GREATER
        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId)]
#endif
        private RedisConnectionException(SerializationInfo info, StreamingContext ctx) : base(info, ctx)
        {
            FailureType = (ConnectionFailureType)info.GetInt32("failureType");
            CommandStatus = info.GetValue("commandStatus", typeof(CommandStatus)) as CommandStatus? ?? CommandStatus.Unknown;
        }

        /// <summary>
        /// Serialization implementation; not intended for general usage.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Serialization context.</param>
#if NET8_0_OR_GREATER
        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId)]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
#endif
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("failureType", (int)FailureType);
            info.AddValue("commandStatus", CommandStatus);
        }
    }

    /// <summary>
    /// Indicates an issue communicating with redis.
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
        public RedisException(string message, Exception? innerException) : base(message, innerException) { }

        /// <summary>
        /// Deserialization constructor; not intended for general usage.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="ctx">Serialization context.</param>
#if NET8_0_OR_GREATER
        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId)]
#endif
        protected RedisException(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }
    }

    /// <summary>
    /// Indicates an exception raised by a redis server.
    /// </summary>
    [Serializable]
    public sealed partial class RedisServerException : RedisException
    {
        /// <summary>
        /// Creates a new <see cref="RedisServerException"/>.
        /// </summary>
        /// <param name="message">The message for the exception.</param>
        [Obsolete("Specify Kind and CommandFlags when possible")]
        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        public RedisServerException(string message) : this(RedisErrorKind.Unknown, CommandFlags.CommandRetryNever, message) { }

        /// <summary>
        /// Creates a new <see cref="RedisServerException"/>.
        /// </summary>
        /// <param name="kind">The categorized meaning of the error.</param>
        /// <param name="flags">The command-flags associated with the faulting operation.</param>
        /// <param name="message">The message for the exception.</param>
        public RedisServerException(RedisErrorKind kind, CommandFlags flags, string message) : base(message)
        {
            Kind = kind;
            Flags = flags;
        }

#if NET8_0_OR_GREATER
        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId)]
#endif
        private RedisServerException(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }

        /// <summary>
        /// Identifies the kind of error received.
        /// </summary>
        public RedisErrorKind Kind { get; }

        /// <summary>
        /// The command-flags associated with the faulting operation (including its retry category).
        /// </summary>
        public CommandFlags Flags { get; }
    }
}
