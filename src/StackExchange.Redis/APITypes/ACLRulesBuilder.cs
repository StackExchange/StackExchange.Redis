using System;
using System.Collections.Generic;
using System.Linq;

namespace StackExchange.Redis;

/// <summary>
/// Main Builder for ACLRules.
/// </summary>
public class ACLRulesBuilder
{
    private ACLUserRulesBuilder? _aCLUserRulesBuilder;
    private ACLCommandRulesBuilder? _aCLCommandRulesBuilder;
    private List<ACLSelectorRulesBuilder>? _aCLSelectorRulesBuilderList;

    /// <summary>
    /// Adds ACL user rules.
    /// </summary>
    /// <param name="buildAction">The action to build ACL user rules.</param>
    /// <returns>The current instance of <see cref="ACLRulesBuilder"/>.</returns>
    public ACLRulesBuilder WithACLUserRules(Action<ACLUserRulesBuilder> buildAction)
    {
        buildAction(_aCLUserRulesBuilder ??= new ACLUserRulesBuilder());
        return this;
    }

    /// <summary>
    /// Adds ACL command rules.
    /// </summary>
    /// <param name="buildAction">The action to build ACL command rules.</param>
    /// <returns>The current instance of <see cref="ACLRulesBuilder"/>.</returns>
    public ACLRulesBuilder WithACLCommandRules(Action<ACLCommandRulesBuilder> buildAction)
    {
        buildAction(_aCLCommandRulesBuilder ??= new ACLCommandRulesBuilder());
        return this;
    }

    /// <summary>
    /// Appends ACL selector rules.
    /// </summary>
    /// <param name="buildAction">The action to build ACL selector rules.</param>
    /// <returns>The current instance of <see cref="ACLRulesBuilder"/>.</returns>
    public ACLRulesBuilder AppendACLSelectorRules(Action<ACLSelectorRulesBuilder> buildAction)
    {
        _aCLSelectorRulesBuilderList ??= new List<ACLSelectorRulesBuilder>();
        var newSelectorRule = new ACLSelectorRulesBuilder();
        buildAction(newSelectorRule);
        _aCLSelectorRulesBuilderList.Add(newSelectorRule);
        return this;
    }

    /// <summary>
    /// Builds the ACL rules.
    /// </summary>
    /// <returns>The built <see cref="ACLRules"/>.</returns>
    public ACLRules Build()
    {
        return new ACLRules(
            _aCLUserRulesBuilder?.Build(),
            _aCLCommandRulesBuilder?.Build(),
            _aCLSelectorRulesBuilderList?.Select(item => item.Build()).ToArray());
    }
}

/// <summary>
/// Builder for ACLUserRules.
/// </summary>
public class ACLUserRulesBuilder
{
    private bool _resetUser = false;
    private bool _noPass = false;
    private bool _resetPass = false;
    private ACLUserState? _userState;
    private string[]? _passwordsToSet;
    private string[]? _passwordsToRemove;
    private string[]? _hashedPasswordsToSet;
    private string[]? _hashedPasswordsToRemove;
    private bool _clearSelectors = false;

    /// <summary>
    /// Resets the user.
    /// </summary>
    /// <param name="resetUser">If set to <c>true</c>, resets the user.</param>
    /// <returns>The current instance of <see cref="ACLUserRulesBuilder"/>.</returns>
    public ACLUserRulesBuilder ResetUser(bool resetUser)
    {
        _resetUser = resetUser;
        return this;
    }

    /// <summary>
    /// Sets the no pass flag.
    /// </summary>
    /// <param name="noPass">If set to <c>true</c>, sets the no pass flag.</param>
    /// <returns>The current instance of <see cref="ACLUserRulesBuilder"/>.</returns>
    public ACLUserRulesBuilder NoPass(bool noPass)
    {
        _noPass = noPass;
        return this;
    }

    /// <summary>
    /// Resets the password.
    /// </summary>
    /// <param name="resetPass">If set to <c>true</c>, resets the password.</param>
    /// <returns>The current instance of <see cref="ACLUserRulesBuilder"/>.</returns>
    public ACLUserRulesBuilder ResetPass(bool resetPass)
    {
        _resetPass = resetPass;
        return this;
    }

