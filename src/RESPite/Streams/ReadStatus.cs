namespace RESPite.Streams;

internal enum ReadStatus
{
    NotStarted,
    Init,
    RanToCompletion,
    Faulted,
    ReadSync,
    ReadAsync,
    TransitioningToAsync,
    UpdateWriteTime,
    ProcessBuffer,
    MarkProcessed,
    TryParseResult,
    MatchResult,
    PubSubMessage,
    PubSubPMessage,
    PubSubSMessage,
    Reconfigure,
    InvokePubSub,
    ResponseSequenceCheck, // high-integrity mode only
    DequeueResult,
    ComputeResult,
    CompletePendingMessageSync,
    CompletePendingMessageAsync,
    MatchResultComplete,
    ResetArena,
    ProcessBufferComplete,
    PubSubUnsubscribe,
    NA = -1,
}
