using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using RESPite;

namespace StackExchange.Redis.Maintenance;

/// <summary>
/// One entry from a cluster slot-migration notification: some slots have moved, or are moving, from one node
/// to another.
/// </summary>
/// <remarks>
/// A single <c>SMIGRATED</c> describes several of these at once, and not necessarily any involving the node
/// that sent it - every node in the cluster reports the same movements, so the sender is not implicitly the
/// source.
/// </remarks>
[Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
public readonly struct ClusterSlotMigration
{
    internal ClusterSlotMigration(EndPoint? source, EndPoint? target, IReadOnlyList<SlotRange> slots, string? raw)
    {
        Source = source;
        Target = target;
        Slots = slots;
        RawSlots = raw;
    }

    /// <summary>
    /// The node the slots are moving from, or <c>null</c> if the server named it in a form that cannot be
    /// dialled.
    /// </summary>
    public EndPoint? Source { get; }

    /// <summary>
    /// The node the slots are moving to, or <c>null</c> if the server named it in a form that cannot be
    /// dialled.
    /// </summary>
    public EndPoint? Target { get; }

    /// <summary>
    /// The slots that are moving.
    /// </summary>
    public IReadOnlyList<SlotRange> Slots { get; }

    /// <summary>
    /// The slots exactly as the server expressed them, for the cases the parsed form does not survive - a
    /// malformed list leaves <see cref="Slots"/> empty and this populated.
    /// </summary>
    public string? RawSlots { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Format.ToString(Source)} -> {Format.ToString(Target)}: {RawSlots}";
}
