using System.Collections.Generic;

namespace StackExchange.Redis;

/// <summary>
/// To control Access Control List config of individual users with ACL SETUSER command.
/// </summary>
/// <seealso href="https://redis.io/docs/latest/commands/acl/"/>
/// <seealso href="https://redis.io/docs/latest/commands/acl-setuser/"/>
public class ACLRules
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ACLRules"/> class.
    /// </summary>
    /// <param name="aclUserRules">The ACL user rules.</param>
    /// <param name="aclCommandRules">The ACL command rules.</param>
    /// <param name="aclSelectorRules">The ACL selector rules.</param>
    public ACLRules(
        ACLUserRules? aclUserRules,
        ACLCommandRules? aclCommandRules,
        ACLSelectorRules[]? aclSelectorRules)
    {
        AclUserRules = aclUserRules;
        AclCommandRules = aclCommandRules;
        AclSelectorRules = aclSelectorRules;
    }

    /// <summary>
    /// Gets the ACL user rules.
    /// </summary>
    public readonly ACLUserRules? AclUserRules;

    /// <summary>
    /// Gets the ACL command rules.
    /// </summary>
    public readonly ACLCommandRules? AclCommandRules;

    /// <summary>
    /// Gets the ACL selector rules.
    /// </summary>
    public readonly ACLSelectorRules[]? AclSelectorRules;

    /// <summary>
    /// Converts the ACL rules to Redis values.
    /// </summary>
    /// <returns>An array of Redis values representing the ACL rules.</returns>
    internal RedisValue[] ToRedisValues()
    {
        var redisValues = new List<RedisValue>();
        AclUserRules?.AppendTo(redisValues);
        AclCommandRules?.AppendTo(redisValues);

        if (AclSelectorRules is not null)
        {
            foreach (var rules in AclSelectorRules)
            {
                rules.AppendTo(redisValues);
            }
        }
        return redisValues.ToArray();
    }
}

/// <summary>
/// Represents the ACL user rules.
/// </summary>
public class ACLUserRules
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ACLUserRules"/> class.
    /// </summary>
    /// <param name="resetUser">If set to <c>true</c>, resets the user.</param>
    /// <param name="noPass">If set to <c>true</c>, no password is required.</param>
    /// <param name="resetPass">If set to <c>true</c>, resets the password.</param>
    /// <param name="userState">The state of the user.</param>
    /// <param name="passwordsToSet">The passwords to set.</param>
    /// <param name="passwordsToRemove">The passwords to remove.</param>
    /// <param name="hashedPasswordsToSet">The hashed passwords to set.</param>
    /// <param name="hashedPasswordsToRemove">The hashed passwords to remove.</param>
    /// <param name="clearSelectors">If set to <c>true</c>, clears the selectors.</param>
    public ACLUserRules(
        bool resetUser,
        bool noPass,
        bool resetPass,
        ACLUserState? userState,
        string[]? passwordsToSet,
        string[]? passwordsToRemove,
        string[]? hashedPasswordsToSet,
        string[]? hashedPasswordsToRemove,
        bool clearSelectors)
    {
        ResetUser = resetUser;
        NoPass = noPass;
        ResetPass = resetPass;
        UserState = userState;
        PasswordsToSet = passwordsToSet;
        PasswordsToRemove = passwordsToRemove;
        HashedPasswordsToSet = hashedPasswordsToSet;
        HashedPasswordsToRemove = hashedPasswordsToRemove;
        ClearSelectors = clearSelectors;
    }

    /// <summary>
    /// Gets a value indicating whether the user is reset.
    /// </summary>
    public readonly bool ResetUser;

    /// <summary>
    /// Gets a value indicating whether no password is required.
    /// </summary>
    public readonly bool NoPass;

    /// <summary>
    /// Gets a value indicating whether the password is reset.
    /// </summary>
    public readonly bool ResetPass;

    /// <summary>
    /// Gets the state of the user.
    /// </summary>
    public readonly ACLUserState? UserState;

    /// <summary>
    /// Gets the passwords to set.
    /// </summary>
    public readonly string[]? PasswordsToSet;

    /// <summary>
    /// Gets the passwords to remove.
    /// </summary>
    public readonly string[]? PasswordsToRemove;

    /// <summary>
    /// Gets the hashed passwords to set.
    /// </summary>
    public readonly string[]? HashedPasswordsToSet;

    /// <summary>
    /// Gets the hashed passwords to remove.
    /// </summary>
    public readonly string[]? HashedPasswordsToRemove;

    /// <summary>
    /// Gets a value indicating whether the selectors are cleared.
    /// </summary>
    public readonly bool ClearSelectors;

    /// <summary>
    /// Appends the ACL user rules to the specified list of Redis values.
    /// </summary>
    /// <param name="redisValues">The list of Redis values.</param>
    internal void AppendTo(List<RedisValue> redisValues)
    {
        if (ResetUser)
        {
            redisValues.Add(RedisLiterals.RESET);
        }
        if (NoPass)
        {
            redisValues.Add(RedisLiterals.NOPASS);
        }
        if (ResetPass)
        {
            redisValues.Add(RedisLiterals.RESETPASS);
        }
        if (UserState.HasValue)
        {
            redisValues.Add(UserState.ToString());
        }
        if (PasswordsToSet is not null)
        {
            foreach (var password in PasswordsToSet)
            {
                redisValues.Add(">" + password);
            }
        }
        if (PasswordsToRemove is not null)
        {
            foreach (var password in PasswordsToRemove)
            {
                redisValues.Add("<" + password);
            }
        }
        if (HashedPasswordsToSet is not null)
        {
            foreach (var password in HashedPasswordsToSet)
            {
                redisValues.Add("#" + password);
            }
        }
        if (HashedPasswordsToRemove is not null)
        {
            foreach (var password in HashedPasswordsToRemove)
            {
                redisValues.Add("!" + password);
            }
        }
        if (ClearSelectors)
        {
            redisValues.Add(RedisLiterals.CLEARSELECTORS);
        }
    }
}

