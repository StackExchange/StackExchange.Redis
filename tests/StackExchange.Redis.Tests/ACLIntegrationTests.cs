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
        _conn = Create(require: RedisFeatures.v7_4_0_rc1);
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
        var userName = new RedisValue(Me());
        _redisServer.AccessControlSetUser(userName, new ACLRulesBuilder()
                        .AppendACLSelectorRules(rules => rules.CommandsAllowed("GET", "SET"))
                        .AppendACLSelectorRules(rules => rules.KeysAllowedReadForPatterns("key*"))
                        .WithACLUserRules(rules => rules.PasswordsToSet("psw1", "psw2"))
                        .WithACLCommandRules(rules => rules.CommandsAllowed("HGET", "HSET")
                                                            .KeysAllowedPatterns("key1", "key*")
                                                            .PubSubAllowChannels("chan1", "chan*"))
                        .Build());

        // Act
        var user = _redisServer.AccessControlGetUser(userName);

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
        var user = _redisServer.AccessControlGetUser(Me());

        // Assert
        Assert.Null(user);
    }

    [Fact]
    public void AccessControlDeleteUsers_ShouldReturnCorrectCount()
    {
        // Arrange
        string userName = Me();
        _redisServer.AccessControlSetUser(new RedisValue(userName), new ACLRulesBuilder().Build());

        // Act
        var count = _redisServer.AccessControlDeleteUsers(new RedisValue[] { userName, "user2" });

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
        string userName = Me();
        _redisServer.AccessControlSetUser(
            userName,
            new ACLRulesBuilder()
                .WithACLUserRules(rules => rules.PasswordsToSet(new[] { "pass1" })
                                                 .UserState(ACLUserState.ON))
                .Build());

        Assert.Throws<RedisServerException>(() => _conn.GetDatabase().Execute("AUTH", userName, "pass2"));

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
        // Act
        var user = _redisServer.AccessControlWhoAmI();

        // Assert
        Assert.True(user.HasValue);
        Assert.True(user.ToString().Length > 0); // Ensure there's a valid user returned
    }

    [Fact]
    public void AccessControlList_ShouldReturnAllUsers()
    {
        // Arrange
        var userName1 = new RedisValue(Me() + "1");
        var userName2 = new RedisValue(Me() + "2");
        _redisServer.AccessControlSetUser(userName1, new ACLRulesBuilder().Build());
        _redisServer.AccessControlSetUser(userName2, new ACLRulesBuilder().Build());

        // Act
        var users = _redisServer.AccessControlList();

        // Assert
        Assert.NotNull(users);
        Assert.Contains(users, user => user.ToString().Contains(userName1!));
        Assert.Contains(users, user => user.ToString().Contains(userName2!));
    }

    [Fact]
    public void AccessControlSetUser_ShouldSetUserWithGivenRules()
    {
        string userName = Me();

        // Act
        _redisServer.AccessControlSetUser(new RedisValue(userName), new ACLRules(null, null, null));

        // Assert
        var users = _redisServer.AccessControlList();
        Assert.NotNull(users);
        Assert.Contains(users!, user => user.ToString().Contains(userName));
    }

    [Fact]
    public void AccessControlSetUser_ShouldSetUserWithMultipleRules()
    {
        // Arrange
        var userName = new RedisValue(Me());
        var rules = new ACLRulesBuilder()
                        .AppendACLSelectorRules(r => r.CommandsAllowed("HMGET", "HMSET").KeysAllowedReadForPatterns("key*"))
                        .WithACLUserRules(r => r.PasswordsToSet("password1", "password2"))
                        .WithACLCommandRules(r => r.CommandsAllowed("HGET", "HSET")
                                                    .KeysAllowedPatterns("key1", "key*")
                                                    .PubSubAllowChannels("chan1", "chan*"))
                        .Build();

        // Act
        _redisServer.AccessControlSetUser(userName, rules);

        // Assert
        var user = _redisServer.AccessControlGetUser(userName);
        Assert.NotNull(user);
        Assert.Contains(user.Selectors!, s => s.Commands!.Contains("hmget") && s.Commands!.Contains("hmset"));
        Assert.Contains(user.Selectors!, s => s.Keys!.Contains("key*"));
    }

    [Fact]
    public void AccessControlSetUser_ShouldUpdateExistingUser()
    {
        // Arrange
        var userName = new RedisValue(Me());
        var hashedPassword1 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var hashedPassword2 = "1123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var updatedPassword = "2123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var initialRules = new ACLRulesBuilder()
                            .WithACLUserRules(r => r.HashedPasswordsToSet(hashedPassword1, hashedPassword2))
                            .Build();
        _redisServer.AccessControlSetUser(userName, initialRules);

        var updatedRules = new ACLRulesBuilder()
                            .WithACLUserRules(r => r.HashedPasswordsToSet(updatedPassword).HashedPasswordsToRemove(hashedPassword1))
                            .Build();

        // Act
        _redisServer.AccessControlSetUser(userName, updatedRules);

        // Assert
        var user = _redisServer.AccessControlGetUser(userName);
        Assert.NotNull(user);
        Assert.DoesNotContain(hashedPassword1, user.Passwords!);
        Assert.Contains(hashedPassword2, user.Passwords!);
        Assert.Contains(updatedPassword, user.Passwords!);
    }
}
