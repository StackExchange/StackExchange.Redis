using System.Collections.Generic;

namespace StackExchange.Redis;

/// <summary>
/// Represents an Access Control List (ACL) user with various properties such as flags, passwords, commands, keys, channels, and selectors.
/// </summary>
public class ACLUser
{
    /// <summary>
    /// A dictionary containing user information.
    /// </summary>
    public readonly Dictionary<string, object>? UserInfo;

    /// <summary>
    /// An array of flags associated with the user.
    /// </summary>
    public readonly string[]? Flags;

    /// <summary>
    /// An array of passwords associated with the user.
    /// </summary>
    public readonly string[]? Passwords;

    /// <summary>
    /// A string representing the commands associated with the user.
    /// </summary>
    public readonly string? Commands;

    /// <summary>
    /// A string representing the keys associated with the user.
    /// </summary>
    public readonly string? Keys;

    /// <summary>
    /// A string representing the channels associated with the user.
    /// </summary>
    public readonly string? Channels;

    /// <summary>
    /// An array of selectors associated with the user.
    /// </summary>
    public readonly ACLSelector[]? Selectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="ACLUser"/> class with specified parameters.
    /// </summary>
    /// <param name="userInfo">A dictionary containing user information.</param>
    /// <param name="flags">An array oflags associated with the user.</param>
    /// <param name="passwords">An array opasswords associated with the user.</param>
    /// <param name="commands">A string representing the commands associated with the user.</param>
    /// <param name="keys">A string representing the keys associated with the user.</param>
    /// <param name="channels">A string representing the channels associated with the user.</param>
    /// <param name="selectors">An array oselectors associated with the user.</param>
    public ACLUser(Dictionary<string, object>? userInfo, string[]? flags, string[]? passwords, string? commands, string? keys, string? channels, ACLSelector[]? selectors)
    {
        UserInfo = userInfo;
        Flags = flags;
        Passwords = passwords;
        Commands = commands;
        Keys = keys;
        Channels = channels;
        Selectors = selectors;
    }

    /// <summary>
    /// Returns a string that represents the current object.
    /// </summary>
    /// <returns>A string that represents the current object.</returns>
    public override string ToString()
    {
        return "AccessControlUser{" + "Flags=" + Flags + ", Passwords=" + Passwords
            + ", Commands='" + Commands + "', Keys='" + Keys + "', Channels='" + Channels
            + "', Selectors=" + Selectors + "}";
    }
}

/// <summary>
/// Represents an Access Control List (ACL) selector for a Redis user.
/// </summary>
public class ACLSelector
{
    /// <summary>
    /// Gets the commands associated with the ACL user.
    /// </summary>
    public readonly string? Commands;

    /// <summary>
    /// Gets the keys associated with the ACL user.
    /// </summary>
    public readonly string? Keys;

    /// <summary>
    /// Gets the channels associated with the ACL user.
    /// </summary>
    public readonly string? Channels;

    /// <summary>
    /// Initializes a new instance of the <see cref="ACLSelector"/> class.
    /// </summary>
    /// <param name="commands">The commands associated with the ACLSelector.</param>
    /// <param name="keys">The keys associated with the ACLSelector.</param>
    /// <param name="channels">The channels associated with the ACLSelector.</param>
    public ACLSelector(string? commands, string? keys, string? channels)
    {
        Commands = commands;
        Keys = keys;
        Channels = channels;
    }

    /// <summary>
    /// Returns a string that represents the current object.
    /// </summary>
    /// <returns>A string that represents the current object.</returns>
    public override string ToString()
    {
        return "ACLSelector{" + "Commands='" + Commands + "', Keys='" + Keys + "', Channels='" + Channels + "'}";
    }
}
