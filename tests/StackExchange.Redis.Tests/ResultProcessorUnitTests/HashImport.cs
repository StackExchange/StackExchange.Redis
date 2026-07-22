using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Tests for the internal processors that back <see cref="IDatabase.HashImport"/>: the per-step processor that
/// captures a failing SET (as a per-entry failure) or PREPARE (as a setup error) into the parent, and the terminal
/// processor that surfaces those (returning the failures array, or throwing on setup error).
/// </summary>
public class HashImport(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    private static RedisDatabase.HashImportMessage NewParent()
        => new(0, CommandFlags.None, default, default);

    private static RedisDatabase.HashImportSetMessage NewSet(int index, RedisKey key)
        => new(0, CommandFlags.None, default, key, index, default);

    [Fact]
    public void Step_Ok_RecordsNothing()
    {
        var parent = NewParent();
        var step = new RedisDatabase.HashImportStepProcessor(parent);
        Execute("+OK\r\n", step, message: NewSet(0, "k"));
        Assert.Null(parent.SetupError);
        Assert.Empty(parent.BuildResult());
    }

    [Fact]
    public void Step_SetError_IsCapturedAsFailureWithIndexAndKey()
    {
        var parent = NewParent();
        var step = new RedisDatabase.HashImportStepProcessor(parent);
        Execute("-WRONGTYPE Operation against a key holding the wrong kind of value\r\n", step, message: NewSet(3, "user:3"));

        var failure = Assert.Single(parent.BuildResult());
        Assert.Equal(3, failure.Index);
        Assert.Equal("user:3", failure.Key);
        Assert.StartsWith("WRONGTYPE", failure.Message);
        Assert.Null(parent.SetupError); // a SET failure is not a setup error
    }

    [Fact]
    public void Step_PrepareError_IsCapturedAsSetupError()
    {
        var parent = NewParent();
        var step = new RedisDatabase.HashImportStepProcessor(parent);
        // a non-SET message (e.g. PREPARE) failing is a setup error
        Execute("-ERR duplicate field name in fieldset\r\n", step, message: DummyMessage());
        Assert.Equal("ERR duplicate field name in fieldset", parent.SetupError);
        Assert.Empty(parent.BuildResult());
    }

    [Fact]
    public void Terminal_Success_NoFailures()
    {
        var parent = NewParent();
        var result = Execute(":1\r\n", RedisDatabase.HashImportProcessor.Default, message: parent);
        Assert.Empty(result!);
    }

    [Fact]
    public void Terminal_ReturnsCollectedFailures()
    {
        var parent = NewParent();
        parent.RecordSetFailure(1, "k1", "WRONGTYPE nope");
        parent.RecordSetFailure(4, "k4", "WRONGTYPE also nope");

        var result = Execute(":1\r\n", RedisDatabase.HashImportProcessor.Default, message: parent)!;
        Assert.Equal(2, result.Length);
        Assert.Equal(1, result[0].Index);
        Assert.Equal(4, result[1].Index);
    }

    [Fact]
    public void Terminal_SetupError_Throws()
    {
        var parent = NewParent();
        parent.RecordSetupError("ERR duplicate field name in fieldset");

        Assert.False(TryExecute<HashImportFailure[]>(":1\r\n", RedisDatabase.HashImportProcessor.Default, out _, out var ex, message: parent));
        var server = Assert.IsType<RedisServerException>(ex);
        Assert.Equal("ERR duplicate field name in fieldset", server.Message);
    }
}
