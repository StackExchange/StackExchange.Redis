using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

internal sealed partial class ProxiedDatabase
{
    // Async Script/Execute/Publish methods
    public Task<long> PublishAsync(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisResult> ExecuteAsync(string command, params object[] args) =>
        throw new NotImplementedException();

    public Task<RedisResult> ExecuteAsync(
        string command,
        ICollection<object>? args,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisResult> ScriptEvaluateAsync(
        string script,
        RedisKey[]? keys = null,
        RedisValue[]? values = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisResult> ScriptEvaluateAsync(
        byte[] hash,
        RedisKey[]? keys = null,
        RedisValue[]? values = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisResult> ScriptEvaluateAsync(
        LuaScript script,
        object? parameters = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisResult> ScriptEvaluateAsync(
        LoadedLuaScript script,
        object? parameters = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisResult> ScriptEvaluateReadOnlyAsync(
        string script,
        RedisKey[]? keys = null,
        RedisValue[]? values = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public Task<RedisResult> ScriptEvaluateReadOnlyAsync(
        byte[] hash,
        RedisKey[]? keys = null,
        RedisValue[]? values = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    // Synchronous Script/Execute/Publish methods
    public long Publish(RedisChannel channel, RedisValue message, CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisResult Execute(string command, params object[] args) =>
        throw new NotImplementedException();

    public RedisResult Execute(
        string command,
        ICollection<object>? args,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisResult ScriptEvaluate(
        string script,
        RedisKey[]? keys = null,
        RedisValue[]? values = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisResult ScriptEvaluate(
        byte[] hash,
        RedisKey[]? keys = null,
        RedisValue[]? values = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisResult ScriptEvaluate(
        LuaScript script,
        object? parameters = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisResult ScriptEvaluate(
        LoadedLuaScript script,
        object? parameters = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisResult ScriptEvaluateReadOnly(
        string script,
        RedisKey[]? keys = null,
        RedisValue[]? values = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();

    public RedisResult ScriptEvaluateReadOnly(
        byte[] hash,
        RedisKey[]? keys = null,
        RedisValue[]? values = null,
        CommandFlags flags = CommandFlags.None) =>
        throw new NotImplementedException();
}
