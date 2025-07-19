using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[Collection(SharedConnectionFixture.Key)]
public class ACLTests : TestBase
{
    public ACLTests(ITestOutputHelper output) : base(output) { }

    protected override string GetConfiguration() => TestConfig.Current.PrimaryServerAndPort;

    [Fact]
    public void ToRedisValues_ShouldReturnCorrectValues_WhenAllFieldsAreSet()
    {
        // Arrange
        var aclUserRules = new ACLUserRules(
            resetUser: true,
            noPass: true,
            resetPass: false,
            userState: ACLUserState.ON,
            passwordsToSet: new[] { "password1", "password2" },
            passwordsToRemove: new[] { "password3" },
            hashedPasswordsToSet: new[] { "hashed1", "hashed2" },
            hashedPasswordsToRemove: new[] { "hashed3" },
            clearSelectors: true);

        var aclCommandRules = new ACLCommandRules(
            commandsRule: ACLCommandsRule.NOCOMMANDS,
            commandsAllowed: new[] { "GET", "SET" },
            commandsDisallowed: new[] { "DEL" },
            categoriesAllowed: new[] { "string", "list" },
            categoriesDisallowed: new[] { "set", "hash" },
            keysRule: ACLKeysRule.ALLKEYS,
            keysAllowedPatterns: new[] { "user:*", "session:*" },
            keysAllowedReadForPatterns: new[] { "user:*" },
            keysAllowedWriteForPatterns: new[] { "session:*" },
            pubSubRule: ACLPubSubRule.ALLCHANNELS,
            pubSubAllowChannels: new[] { "channel1", "channel2" });

        var aclSelectorRules = new[]
        {
            new ACLSelectorRules(
                commandsAllowed: new[] { "GET", },
                commandsDisallowed: new[] { "SET" },
                categoriesAllowed: new[] { "string" },
                categoriesDisallowed: new[] { "list" },
                keysAllowedPatterns: new[] { "user:*" },
                keysAllowedReadForPatterns: new[] { "session:*" },
                keysAllowedWriteForPatterns: new[] { "user:*" }),
        };

        var aclRules = new ACLRules(aclUserRules, aclCommandRules, aclSelectorRules);

        // Act
        var redisValues = aclRules.ToRedisValues();

        // Assert
        var expectedValues = new List<RedisValue>
            {
                RedisLiterals.RESET,
                RedisLiterals.NOPASS,
                "ON",
                ">password1",
                ">password2",
                "<password3",
                "#hashed1",
                "#hashed2",
                "!hashed3",
                RedisLiterals.CLEARSELECTORS,
                "NOCOMMANDS",
                "+GET",
                "+SET",
                "-DEL",
                "+@string",
                "+@list",
                "-@set",
                "-@hash",
                "ALLKEYS",
                "~user:*",
                "~session:*",
                "%R~user:*",
                "%W~session:*",
                "ALLCHANNELS",
                "&channel1",
                "&channel2",
                "(",
                "+GET",
                "-SET",
                "+@string",
                "-@list",
                "~user:*",
                "%R~session:*",
                "%W~user:*",
                ")",
            };

        Assert.Equal(expectedValues, redisValues);
    }

    [Fact]
    public void ToRedisValues_ShouldReturnEmpty_WhenAllNull()
    {
        // Arrange
        var aclRules = new ACLRules(null, null, null);

        // Act
        var redisValues = aclRules.ToRedisValues();

        // Assert
        Assert.Empty(redisValues);
    }

    [Fact]
    public void ToRedisValues_ShouldReturnEmpty_WhenNoFieldsAreSet()
    {
        // Arrange
        var aclUserRules = new ACLUserRules(
            resetUser: false,
            noPass: false,
            resetPass: false,
            userState: null,
            passwordsToSet: null,
            passwordsToRemove: null,
            hashedPasswordsToSet: null,
            hashedPasswordsToRemove: null,
            clearSelectors: false);

        var aclCommandRules = new ACLCommandRules(
            commandsRule: null,
            commandsAllowed: null,
            commandsDisallowed: null,
            categoriesAllowed: null,
            categoriesDisallowed: null,
            keysRule: null,
            keysAllowedPatterns: null,
            keysAllowedReadForPatterns: null,
            keysAllowedWriteForPatterns: null,
            pubSubRule: null,
            pubSubAllowChannels: null);

        var aclRules = new ACLRules(aclUserRules, aclCommandRules, new ACLSelectorRules[0]);

        // Act
        var redisValues = aclRules.ToRedisValues();

        // Assert
        Assert.Empty(redisValues);
    }