    /// <summary>
    /// Sets the user state.
    /// </summary>
    /// <param name="userState">The user state.</param>
    /// <returns>The current instance of <see cref="ACLUserRulesBuilder"/>.</returns>
    public ACLUserRulesBuilder UserState(ACLUserState? userState)
    {
        _userState = userState;
        return this;
    }

    /// <summary>
    /// Sets the passwords to set.
    /// </summary>
    /// <param name="passwords">The passwords to set.</param>
    /// <returns>The current instance of <see cref="ACLUserRulesBuilder"/>.</returns>
    public ACLUserRulesBuilder PasswordsToSet(params string[] passwords)
    {
        _passwordsToSet = passwords;
        return this;
    }

    /// <summary>
    /// Sets the passwords to remove.
    /// </summary>
    /// <param name="passwords">The passwords to remove.</param>
    /// <returns>The current instance of <see cref="ACLUserRulesBuilder"/>.</returns>
    public ACLUserRulesBuilder PasswordsToRemove(params string[] passwords)
    {
        _passwordsToRemove = passwords;
        return this;
    }

    /// <summary>
    /// Sets the hashed passwords to set.
    /// </summary>
    /// <param name="hashedPasswords">The hashed passwords to set.</param>
    /// <returns>The current instance of <see cref="ACLUserRulesBuilder"/>.</returns>
    public ACLUserRulesBuilder HashedPasswordsToSet(params string[] hashedPasswords)
    {
        _hashedPasswordsToSet = hashedPasswords;
        return this;
    }

    /// <summary>
    /// Sets the hashed passwords to remove.
    /// </summary>
    /// <param name="hashedPasswords">The hashed passwords to remove.</param>
    /// <returns>The current instance of <see cref="ACLUserRulesBuilder"/>.</returns>
    public ACLUserRulesBuilder HashedPasswordsToRemove(params string[] hashedPasswords)
    {
        _hashedPasswordsToRemove = hashedPasswords;
        return this;
    }

    /// <summary>
    /// Clears the selectors.
    /// </summary>
    /// <param name="clearSelectors">If set to <c>true</c>, clears the selectors.</param>
    /// <returns>The current instance of <see cref="ACLUserRulesBuilder"/>.</returns>
    public ACLUserRulesBuilder ClearSelectors(bool clearSelectors)
    {
        _clearSelectors = clearSelectors;
        return this;
    }

    /// <summary>
    /// Builds the ACL user rules.
    /// </summary>
    /// <returns>The built <see cref="ACLUserRules"/>.</returns>
    public ACLUserRules Build()
    {
        return new ACLUserRules(
            _resetUser,
            _noPass,
            _resetPass,
            _userState,
            _passwordsToSet,
            _passwordsToRemove,
            _hashedPasswordsToSet,
            _hashedPasswordsToRemove,
            _clearSelectors);
    }
}

/// <summary>
/// Builder for ACLCommandRules.
/// </summary>
public class ACLCommandRulesBuilder
{
    private ACLCommandsRule? _commandsRule;
    private string[]? _commandsAllowed;
    private string[]? _commandsDisallowed;
    private string[]? _categoriesAllowed;
    private string[]? _categoriesDisallowed;
    private ACLKeysRule? _keysRule;
    private string[]? _keysAllowedPatterns;
    private string[]? _keysAllowedReadForPatterns;
    private string[]? _keysAllowedWriteForPatterns;
    private ACLPubSubRule? _pubSubRule;
    private string[]? _pubSubAllowChannels;

    /// <summary>
    /// Sets the commands rule.
    /// </summary>
    /// <param name="commandsRule">The commands rule.</param>
    /// <returns>The current instance of <see cref="ACLCommandRulesBuilder"/>.</returns>
    public ACLCommandRulesBuilder CommandsRule(ACLCommandsRule? commandsRule)
    {
        _commandsRule = commandsRule;
        return this;
    }