/// <summary>
/// Represents the ACL command rules.
/// </summary>
public class ACLCommandRules
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ACLCommandRules"/> class.
    /// </summary>
    /// <param name="commandsRule">The commands rule.</param>
    /// <param name="commandsAllowed">The commands allowed.</param>
    /// <param name="commandsDisallowed">The commands disallowed.</param>
    /// <param name="categoriesAllowed">The categories allowed.</param>
    /// <param name="categoriesDisallowed">The categories disallowed.</param>
    /// <param name="keysRule">The keys rule.</param>
    /// <param name="keysAllowedPatterns">The keys allowed patterns.</param>
    /// <param name="keysAllowedReadForPatterns">The keys allowed read-for patterns.</param>
    /// <param name="keysAllowedWriteForPatterns">The keys allowed write-for patterns.</param>
    /// <param name="pubSubRule">The pub/sub rule.</param>
    /// <param name="pubSubAllowChannels">The pub/sub allow channels.</param>
    public ACLCommandRules(
        ACLCommandsRule? commandsRule,
        string[]? commandsAllowed,
        string[]? commandsDisallowed,
        string[]? categoriesAllowed,
        string[]? categoriesDisallowed,
        ACLKeysRule? keysRule,
        string[]? keysAllowedPatterns,
        string[]? keysAllowedReadForPatterns,
        string[]? keysAllowedWriteForPatterns,
        ACLPubSubRule? pubSubRule,
        string[]? pubSubAllowChannels)
    {
        CommandsRule = commandsRule;
        CommandsAllowed = commandsAllowed;
        CommandsDisallowed = commandsDisallowed;
        CategoriesAllowed = categoriesAllowed;
        CategoriesDisallowed = categoriesDisallowed;
        KeysRule = keysRule;
        KeysAllowedPatterns = keysAllowedPatterns;
        KeysAllowedReadForPatterns = keysAllowedReadForPatterns;
        KeysAllowedWriteForPatterns = keysAllowedWriteForPatterns;
        PubSubRule = pubSubRule;
        PubSubAllowChannels = pubSubAllowChannels;
    }

    /// <summary>
    /// Gets the commands rule.
    /// </summary>
    public readonly ACLCommandsRule? CommandsRule;

    /// <summary>
    /// Gets the commands allowed.
    /// </summary>
    public readonly string[]? CommandsAllowed;

    /// <summary>
    /// Gets the commands disallowed.
    /// </summary>
    public readonly string[]? CommandsDisallowed;

    /// <summary>
    /// Gets the categories allowed.
    /// </summary>
    public readonly string[]? CategoriesAllowed;

    /// <summary>
    /// Gets the categories disallowed.
    /// </summary>
    public readonly string[]? CategoriesDisallowed;

    /// <summary>
    /// Gets the keys rule.
    /// </summary>
    public readonly ACLKeysRule? KeysRule;

    /// <summary>
    /// Gets the keys allowed patterns.
    /// </summary>
    public readonly string[]? KeysAllowedPatterns;

    /// <summary>
    /// Gets the keys allowed read-for patterns.
    /// </summary>
    public readonly string[]? KeysAllowedReadForPatterns;

    /// <summary>
    /// Gets the keys allowed write-for patterns.
    /// </summary>
    public readonly string[]? KeysAllowedWriteForPatterns;

    /// <summary>
    /// Gets the pub/sub rule.
    /// </summary>
    public readonly ACLPubSubRule? PubSubRule;

    /// <summary>
    /// Gets the pub/sub allow channels.
    /// </summary>
    public readonly string[]? PubSubAllowChannels;

    /// <summary>
    /// Appends the ACL command rules to the specified list of Redis values.
    /// </summary>
    /// <param name="redisValues">The list of Redis values.</param>
    internal void AppendTo(List<RedisValue> redisValues)
    {
        if (CommandsRule.HasValue)
        {
            redisValues.Add(CommandsRule.ToString());
        }
        if (CommandsAllowed is not null)
        {
            foreach (var command in CommandsAllowed)
            {
                redisValues.Add(RedisLiterals.PlusSymbol + command);
            }
        }
        if (CommandsDisallowed is not null)
        {
            foreach (var command in CommandsDisallowed)
            {
                redisValues.Add(RedisLiterals.MinusSymbol + command);
            }
        }
        if (CategoriesAllowed is not null)
        {
            foreach (var category in CategoriesAllowed)
            {
                redisValues.Add("+@" + category);
            }
        }
        if (CategoriesDisallowed is not null)
        {
            foreach (var category in CategoriesDisallowed)
            {
                redisValues.Add("-@" + category);
            }
        }
        if (KeysRule.HasValue)
        {
            redisValues.Add(KeysRule.ToString());
        }
        if (KeysAllowedPatterns is not null)
        {
            foreach (var pattern in KeysAllowedPatterns)
            {
                redisValues.Add("~" + pattern);
            }
        }
        if (KeysAllowedReadForPatterns is not null)
        {
            foreach (var pattern in KeysAllowedReadForPatterns)
            {
                redisValues.Add("%R~" + pattern);
            }
        }
        if (KeysAllowedWriteForPatterns is not null)
        {
            foreach (var pattern in KeysAllowedWriteForPatterns)
            {
                redisValues.Add("%W~" + pattern);
            }
        }
        if (PubSubRule.HasValue)
        {
            redisValues.Add(PubSubRule.ToString());
        }
        if (PubSubAllowChannels is not null)
        {
            foreach (var channel in PubSubAllowChannels)
            {
                redisValues.Add("&" + channel);
            }
        }
    }
}

