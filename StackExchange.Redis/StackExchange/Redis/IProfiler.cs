using System;
using System.Net;

namespace StackExchange.Redis
{
    /// <summary>
    /// If an IProfiledCommand is a retransmission of a previous command, this enum
    /// is used to indicate what prompted the retransmission.
    /// 
    /// This can be used to distinguish between transient causes (moving hashslots, joining nodes, etc.)
    /// and incorrect routing.
    /// </summary>
    public enum RetransmissionReasonType
    {
        /// <summary>
        /// No stated reason
        /// </summary>
        None = 0,
        /// <summary>
        /// Issued to investigate which node owns a key
        /// </summary>
        Ask,
        /// <summary>
        /// A node has indicated that it does *not* own the given key
        /// </summary>
        Moved
    }

    /// <summary>
    /// A profiled command against a redis instance.
    /// 
    /// TimeSpans returned by this interface use a high precision timer if possible.
    /// DateTimes returned by this interface are no more precise than DateTime.UtcNow.
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
        /// When this command was *created*, will be approximately
        /// when the paired method of StackExchange.Redis was called but
        /// before that method returned.
        /// 
        /// Note that the resolution of the returned DateTime is limited by DateTime.UtcNow.
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
        /// How long it took this redis command to be processed, from creation to deserializing the final resposne.
        /// 
        /// Note that this TimeSpan *does not* include time spent awaiting a Task in consumer code.
        /// </summary>
        TimeSpan ElapsedTime { get; }

        /// <summary>
        /// If a command has to be resent due to an ASK or MOVED response from redis (in a cluster configuration),
        /// the second sending of the command will have this property set to the original IProfiledCommand.
        /// 
        /// This can only be set if redis is configured as a cluster.
        /// </summary>
        IProfiledCommand RetransmissionOf { get; }

        /// <summary>
        /// If RetransmissionOf is not null, this property will be set to either Ask or Moved to indicate
        /// what sort of response triggered the retransmission.
        /// 
        /// This can be useful for determining the root cause of extra commands.
        /// </summary>
        RetransmissionReasonType? RetransmissionReason { get; }
    }

    /// <summary>
    /// Interface for profiling individual commands against an Redis ConnectionMulitplexer.
    /// </summary>
    public interface IProfiler
    {
        /// <summary>
        /// Called to provide a context object.
        /// 
        /// This method is called before the method which triggers work against redis (such as StringSet(Async)) returns,
        /// and will always be called on the same thread as that method.
        /// 
        /// Note that GetContext() may be called even if ConnectionMultiplexer.BeginProfiling() has not been called.
        /// You may return `null` to prevent any tracking of commands.
        /// </summary>
        object GetContext();
    }
}
