namespace RESPite.Operations;

/// <summary>
/// Where an operation has got to in the write path.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not merely diagnostic.</b> A command the client never handed to a socket cannot have been applied,
/// and that fact bypasses the side-effect cap in the retry policy. Lose the ladder and retry silently
/// becomes more conservative - which nothing fails, and nobody notices.
/// </para>
/// <para>
/// The names and order mirror the host's shipped <c>CommandStatus</c> exactly, including its slightly odd
/// ordering (<see cref="Sent"/> numerically before <see cref="WaitingInBacklog"/>). That is deliberate:
/// a public shipped enum cannot be reordered, and making the mapping an identity beats making it a
/// <c>switch</c> that somebody has to keep correct.
/// </para>
/// </remarks>
internal enum RespCommandStatus
{
    /// <summary>Nothing is known about this operation's progress.</summary>
    Unknown = 0,

    /// <summary>Not yet written to a connection.</summary>
    WaitingToBeSent = 1,

    /// <summary>Handed to a connection to write.</summary>
    Sent = 2,

    /// <summary>Held in a backlog because no connection was available.</summary>
    WaitingInBacklog = 3,
}
