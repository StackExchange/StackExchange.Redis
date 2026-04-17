using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using RESPite;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

/// <summary>
/// A group of connections to redis servers, that manages connections to multiple
/// servers, routing traffic based on the availability of the servers and their
/// relative <see cref="ConnectionGroupMember.Weight"/>.
/// </summary>
[Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
public interface IConnectionGroup : IConnectionMultiplexer
{
    /// <summary>
    /// A change occured to one of the connection groups.
    /// </summary>
    event EventHandler<GroupConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>
    /// Adds a new member to the group.
    /// </summary>
    Task AddAsync(ConnectionGroupMember member, TextWriter? log = null);

    /// <summary>
    /// Attempt to explicitly switch to the specified member, or remove an explicit failover if <paramref name="member"/> is <see langword="null"/>.
    /// A member specified via this method will be preferred over other members (ignoring <see cref="ConnectionGroupMember.Weight"/> and <see cref="ConnectionGroupMember.Latency"/>)
    /// until the failover is removed, or the connection is no longer reachable.
    /// </summary>
    /// <param name="member">The member to switch to, or <see langword="null"/> to remove an explicit failover.</param>
    /// <returns><see langword="true"/> if the failover was successful (i.e. the member can be used), <see langword="false"/> otherwise.</returns>
    bool TryFailoverTo(ConnectionGroupMember? member);

    /// <summary>
    /// Removes a member from the group.
    /// </summary>
    bool Remove(ConnectionGroupMember member);

    /// <summary>
    /// Get the members of the group.
    /// </summary>
    ReadOnlySpan<ConnectionGroupMember> GetMembers();

    /// <summary>
    /// Gets the currently active member.
    /// </summary>
    ConnectionGroupMember? ActiveMember { get; }
}

/// <summary>
/// Represents a change to a connection group.
/// </summary>
[Experimental(Experiments.ActiveActive, UrlFormat = Experiments.UrlFormat)]
public class GroupConnectionChangedEventArgs(GroupConnectionChangedEventArgs.ChangeType type, ConnectionGroupMember group, ConnectionGroupMember? previousGroup = null) : EventArgs, ICompletable
{
    /// <summary>
    /// The group relating to the change. For <see cref="ChangeType.ActiveChanged"/>, this is the new group.
    /// </summary>
    public ConnectionGroupMember Group => group;

    /// <summary>
    /// The previous group relating to the change, if applicable.
    /// </summary>
    public ConnectionGroupMember? PreviousGroup => previousGroup;

    /// <summary>
    /// The type of change that occurred.
    /// </summary>
    public ChangeType Type => type;

    private EventHandler<GroupConnectionChangedEventArgs>? _handler;
    private object? _sender;

    /// <summary>
    /// The type of change that occurred.
    /// </summary>
    public enum ChangeType
    {
        /// <summary>
        /// Unused.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// A new connection group was added.
        /// </summary>
        Added = 1,

        /// <summary>
        /// A connection group was removed.
        /// </summary>
        Removed = 2,

        /// <summary>
        /// A connection group became disconnected.
        /// </summary>
        Disconnected = 3,

        /// <summary>
        /// A connection group became reconnected.
        /// </summary>
        Reconnected = 4,

        /// <summary>
        /// The active connection group changed, changing how traffic is routed.
        /// </summary>
        ActiveChanged = 5,
    }

    internal void CompleteAsWorker(EventHandler<GroupConnectionChangedEventArgs> handler, object sender)
    {
        _handler = handler;
        _sender = sender;
        ConnectionMultiplexer.CompleteAsWorker(this);
    }

    void ICompletable.AppendStormLog(StringBuilder sb) { }

    bool ICompletable.TryComplete(bool isAsync) => ConnectionMultiplexer.TryCompleteHandler(_handler, _sender!, this, isAsync);
}
