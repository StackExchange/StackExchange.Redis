using System;
using System.Net;

namespace StackExchange.Redis.Profiling
{
    /// <summary>
    /// <para>A profiled command against a redis instance.</para>
    /// <para>
    /// TimeSpans returned by this interface use a high precision timer if possible.
    /// DateTimes returned by this interface are no more precise than DateTime.UtcNow.
    /// </para>
    /// </summary>
    public interface IProfiledCommand
    {
        /// <summary>
        /// The endpoint this command was sent to.
        /// </summary>
        EndPoint EndPoint { get; }

        /// <summary>
        /// The Db this command was sent to.
        /// </summary>
        int Db { get; }

        /// <summary>
        /// The name of this command.
        /// </summary>
        string Command { get; }

        /// <summary>
        /// The CommandFlags the command was submitted with.
        /// </summary>
        CommandFlags Flags { get; }

        /// <summary>
        /// <para>
        /// When this command was *created*, will be approximately
        /// when the paired method of StackExchange.Redis was called but
        /// before that method returned.
        /// </para>
        /// <para>Note that the resolution of the returned DateTime is limited by DateTime.UtcNow.</para>
        /// </summary>
        DateTime CommandCreated { get; }

        /// <summary>
        /// How long this command waited to be added to the queue of pending
        /// redis commands.  A large TimeSpan indicates serious contention for
        /// the pending queue.
        /// </summary>
        TimeSpan CreationToEnqueued { get; }

        /// <summary>
        /// How long this command spent in the pending queue before being sent to redis.
        /// A large TimeSpan can indicate a large number of pending events, large pending events,
        /// or network issues.
        /// </summary>
        TimeSpan EnqueuedToSending { get; }

        /// <summary>
        /// How long before Redis responded to this command and it's response could be handled after it was sent.
        /// A large TimeSpan can indicate a large response body, an overtaxed redis instance, or network issues.
        /// </summary>
        TimeSpan SentToResponse { get; }

        /// <summary>
        /// How long between Redis responding to this command and awaiting consumers being notified.
        /// </summary>
        TimeSpan ResponseToCompletion { get; }

        /// <summary>
        /// <para>How long it took this redis command to be processed, from creation to deserializing the final response.</para>
        /// <para>Note that this TimeSpan *does not* include time spent awaiting a Task in consumer code.</para>
        /// </summary>
        TimeSpan ElapsedTime { get; }

        /// <summary>
        /// <para>
        /// If a command has to be resent due to an ASK or MOVED response from redis (in a cluster configuration),
        /// the second sending of the command will have this property set to the original IProfiledCommand.
        /// </para>
        /// <para>This can only be set if redis is configured as a cluster.</para>
        /// </summary>
        IProfiledCommand? RetransmissionOf { get; }

        /// <summary>
        /// If RetransmissionOf is not null, this property will be set to either Ask or Moved to indicate
        /// what sort of response triggered the retransmission.
        ///
        /// This can be useful for determining the root cause of extra commands.
        /// </summary>
        RetransmissionReasonType? RetransmissionReason { get; }
    }
}