    [Fact]
    public void ToRedisValues_AllCommandsAllKeysAllChannels()
    {
        // Arrange
        var aclUserRules = new ACLUserRules(
            resetUser: false,
            noPass: false,
            resetPass: false,
            userState: null,
            passwordsToSet: null,
            passwordsToRemove: null,
            hashedPasswordsToSet: null,
            hashedPasswordsToRemove: null,
            clearSelectors: false);

        var aclCommandRules = new ACLCommandRules(
            commandsRule: ACLCommandsRule.ALLCOMMANDS,
            commandsAllowed: null,
            commandsDisallowed: null,
            categoriesAllowed: null,
            categoriesDisallowed: null,
            keysRule: ACLKeysRule.ALLKEYS,
            keysAllowedPatterns: null,
            keysAllowedReadForPatterns: null,
            keysAllowedWriteForPatterns: null,
            pubSubRule: ACLPubSubRule.ALLCHANNELS,
            pubSubAllowChannels: null);

        // Act
        var aclRules = new ACLRules(aclUserRules, aclCommandRules, null);
        var redisValues = aclRules.ToRedisValues();

        // Assert
        var expectedValues = new List<RedisValue>
            {
                "ALLCOMMANDS",
                "ALLKEYS",
                "ALLCHANNELS",
            };

        Assert.Equal(expectedValues, redisValues);
    }

    [Fact]
    public void ToRedisValues_NoCommandsNoKeysResetChannels()
    {
        // Arrange
        var aclUserRules = new ACLUserRules(
            resetUser: false,
            noPass: false,
            resetPass: false,
            userState: null,
            passwordsToSet: null,
            passwordsToRemove: null,
            hashedPasswordsToSet: null,
            hashedPasswordsToRemove: null,
            clearSelectors: false);

        var aclCommandRules = new ACLCommandRules(
            commandsRule: ACLCommandsRule.NOCOMMANDS,
            commandsAllowed: null,
            commandsDisallowed: null,
            categoriesAllowed: null,
            categoriesDisallowed: null,
            keysRule: ACLKeysRule.RESETKEYS,
            keysAllowedPatterns: null,
            keysAllowedReadForPatterns: null,
            keysAllowedWriteForPatterns: null,
            pubSubRule: ACLPubSubRule.RESETCHANNELS,
            pubSubAllowChannels: null);

        // Act
        var aclRules = new ACLRules(aclUserRules, aclCommandRules, null);
        var redisValues = aclRules.ToRedisValues();

        // Assert
        var expectedValues = new List<RedisValue>
            {
                "NOCOMMANDS",
                "RESETKEYS",
                "RESETCHANNELS",
            };

        Assert.Equal(expectedValues, redisValues);
    }

    [Fact]
    public void Build_ShouldCreateACLRulesWithUserRules_WhenUserRulesAreSet()
    {
        // Arrange
        var builder = new ACLRulesBuilder();

        builder.WithACLUserRules(userBuilder => userBuilder
            .ResetUser(true)
            .NoPass(true)
            .UserState(ACLUserState.ON)
            .PasswordsToSet("password123")
            .ClearSelectors(true));

        // Act
        var aclRules = builder.Build();

        // Assert
        Assert.NotNull(aclRules.AclUserRules);
        Assert.True(aclRules.AclUserRules?.ResetUser);
        Assert.True(aclRules.AclUserRules?.NoPass);
        Assert.Equal(ACLUserState.ON, aclRules.AclUserRules?.UserState);
        Assert.Contains(">password123", aclRules.ToRedisValues());
        Assert.Contains(RedisLiterals.CLEARSELECTORS, aclRules.ToRedisValues());
    }

    [Fact]
    public void Build_ShouldCreateACLRulesWithCommandRules_WhenCommandRulesAreSet()
    {
        // Arrange
        var builder = new ACLRulesBuilder();

        builder.WithACLCommandRules(commandBuilder => commandBuilder
            .CommandsRule(ACLCommandsRule.ALLCOMMANDS)
            .CommandsAllowed("GET", "SET")
            .CommandsDisallowed("DEL")
            .CategoriesAllowed("string")
            .KeysRule(ACLKeysRule.ALLKEYS)
            .KeysAllowedPatterns("user:*", "session:*"));

        // Act
        var aclRules = builder.Build();

        // Assert
        Assert.NotNull(aclRules.AclCommandRules);
        Assert.Equal(ACLCommandsRule.ALLCOMMANDS, aclRules.AclCommandRules?.CommandsRule);
        Assert.Contains("+GET", aclRules.ToRedisValues());
        Assert.Contains("-DEL", aclRules.ToRedisValues());
        Assert.Contains("+@string", aclRules.ToRedisValues());
        Assert.Contains("~user:*", aclRules.ToRedisValues());
    }

