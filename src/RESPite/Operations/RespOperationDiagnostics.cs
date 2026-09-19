using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RESPite.Operations;

/// <summary>
/// Everything an operation carries for the sake of explaining itself afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>One struct, because <c>Reset</c> has to be exhaustive.</b> This is the highest-risk part of the
/// core replacement precisely because nothing fails a test when it goes missing: the cost of losing a
/// field is a timeout exception that says "it timed out" instead of naming the problem. Kept as separate
/// fields on the message, clearing them is seven lines and forgetting one is invisible; kept here, it is
/// <c>_diagnostics = default</c> and the compiler owns the completeness.
/// </para>
/// <para>
/// Pooling makes that sharper rather than softer. A recycled operation that leaks a previous life's
/// timestamps does not throw - it produces a report describing the wrong command, which is worse than no
/// report at all.
/// </para>
/// <para>
/// <b>Protocol-level only.</b> Everything here is something the write path itself knows. The host's own
/// per-command object - a profiling record, say - lives in <see cref="HostState"/> as an opaque
/// reference, so RESPite neither depends on it nor has to model it.
/// </para>
/// </remarks>
internal struct RespOperationDiagnostics
{
    /// <summary>When the operation was created, for the profiling timeline.</summary>
    public DateTime CreatedDateTime;

    /// <summary>A <see cref="Stopwatch"/> stamp at creation, for measuring age precisely.</summary>
    public long CreatedTimestamp;

    /// <summary>How far the operation got.</summary>
    public RespCommandStatus Status;

    /// <summary>The connection it was handed to, if any; opaque here.</summary>
    public object? EnqueuedTo;

    /// <summary>The host's own per-command object - profiling and the like; opaque here.</summary>
    public object? HostState;

    /// <summary>The connection's written-byte counter, snapshotted at enqueue.</summary>
    public long QueuedStampSent;

    /// <summary>The connection's read-byte counter, snapshotted at enqueue.</summary>
    public long QueuedStampReceived;

    /// <summary>A tick count taken when the write began, for backlog timeout detection.</summary>
    public int WriteTickCount;

    /// <summary>The high-integrity response token, or zero.</summary>
    public uint HighIntegrityToken;

    /// <summary>
    /// Whether the command provably never reached a socket, so the server cannot have applied it.
    /// </summary>
    /// <remarks>
    /// The status half of the host's <c>FaultContext.NotApplied</c>. The other half - which server errors
    /// describe the server's own state rather than a partly-applied script - is the host's business,
    /// because it is about error taxonomy rather than the write path.
    /// </remarks>
    public readonly bool IsKnownNotApplied
        => Status is RespCommandStatus.WaitingToBeSent or RespCommandStatus.WaitingInBacklog;

    /// <summary>How long since the operation was created.</summary>
    /// <remarks>Zero rather than a nonsense span when the stamp was never taken.</remarks>
    public readonly TimeSpan Age => CreatedTimestamp == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - CreatedTimestamp) / (double)Stopwatch.Frequency);

    /// <summary>Stamp the start of a life. Called once per request, including after a recycle.</summary>
    public void OnCreated()
    {
        CreatedDateTime = DateTime.UtcNow;
        CreatedTimestamp = Stopwatch.GetTimestamp();
        Status = RespCommandStatus.WaitingToBeSent;
    }

    /// <summary>Record being handed to a connection, with its byte counters at that moment.</summary>
    /// <param name="connection">The connection taking it.</param>
    /// <param name="bytesSent">The connection's written-byte counter.</param>
    /// <param name="bytesReceived">The connection's read-byte counter.</param>
    public void OnEnqueued(object? connection, long bytesSent, long bytesReceived)
    {
        EnqueuedTo = connection;
        QueuedStampSent = bytesSent;
        QueuedStampReceived = bytesReceived;
        WriteTickCount = Environment.TickCount;
    }

    /// <summary>Render the operation's own contribution to a timeout report.</summary>
    /// <param name="builder">Where to write it.</param>
    /// <remarks>
    /// <b>Only the operation's half.</b> The report people recognise also carries multiplexer and
    /// connection counters - <c>inst</c>, <c>qu</c>, <c>qs</c>, <c>aw</c>, and the rest - which belong to
    /// whoever owns those. This renders what the operation itself knows, so the host composes rather than
    /// reaches in and reads fields.
    /// </remarks>
    public readonly void Describe(StringBuilder builder)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        builder.Append("status: ").Append(Status);
        builder.Append(", age: ").Append(Age.TotalMilliseconds.ToString("0.##", CultureInfo.InvariantCulture)).Append("ms");
        if (IsKnownNotApplied) builder.Append(", not-applied: True");
        if (EnqueuedTo is { } connection) builder.Append(", enqueued-to: ").Append(connection);
        if (QueuedStampSent >= 0 && (QueuedStampSent | QueuedStampReceived) != 0)
        {
            builder.Append(", qs: ").Append(QueuedStampSent).Append(", qr: ").Append(QueuedStampReceived);
        }

        if (HighIntegrityToken != 0) builder.Append(", hi-token: ").Append(HighIntegrityToken);
    }

    /// <inheritdoc/>
    public override readonly string ToString()
    {
        var builder = new StringBuilder();
        Describe(builder);
        return builder.ToString();
    }
}
