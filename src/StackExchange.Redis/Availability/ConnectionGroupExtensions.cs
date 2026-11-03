namespace StackExchange.Redis.Availability;

/// <summary>
/// Convenience methods for working with an <see cref="IConnectionGroup"/>.
/// </summary>
public static class ConnectionGroupExtensions
{
    /// <summary>
    /// Find the member of the group that the sender of an event originated from, if any.
    /// </summary>
    /// <param name="group">The group that raised the event.</param>
    /// <param name="sender">The <c>sender</c> passed to an event handler.</param>
    /// <returns>The member that the event came from, or <c>null</c> if the sender is not (or is no longer) one of this group's members,
    /// including when the event was raised by the group itself.</returns>
    /// <remarks>See <see cref="ConnectionGroupMember.IsSenderOf"/>.</remarks>
    public static ConnectionGroupMember? FindMember(this IConnectionGroup group, object? sender)
    {
        if (group is null || sender is null) return null;
        foreach (var member in group.GetMembers())
        {
            if (member.IsSenderOf(sender)) return member;
        }
        return null;
    }
}