    /// <summary>
    /// Sets the commands allowed.
    /// </summary>
    /// <param name="commands">The commands allowed.</param>
    /// <returns>The current instance of <see cref="ACLCommandRulesBuilder"/>.</returns>
    public ACLCommandRulesBuilder CommandsAllowed(params string[] commands)
    {
        _commandsAllowed = commands;
        return this;
    }

    /// <summary>
    /// Sets the commands disallowed.
    /// </summary>
    /// <param name="commands">The commands disallowed.</param>
    /// <returns>The current instance of <see cref="ACLCommandRulesBuilder"/>.</returns>
    public ACLCommandRulesBuilder CommandsDisallowed(params string[] commands)
    {
        _commandsDisallowed = commands;
        return this;
    }

    /// <summary>
    /// Sets the categories allowed.
    /// </summary>
    /// <param name="categories">The categories allowed.</param>
    /// <returns>The current instance of <see cref="ACLCommandRulesBuilder"/>.</returns>
    public ACLCommandRulesBuilder CategoriesAllowed(params string[] categories)
    {
        _categoriesAllowed = categories;
        return this;
    }

    /// <summary>
    /// Sets the categories disallowed.
    /// </summary>
    /// <param name="categories">The categories disallowed.</param>
    /// <returns>The current instance of <see cref="ACLCommandRulesBuilder"/>.</returns>
    public ACLCommandRulesBuilder CategoriesDisallowed(params string[] categories)
    {
        _categoriesDisallowed = categories;
        return this;
    }

    /// <summary>
    /// Sets the keys rule.
    /// </summary>
    /// <param name="keysRule">The keys rule.</param>
    /// <returns>The current instance of <see cref="ACLCommandRulesBuilder"/>.</returns>
    public ACLCommandRulesBuilder KeysRule(ACLKeysRule? keysRule)
    {
        _keysRule = keysRule;
        return this;
    }

    /// <summary>
    /// Sets the keys allowed patterns.
    /// </summary>
    /// <param name="patterns">The keys allowed patterns.</param>
    /// <returns>The current instance of <see cref="ACLCommandRulesBuilder"/>.</returns>
    public ACLCommandRulesBuilder KeysAllowedPatterns(params string[] patterns)
    {
        _keysAllowedPatterns = patterns;
        return this;
    }

    /// <summary>
    /// Sets the keys allowed read for patterns.
    /// </summary>
    /// <param name="patterns">The keys allowed read for patterns.</param>
    /// <returns>The current instance of <see cref="ACLCommandRulesBuilder"/>.</returns>
    public ACLCommandRulesBuilder KeysAllowedReadForPatterns(params string[] patterns)
    {
        _keysAllowedReadForPatterns = patterns;
        return this;
    }

    /// <summary>
    /// Sets the keys allowed write for patterns.
    /// </summary>
    /// <param name="patterns">The keys allowed write for patterns.</param>
    /// <returns>The current instance of <see cref="ACLCommandRulesBuilder"/>.</returns>
    public ACLCommandRulesBuilder KeysAllowedWriteForPatterns(params string[] patterns)
    {
        _keysAllowedWriteForPatterns = patterns;
        return this;
    }

    /// <summary>
    /// Sets the pub/sub rule.
    /// </summary>
    /// <param name="pubSubRule">The pub/sub rule.</param>
    /// <returns>The current instance of <see cref="ACLCommandRulesBuilder"/>.</returns>
    public ACLCommandRulesBuilder PubSubRule(ACLPubSubRule? pubSubRule)
    {
        _pubSubRule = pubSubRule;
        return this;
    }

    /// <summary>
    /// Sets the pub/sub allow channels.
    /// </summary>
    /// <param name="channels">The pub/sub allow channels.</param>
    /// <returns>The current instance of <see cref="ACLCommandRulesBuilder"/>.</returns>
    public ACLCommandRulesBuilder PubSubAllowChannels(params string[] channels)
    {
        _pubSubAllowChannels = channels;
        return this;
    }

