using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class AutoConfigure(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void ClientId_Integer_Success()
    {
        // CLIENT ID response
        var resp = ":11\r\n";
        var message = Message.Create(-1, default, RedisCommand.CLIENT);

        // Note: This will return false because we don't have a real connection with a server endpoint
        // The processor will throw because it can't set the connection ID without a real connection
        var success = TryExecute(resp, ResultProcessor.AutoConfigure, out bool result, out var exception, message);

        Assert.False(success);
        Assert.NotNull(exception);
        Assert.IsType<RedisConnectionException>(exception);
    }

    [Fact]
    public void Info_BulkString_Success()
    {
        // INFO response with replication info
        var info = "# Replication\r\n" +
                   "role:master\r\n" +
                   "connected_slaves:0\r\n" +
                   "master_failover_state:no-failover\r\n" +
                   "master_replid:8c3e3c3e3c3e3c3e3c3e3c3e3c3e3c3e3c3e3c3e\r\n" +
                   "master_replid2:0000000000000000000000000000000000000000\r\n" +
                   "master_repl_offset:0\r\n" +
                   "second_repl_offset:-1\r\n" +
                   "repl_backlog_active:0\r\n" +
                   "repl_backlog_size:1048576\r\n" +
                   "repl_backlog_first_byte_offset:0\r\n" +
                   "repl_backlog_histlen:0\r\n";

        var resp = $"${info.Length}\r\n{info}\r\n";
        var message = Message.Create(-1, default, RedisCommand.INFO);

        // Note: This will return false because we don't have a real connection with a server endpoint
        var success = TryExecute(resp, ResultProcessor.AutoConfigure, out bool result, out var exception, message);

        Assert.False(success);
        Assert.NotNull(exception);
        Assert.IsType<RedisConnectionException>(exception);
    }

    [Fact]
    public void Info_WithVersion_Success()
    {
        // INFO response with version info
        var info = "# Server\r\n" +
                   "redis_version:7.2.4\r\n" +
                   "redis_git_sha1:00000000\r\n" +
                   "redis_mode:standalone\r\n" +
                   "os:Linux 5.15.0-1-amd64 x86_64\r\n" +
                   "arch_bits:64\r\n";

        var resp = $"${info.Length}\r\n{info}\r\n";
        var message = Message.Create(-1, default, RedisCommand.INFO);

        var success = TryExecute(resp, ResultProcessor.AutoConfigure, out bool result, out var exception, message);

        Assert.False(success);
        Assert.NotNull(exception);
        Assert.IsType<RedisConnectionException>(exception);
    }

    [Fact]
    public void Info_EmptyString_Success()
    {
        // Empty INFO response
        var resp = "$0\r\n\r\n";
        var message = Message.Create(-1, default, RedisCommand.INFO);

        var success = TryExecute(resp, ResultProcessor.AutoConfigure, out bool result, out var exception, message);

        Assert.False(success);
        Assert.NotNull(exception);
        Assert.IsType<RedisConnectionException>(exception);
    }

    [Fact]
    public void Info_Null_Success()
    {
        // Null INFO response
        var resp = "$-1\r\n";
        var message = Message.Create(-1, default, RedisCommand.INFO);

        var success = TryExecute(resp, ResultProcessor.AutoConfigure, out bool result, out var exception, message);

        Assert.False(success);
        Assert.NotNull(exception);
        Assert.IsType<RedisConnectionException>(exception);
    }

    [Fact]
    public void Config_Array_Success()
    {
        // CONFIG GET timeout response
        var resp = "*2\r\n" +
                   "$7\r\ntimeout\r\n" +
                   "$3\r\n300\r\n";
        var message = Message.Create(-1, default, RedisCommand.CONFIG);

        var success = TryExecute(resp, ResultProcessor.AutoConfigure, out bool result, out var exception, message);

        Assert.False(success);
        Assert.NotNull(exception);
        Assert.IsType<RedisConnectionException>(exception);
    }

    [Fact]
    public void ReadonlyError_Success()
    {
        // READONLY error response
        var resp = "-READONLY You can't write against a read only replica.\r\n";
        var message = DummyMessage();

        var success = TryExecute(resp, ResultProcessor.AutoConfigure, out bool result, out var exception, message);

        // Should handle the error - returns RedisServerException for error responses
        Assert.False(success);
        Assert.NotNull(exception);
        Assert.IsType<RedisServerException>(exception);
    }
}
