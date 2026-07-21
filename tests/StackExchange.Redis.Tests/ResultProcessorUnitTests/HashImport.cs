using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Tests for the internal processors that back <see cref="IDatabase.HashImport"/>: the per-step processor that
/// captures a failing PREPARE/SET into the parent, and the terminal processor that surfaces that error (or success).
/// </summary>
public class HashImport(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    private static RedisDatabase.HashImportMessage NewParent()
        => new(0, CommandFlags.None, default, default);

    [Fact]
    public void Step_Ok_RecordsNoError()
    {
        var parent = NewParent();
        var step = new RedisDatabase.HashImportStepProcessor(parent);
        Execute("+OK\r\n", step, message: DummyMessage());
        Assert.Null(parent.StepError);
    }

    [Fact]
    public void Step_Error_IsCaptured()
    {
        var parent = NewParent();
        var step = new RedisDatabase.HashImportStepProcessor(parent);
        Execute("-ERR duplicate field name in fieldset\r\n", step, message: DummyMessage());
        Assert.Equal("ERR duplicate field name in fieldset", parent.StepError);
    }

    [Fact]
    public void Step_FirstErrorWins()
    {
        var parent = NewParent();
        var step = new RedisDatabase.HashImportStepProcessor(parent);
        Execute("-ERR first\r\n", step, message: DummyMessage());
        Execute("-ERR second\r\n", step, message: DummyMessage());
        Assert.Equal("ERR first", parent.StepError);
    }

    [Fact]
    public void Terminal_Success()
    {
        var parent = NewParent();
        // DISCARD reply is an integer; the value is irrelevant, the operation is a success
        var result = Execute(":1\r\n", RedisDatabase.HashImportProcessor.Default, message: parent);
        Assert.True(result);
    }

    [Fact]
    public void Terminal_SurfacesCapturedStepError()
    {
        var parent = NewParent();
        parent.RecordStepError("ERR value count does not match fieldset field count");

        Assert.False(TryExecute<bool>(":1\r\n", RedisDatabase.HashImportProcessor.Default, out _, out var ex, message: parent));
        var server = Assert.IsType<RedisServerException>(ex);
        Assert.Equal("ERR value count does not match fieldset field count", server.Message);
    }

    [Fact]
    public void Terminal_SurfacesTerminalError()
    {
        var parent = NewParent();
        // the DISCARD itself erroring is surfaced by the common error path
        Assert.False(TryExecute<bool>("-ERR boom\r\n", RedisDatabase.HashImportProcessor.Default, out _, out var ex, message: parent));
        Assert.IsType<RedisServerException>(ex);
    }
}