    /// <summary>
    /// Builds the ACL command rules.
    /// </summary>
    /// <returns>The built <see cref="ACLCommandRules"/>.</returns>
    public ACLCommandRules Build()
    {
        return new ACLCommandRules(
            _commandsRule,
            _commandsAllowed,
            _commandsDisallowed,
            _categoriesAllowed,
            _categoriesDisallowed,
            _keysRule,
            _keysAllowedPatterns,
            _keysAllowedReadForPatterns,
            _keysAllowedWriteForPatterns,
            _pubSubRule,
            _pubSubAllowChannels);
    }
}

/// <summary>
/// Builder for ACLSelectorRules.
/// </summary>
public class ACLSelectorRulesBuilder
{
    private string[]? _commandsAllowed;
    private string[]? _commandsDisallowed;
    private string[]? _categoriesAllowed;
    private string[]? _categoriesDisallowed;
    private string[]? _keysAllowedPatterns;
    private string[]? _keysAllowedReadForPatterns;
    private string[]? _keysAllowedWriteForPatterns;

    /// <summary>
    /// Sets the commands allowed.
    /// </summary>
    /// <param name="commands">The commands allowed.</param>
    /// <returns>The current instance of <see cref="ACLSelectorRulesBuilder"/>.</returns>
    public ACLSelectorRulesBuilder CommandsAllowed(params string[] commands)
    {
        _commandsAllowed = commands;
        return this;
    }

    /// <summary>
    /// Sets the commands disallowed.
    /// </summary>
    /// <param name="commands">The commands disallowed.</param>
    /// <returns>The current instance of <see cref="ACLSelectorRulesBuilder"/>.</returns>
    public ACLSelectorRulesBuilder CommandsDisallowed(params string[] commands)
    {
        _commandsDisallowed = commands;
        return this;
    }

    /// <summary>
    /// Sets the categories allowed.
    /// </summary>
    /// <param name="categories">The categories allowed.</param>
    /// <returns>The current instance of <see cref="ACLSelectorRulesBuilder"/>.</returns>
    public ACLSelectorRulesBuilder CategoriesAllowed(params string[] categories)
    {
        _categoriesAllowed = categories;
        return this;
    }

    /// <summary>
    /// Sets the categories disallowed.
    /// </summary>
    /// <param name="categories">The categories disallowed.</param>
    /// <returns>The current instance of <see cref="ACLSelectorRulesBuilder"/>.</returns>
    public ACLSelectorRulesBuilder CategoriesDisallowed(params string[] categories)
    {
        _categoriesDisallowed = categories;
        return this;
    }

    /// <summary>
    /// Sets the keys allowed patterns.
    /// </summary>
    /// <param name="patterns">The keys allowed patterns.</param>
    /// <returns>The current instance of <see cref="ACLSelectorRulesBuilder"/>.</returns>
    public ACLSelectorRulesBuilder KeysAllowedPatterns(params string[] patterns)
    {
        _keysAllowedPatterns = patterns;
        return this;
    }

    /// <summary>
    /// Sets the keys allowed read for patterns.
    /// </summary>
    /// <param name="patterns">The keys allowed read for patterns.</param>
    /// <returns>The current instance of <see cref="ACLSelectorRulesBuilder"/>.</returns>
    public ACLSelectorRulesBuilder KeysAllowedReadForPatterns(params string[] patterns)
    {
        _keysAllowedReadForPatterns = patterns;
        return this;
    }

    /// <summary>
    /// Sets the keys allowed write for patterns.
    /// </summary>
    /// <param name="patterns">The keys allowed write for patterns.</param>
    /// <returns>The current instance of <see cref="ACLSelectorRulesBuilder"/>.</returns>
    public ACLSelectorRulesBuilder KeysAllowedWriteForPatterns(params string[] patterns)
    {
        _keysAllowedWriteForPatterns = patterns;
        return this;
    }

    /// <summary>
    /// Builds the ACL selector rules.
    /// </summary>
    /// <returns>The built <see cref="ACLSelectorRules"/>.</returns>
    public ACLSelectorRules Build()
    {
        return new ACLSelectorRules(
            _commandsAllowed,
            _commandsDisallowed,
            _categoriesAllowed,
            _categoriesDisallowed,
            _keysAllowedPatterns,
            _keysAllowedReadForPatterns,
            _keysAllowedWriteForPatterns);
    }
}
