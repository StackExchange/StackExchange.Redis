using System;

namespace StackExchange.Redis;

// The status shapes reported for a connection - by GetStatus, the timeout report and the connect log. They began
// nested in PhysicalBridge and PhysicalConnection; they outlive them because the new core reports through the same
// shapes, so a status line, an exception message or a log filter reads the same whichever core produced it.
internal enum BridgeState : byte
{
    Connecting,
    ConnectedEstablishing,
    ConnectedEstablished,
    Disconnected,
}

internal readonly struct BridgeStatus
{
    /// <summary>
    /// Number of messages sent since the last heartbeat was processed.
    /// </summary>
    public int MessagesSinceLastHeartbeat { get; init; }

    /// <summary>
    /// The time this connection was connected at, if it's connected currently.
    /// </summary>
    public DateTime? ConnectedAt { get; init; }

    /// <summary>
    /// Whether the pipe writer is currently active.
    /// </summary>
    public bool IsWriterActive { get; init; }

    /// <summary>
    /// Status of the currently processing backlog, if any.
    /// </summary>
    public BacklogStatus BacklogStatus { get; init; }

    /// <summary>
    /// The number of messages that are in the backlog queue (waiting to be sent when the connection is healthy again).
    /// </summary>
    public int BacklogMessagesPending { get; init; }

    /// <summary>
    /// The number of messages that are in the backlog queue (waiting to be sent when the connection is healthy again).
    /// </summary>
    public int BacklogMessagesPendingCounter { get; init; }

    /// <summary>
    /// The number of messages ever added to the backlog queue in the life of this connection.
    /// </summary>
    public long TotalBacklogMessagesQueued { get; init; }

    /// <summary>
    /// Status for the underlying connection.
    /// </summary>
    public ConnectionStatus Connection { get; init; }

    /// <summary>
    /// The default bridge stats, notable *not* the same as <c>default</c> since initializers don't run.
    /// </summary>
    public static BridgeStatus Zero { get; } = new() { Connection = ConnectionStatus.Zero };

    public override string ToString() =>
        $"MessagesSinceLastHeartbeat: {MessagesSinceLastHeartbeat}, ConnectedAt: {ConnectedAt?.ToString("u") ?? "n/a"}, Writer: {(IsWriterActive ? "Active" : "Inactive")}, BacklogStatus: {BacklogStatus}, BacklogMessagesPending: (Queue: {BacklogMessagesPending}, Counter: {BacklogMessagesPendingCounter}), TotalBacklogMessagesQueued: {TotalBacklogMessagesQueued}, Connection: ({Connection})";
}

internal enum BacklogStatus : byte
{
    Inactive,
    Activating,
    Starting,
    Started,
    CheckingForWork,
    SpinningDown,
    CheckingForTimeout,
    CheckingForTimeoutComplete,
    RecordingTimeout,
    WritingMessage,
    Flushing,
    MarkingInactive,
    RecordingWriteFailure,
    RecordingFault,
    SettingIdle,
    Faulted,
    NotifyingDisposed,
}

internal readonly struct ConnectionStatus
{
    /// <summary>
    /// Number of messages sent outbound, but we don't yet have a response for.
    /// </summary>
    public int MessagesSentAwaitingResponse { get; init; }

    /// <summary>
    /// Bytes available on the socket, not yet read into the pipe.
    /// </summary>
    public long BytesAvailableOnSocket { get; init; }

    /// <summary>
    /// Bytes read from the socket, pending in the reader pipe.
    /// </summary>
    public long BytesInReadPipe { get; init; }

    /// <summary>
    /// Bytes in the writer pipe, waiting to be written to the socket.
    /// </summary>
    public long BytesInWritePipe { get; init; }

    /// <summary>
    /// Byte size of the last result we processed.
    /// </summary>
    public long BytesLastResult { get; init; }

    /// <summary>
    /// Byte size on the buffer that isn't processed yet.
    /// </summary>
    public long BytesInBuffer { get; init; }

    /// <summary>
    /// The inbound pipe reader status.
    /// </summary>
    public ReadStatus ReadStatus { get; init; }

    /// <summary>
    /// The outbound pipe writer status.
    /// </summary>
    public WriteStatus WriteStatus { get; init; }

    public override string ToString() =>
        $"SentAwaitingResponse: {MessagesSentAwaitingResponse}, AvailableOnSocket: {BytesAvailableOnSocket} byte(s), InReadPipe: {BytesInReadPipe} byte(s), InWritePipe: {BytesInWritePipe} byte(s), ReadStatus: {ReadStatus}, WriteStatus: {WriteStatus}";

    /// <summary>
    /// The default connection stats, notable *not* the same as <c>default</c> since initializers don't run.
    /// </summary>
    public static ConnectionStatus Default { get; } = new()
    {
        BytesAvailableOnSocket = -1,
        BytesInReadPipe = -1,
        BytesInWritePipe = -1,
        ReadStatus = ReadStatus.NA,
        WriteStatus = WriteStatus.NA,
    };

    /// <summary>
    /// The zeroed connection stats, which we want to display as zero for default exception cases.
    /// </summary>
    public static ConnectionStatus Zero { get; } = new()
    {
        BytesAvailableOnSocket = 0,
        BytesInReadPipe = 0,
        BytesInWritePipe = 0,
        ReadStatus = ReadStatus.NA,
        WriteStatus = WriteStatus.NA,
    };
}

internal enum WriteStatus
{
    Initializing,
    Idle,
    Writing,
    Flushing,
    Flushed,

    NA = -1,
}

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
    MaintenanceNotification,
    Invalidate, // client-side caching
    NA = -1,
}