    [Fact]
    public void Build_ShouldCreateACLRulesWithSelectorRules_WhenSelectorRulesAreSet()
    {
        // Arrange
        var builder = new ACLRulesBuilder();

        builder.AppendACLSelectorRules(selectorBuilder => selectorBuilder
            .CommandsAllowed("GET")
            .CommandsDisallowed("SET")
            .CategoriesAllowed("list")
            .KeysAllowedPatterns("session:*"));

        // Act
        var aclRules = builder.Build();

        // Assert
        Assert.NotNull(aclRules.AclSelectorRules);
        Assert.Single(aclRules.AclSelectorRules);
        Assert.Contains("+GET", aclRules.ToRedisValues());
        Assert.Contains("-SET", aclRules.ToRedisValues());
        Assert.Contains("+@list", aclRules.ToRedisValues());
        Assert.Contains("~session:*", aclRules.ToRedisValues());
    }

    [Fact]
    public void Build_ShouldCreateACLRulesWithAllComponents_WhenAllRulesAreSet()
    {
        // Arrange
        var builder = new ACLRulesBuilder();

        builder.WithACLUserRules(userBuilder => userBuilder
            .ResetUser(true)
            .NoPass(true)
            .UserState(ACLUserState.OFF)
            .PasswordsToSet("newpassword")
            .ClearSelectors(true));

        builder.WithACLCommandRules(commandBuilder => commandBuilder
            .CommandsRule(ACLCommandsRule.NOCOMMANDS)
            .CommandsAllowed("GET", "SET")
            .CategoriesAllowed("list")
            .KeysRule(ACLKeysRule.RESETKEYS)
            .KeysAllowedPatterns("user:*"));

        builder.AppendACLSelectorRules(selectorBuilder => selectorBuilder
            .CommandsDisallowed("DEL")
            .CategoriesDisallowed("hash")
            .KeysAllowedPatterns("session:*"));

        // Act
        var aclRules = builder.Build();

        // Assert
        // Verify ACLUserRules
        Assert.True(aclRules.AclUserRules?.ResetUser);
        Assert.True(aclRules.AclUserRules?.NoPass);
        Assert.Equal(ACLUserState.OFF, aclRules.AclUserRules?.UserState);
        Assert.Contains(">newpassword", aclRules.ToRedisValues());
        Assert.Contains(RedisLiterals.CLEARSELECTORS, aclRules.ToRedisValues());

        // Verify ACLCommandRules
        Assert.Equal(ACLCommandsRule.NOCOMMANDS, aclRules.AclCommandRules?.CommandsRule);
        Assert.Contains("+GET", aclRules.ToRedisValues());
        Assert.Contains("+SET", aclRules.ToRedisValues());
        Assert.Contains("+@list", aclRules.ToRedisValues());
        Assert.Contains("~user:*", aclRules.ToRedisValues());

        // Verify ACLSelectorRules
        Assert.NotNull(aclRules.AclSelectorRules);
        Assert.Single(aclRules.AclSelectorRules);
        Assert.Contains("-DEL", aclRules.ToRedisValues());
        Assert.Contains("-@hash", aclRules.ToRedisValues());
        Assert.Contains("~session:*", aclRules.ToRedisValues());
    }

    [Fact]
    public void Build_ShouldHandleEmptyInput_WhenNoRulesAreSet()
    {
        // Arrange
        var builder = new ACLRulesBuilder();

        // Act
        var aclRules = builder.Build();

        // Assert
        Assert.Null(aclRules.AclUserRules);
        Assert.Null(aclRules.AclCommandRules);
        Assert.Null(aclRules.AclSelectorRules);
        Assert.Empty(aclRules.ToRedisValues());
    }

    [Fact]
    public void Build_ShouldCreateACLRulesWithMultipleSelectorRules_WhenMultipleAreAppended()
    {
        // Arrange
        var builder = new ACLRulesBuilder();

        builder.AppendACLSelectorRules(selectorBuilder => selectorBuilder
            .CommandsAllowed("GET")
            .CategoriesAllowed("string"));

        builder.AppendACLSelectorRules(selectorBuilder => selectorBuilder
            .CommandsDisallowed("SET")
            .CategoriesDisallowed("list"));

        // Act
        var aclRules = builder.Build();

        // Assert
        Assert.NotNull(aclRules.AclSelectorRules);
        Assert.Equal(2, aclRules.AclSelectorRules.Length);

        Assert.Contains("+GET", aclRules.ToRedisValues());
        Assert.Contains("+@string", aclRules.ToRedisValues());

        Assert.Contains("-SET", aclRules.ToRedisValues());
        Assert.Contains("-@list", aclRules.ToRedisValues());
    }
}
