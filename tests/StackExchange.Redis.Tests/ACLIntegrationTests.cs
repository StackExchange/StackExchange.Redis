using System;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
[Collection(SharedConnectionFixture.Key)]
public class ACLIntegrationTests : TestBase
{
    private readonly IConnectionMultiplexer _conn;
    private readonly IServer _redisServer;

    public ACLIntegrationTests(ITestOutputHelper output, SharedConnectionFixture fixture) : base(output, fixture)
    {
        _conn = Create();
        _redisServer = GetAnyPrimary(_conn);
    }

    [Fact]
    public void AccessControlGetCategories_ShouldReturnCategories()
    {
        // Act
        var categories = _redisServer.AccessControlGetCategories();

        // Assert
        Assert.NotNull(categories);
        Assert.Contains("write", categories);
        Assert.Contains("set", categories);
        Assert.Contains("list", categories);
    }

    [Fact]
    public void AccessControlGetCommands_ShouldReturnCommands()
    {
        // Act
        var commands = _redisServer.AccessControlGetCommands("set");

        // Assert
        Assert.NotNull(commands);
        Assert.Contains("sort", commands);
        Assert.Contains("spop", commands);
    }

    [Fact]
    public void AccessControlGetUser_ShouldReturnUserDetails()
    {
        Action<ACLSelectorRulesBuilder> act = rules => rules.CommandsAllowed("GET", "SET");
        // Arrange
        var username = new RedisValue("testuser");
        _redisServer.AccessControlSetUser("testuser", new ACLRulesBuilder()
                        .AppendACLSelectorRules(rules => rules.CommandsAllowed("GET", "SET"))
                        .AppendACLSelectorRules(rules => rules.KeysAllowedReadForPatterns("key*"))
                        .WithACLUserRules(rules => rules.PasswordsToSet("psw1", "psw2"))
                        .WithACLCommandRules(rules => rules.CommandsAllowed("HGET", "HSET")
                                                            .KeysAllowedPatterns("key1", "key*")
                                                            .PubSubAllowChannels("chan1", "chan*"))
                        .Build());

        // Act
        var user = _redisServer.AccessControlGetUser(username);

        // Assert
        Assert.NotNull(user);
        Assert.NotNull(user.Passwords);
        Assert.True(user.Passwords.Length > 1);
        Assert.NotNull(user.Selectors);
        Assert.True(user.Selectors.Length > 1);
    }

    [Fact]
    public void AccessControlGetUser_ShouldReturnNullForNonExistentUser()
    {
        // Act
        var user = _redisServer.AccessControlGetUser("nonexistentuser");

        // Assert
        Assert.Null(user);
    }

    [Fact]
    public void AccessControlDeleteUsers_ShouldReturnCorrectCount()
    {
        // Arrange
        _redisServer.AccessControlSetUser(new RedisValue("user1"), new ACLRulesBuilder().Build());

        // Act
        var count = _redisServer.AccessControlDeleteUsers(new RedisValue[] { "user1", "user2" });

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public void AccessControlGeneratePassword_ShouldReturnGeneratedPassword()
    {
        // Act
        var password = _redisServer.AccessControlGeneratePassword(256);

        // Assert
        Assert.True(password.HasValue);
        Assert.True(password.ToString().Length > 0); // Ensure a password is generated
    }

    [Fact]
    public void AccessControlLogReset_ShouldExecuteSuccessfully()
    {
        // Act
        _redisServer.AccessControlLogReset();

        // Assert
        // The action is successful if no exceptions are thrown
    }

    [Fact]
    public void AccessControlLog_ShouldReturnLogs()
    {
        // Arrange
        _redisServer.AccessControlSetUser(
            "user1",
            new ACLRulesBuilder()
                .WithACLUserRules(rules => rules.PasswordsToSet(new[] { "pass1" })
                                                 .UserState(ACLUserState.ON))
                .Build());

        Assert.Throws<RedisServerException>(() => _conn.GetDatabase().Execute("AUTH", "user1", "pass2"));

        // Act
        var logs = _redisServer.AccessControlLog(10);

        // Assert
        Assert.NotNull(logs);
        Assert.NotEmpty(logs);
        Assert.Contains(logs[0], x => x.Key == "reason");
    }

    [Fact]
    public void AccessControlWhoAmI_ShouldReturnCurrentUser()
    {
        // // Arrange
        // var conn = Create(require: RedisFeatures.v7_0_0_rc1);
        // var redisServer = (RedisServer)GetAnyPrimary(conn);

        // redisServer.AccessControlSetUser(
        //     "user1",
        //     new ACLRulesBuilder()
        //         .WithACLUserRules(rules => rules.PasswordsToSet(new[] { "pass1" })
        //                                          .UserState(UserState.ON))
        //         .Build());

        // Act
        var user = _redisServer.AccessControlWhoAmI();

        // Assert
        Assert.True(user.HasValue);
        Assert.True(user.ToString().Length > 0); // Ensure there's a valid user returned
    }

    [Fact]
    public void AccessControlSetUser_ShouldSetUserWithGivenRules()
    {
        // Act
        _redisServer.AccessControlSetUser(new RedisValue("testuser"), new ACLRules(null, null, null));

        // Assert
        // In this case, we're asserting that no exceptions are thrown and the user is successfully set.
        // To validate this, you might want to verify if the user exists in your Redis instance or use a similar check.
    }
}