/// <summary>
/// Represents the ACL selector rules.
/// </summary>
public class ACLSelectorRules
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ACLSelectorRules"/> class.
    /// </summary>
    /// <param name="commandsAllowed">The commands allowed.</param>
    /// <param name="commandsDisallowed">The commands disallowed.</param>
    /// <param name="categoriesAllowed">The categories allowed.</param>
    /// <param name="categoriesDisallowed">The categories disallowed.</param>
    /// <param name="keysAllowedPatterns">The keys allowed patterns.</param>
    /// <param name="keysAllowedReadForPatterns">The keys allowed read-for patterns.</param>
    /// <param name="keysAllowedWriteForPatterns">The keys allowed write-for patterns.</param>
    public ACLSelectorRules(
        string[]? commandsAllowed,
        string[]? commandsDisallowed,
        string[]? categoriesAllowed,
        string[]? categoriesDisallowed,
        string[]? keysAllowedPatterns,
        string[]? keysAllowedReadForPatterns,
        string[]? keysAllowedWriteForPatterns)
    {
        CommandsAllowed = commandsAllowed;
        CommandsDisallowed = commandsDisallowed;
        CategoriesAllowed = categoriesAllowed;
        CategoriesDisallowed = categoriesDisallowed;
        KeysAllowedPatterns = keysAllowedPatterns;
        KeysAllowedReadForPatterns = keysAllowedReadForPatterns;
        KeysAllowedWriteForPatterns = keysAllowedWriteForPatterns;
    }

    /// <summary>
    /// Gets the commands allowed.
    /// </summary>
    public readonly string[]? CommandsAllowed;

    /// <summary>
    /// Gets the commands disallowed.
    /// </summary>
    public readonly string[]? CommandsDisallowed;

    /// <summary>
    /// Gets the categories allowed.
    /// </summary>
    public readonly string[]? CategoriesAllowed;

    /// <summary>
    /// Gets the categories disallowed.
    /// </summary>
    public readonly string[]? CategoriesDisallowed;

    /// <summary>
    /// Gets the keys allowed patterns.
    /// </summary>
    public readonly string[]? KeysAllowedPatterns;

    /// <summary>
    /// Gets the keys allowed read-for patterns.
    /// </summary>
    public readonly string[]? KeysAllowedReadForPatterns;

    /// <summary>
    /// Gets the keys allowed write-for patterns.
    /// </summary>
    public readonly string[]? KeysAllowedWriteForPatterns;

    /// <summary>
    /// Appends the ACL selector rules to the specified list of Redis values.
    /// </summary>
    /// <param name="redisValues">The list of Redis values.</param>
    internal void AppendTo(List<RedisValue> redisValues)
    {
        redisValues.Add("(");
        if (CommandsAllowed is not null)
        {
            foreach (var command in CommandsAllowed)
            {
                redisValues.Add("+" + command);
            }
        }
        if (CommandsDisallowed is not null)
        {
            foreach (var command in CommandsDisallowed)
            {
                redisValues.Add("-" + command);
            }
        }
        if (CategoriesAllowed is not null)
        {
            foreach (var category in CategoriesAllowed)
            {
                redisValues.Add("+@" + category);
            }
        }
        if (CategoriesDisallowed is not null)
        {
            foreach (var category in CategoriesDisallowed)
            {
                redisValues.Add("-@" + category);
            }
        }
        if (KeysAllowedPatterns is not null)
        {
            foreach (var pattern in KeysAllowedPatterns)
            {
                redisValues.Add("~" + pattern);
            }
        }
        if (KeysAllowedReadForPatterns is not null)
        {
            foreach (var pattern in KeysAllowedReadForPatterns)
            {
                redisValues.Add("%R~" + pattern);
            }
        }
        if (KeysAllowedWriteForPatterns is not null)
        {
            foreach (var pattern in KeysAllowedWriteForPatterns)
            {
                redisValues.Add("%W~" + pattern);
            }
        }
        redisValues.Add(")");
    }
}

/// <summary>
/// Represents the state of an ACL user.
/// </summary>
public enum ACLUserState
{
    /// <summary>
    /// The user is on.
    /// </summary>
    ON,

    /// <summary>
    /// The user is off.
    /// </summary>
    OFF,
}

/// <summary>
/// Represents the ACL commands rule.
/// </summary>
public enum ACLCommandsRule
{
    /// <summary>
    /// All commands are allowed.
    /// </summary>
    ALLCOMMANDS,

    /// <summary>
    /// No commands are allowed.
    /// </summary>
    NOCOMMANDS,
}

/// <summary>
/// Represents the ACL keys rule.
/// </summary>
public enum ACLKeysRule
{
    /// <summary>
    /// All keys are allowed.
    /// </summary>
    ALLKEYS,

    /// <summary>
    /// Keys are reset.
    /// </summary>
    RESETKEYS,
}

/// <summary>
/// Represents the ACL pub/sub rule.
/// </summary>
public enum ACLPubSubRule
{
    /// <summary>
    /// All channels are allowed.
    /// </summary>
    ALLCHANNELS,

    /// <summary>
    /// Channels are reset.
    /// </summary>
    RESETCHANNELS,
}
