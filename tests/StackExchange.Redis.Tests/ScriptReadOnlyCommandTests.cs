using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// The read-only script APIs should put EVAL_RO/EVALSHA_RO on the wire where the server and command map
/// allow it, and fall back to EVAL/EVALSHA where they do not.
/// </summary>
/// <remarks>
/// Asserting which command was actually sent is done by renaming it in the command map to something the
/// server does not implement: if we chose the read-only form, the server complains about the bogus name;
/// if we fell back, the script simply runs. A replica cannot be used to tell these apart - a modern server
/// accepts a plain EVAL on a replica so long as the script does not write.
/// </remarks>
public class ScriptReadOnlyCommandTests(ITestOutputHelper output) : TestBase(output)
{
    private const string Script = "return 1";
    private const string BogusEvalRo = "EVAL_RO_DOES_NOT_EXIST";
    private const string BogusEvalShaRo = "EVALSHA_RO_DOES_NOT_EXIST";

    // TestBase.Create has no command-map hook, so build this one directly
    private async Task<ConnectionMultiplexer> CreateRenamedAsync()
    {
        var config = ConfigurationOptions.Parse(GetConfiguration());
        config.AllowAdmin = true;
        config.CommandMap = CommandMap.Create(new Dictionary<string, string?>
        {
            ["EVAL_RO"] = BogusEvalRo,
            ["EVALSHA_RO"] = BogusEvalShaRo,
        });
        var conn = await ConnectionMultiplexer.ConnectAsync(config);
        var version = conn.GetServer(conn.GetEndPoints()[0]).Version;
        Assert.SkipUnless(new RedisFeatures(version).ReadOnlyScripts, $"Requires server 7.0+, but server is {version}.");
        return conn;
    }

    [Fact]
    public async Task ReadOnlyEval_UsesEvalRo()
    {
        await using var conn = await CreateRenamedAsync();
        var db = conn.GetDatabase();

        // NoScriptCache keeps us on the non-hash path, so this is unambiguously the EVAL_RO decision
        var ex = Assert.Throws<RedisServerException>(
            () => db.ScriptEvaluateReadOnly(Script, flags: CommandFlags.NoScriptCache));
        Assert.Contains(BogusEvalRo, ex.Message);
    }

    [Fact]
    public async Task ReadOnlyEvalResp_UsesEvalRo()
    {
        await using var conn = await CreateRenamedAsync();
        var db = conn.GetDatabase();

        var ex = Assert.Throws<RedisServerException>(
            () => db.ScriptEvaluateReadOnlyResp(Script, default, default, CommandFlags.NoScriptCache));
        Assert.Contains(BogusEvalRo, ex.Message);
    }

    [Fact]
    public async Task ReadOnlyEval_UsesEvalShaRo_OnceTheHashIsKnown()
    {
        await using var conn = await CreateRenamedAsync();
        var db = conn.GetDatabase();

        // first call loads the script and caches its hash; the call itself still goes out as EVAL_RO
        Assert.Throws<RedisServerException>(() => db.ScriptEvaluateReadOnly(Script));

        // now the hash is known, so the second attempt takes the EVALSHA_RO path
        var ex = Assert.Throws<RedisServerException>(() => db.ScriptEvaluateReadOnly(Script));
        Assert.Contains(BogusEvalShaRo, ex.Message);
    }

    [Fact]
    public async Task WritableEval_IsUnaffected()
    {
        // the renaming above only touches the read-only forms; a normal ScriptEvaluate must be untouched
        await using var conn = await CreateRenamedAsync();
        var db = conn.GetDatabase();
        Assert.Equal(1, (long)db.ScriptEvaluate(Script, flags: CommandFlags.NoScriptCache));
    }

    [Fact]
    public async Task FallsBackToEval_WhenReadOnlyCommandsAreDisabled()
    {
        await using var conn = Create(disabledCommands: ["eval_ro", "evalsha_ro"]);
        var db = conn.GetDatabase();

        // disabled rather than renamed: we must quietly use EVAL/EVALSHA instead of failing
        Assert.Equal(1, (long)db.ScriptEvaluateReadOnly(Script, flags: CommandFlags.NoScriptCache));
        Assert.Equal(1, (long)db.ScriptEvaluateReadOnly(Script));
        using var resp = db.ScriptEvaluateReadOnlyResp(Script, default, default);
        Assert.Equal(1, (long)resp.ReadScalar().ReadRedisValue());
    }

    [Fact]
    public void FallingBackKeepsTheReadOnlyRetryCategory()
    {
        // EVAL_RO defaults to CommandRetryReadOnly and EVAL to CommandRetryWriteAccumulating, so a
        // fallback that only swapped the command would quietly change how the call retries. This is
        // message state rather than anything that reaches the wire, so it is asserted directly.
        var unavailable = CommandMap.Create(["eval_ro", "evalsha_ro"], available: false);

        var flags = CommandFlags.None;
        var command = RedisDatabase.ForReadOnlyScript(unavailable, RedisCommand.EVAL_RO, ref flags);
        Assert.Equal(RedisCommand.EVAL, command); // fell back...
        Assert.Equal(CommandFlags.CommandRetryReadOnly, flags & Message.MaskRetryCategory); // ...but still read-only

        flags = CommandFlags.None;
        command = RedisDatabase.ForReadOnlyScript(unavailable, RedisCommand.EVALSHA_RO, ref flags);
        Assert.Equal(RedisCommand.EVALSHA, command);
        Assert.Equal(CommandFlags.CommandRetryReadOnly, flags & Message.MaskRetryCategory);

        // an explicit category from the caller still wins over the fallback's
        flags = CommandFlags.CommandRetryNever;
        RedisDatabase.ForReadOnlyScript(unavailable, RedisCommand.EVAL_RO, ref flags);
        Assert.Equal(CommandFlags.CommandRetryNever, flags & Message.MaskRetryCategory);

        // and where the commands are available, the read-only form is kept as-is
        flags = CommandFlags.None;
        Assert.Equal(RedisCommand.EVAL_RO, RedisDatabase.ForReadOnlyScript(CommandMap.Default, RedisCommand.EVAL_RO, ref flags));

        // the pin above is only worth anything because Message's own defaulting leaves an already-chosen
        // category alone; without that, EVAL's default would overwrite it right back to write-accumulating
        Assert.Equal(
            CommandFlags.CommandRetryReadOnly,
            CommandFlags.CommandRetryReadOnly.WithDefaultCategory(RedisCommand.EVAL) & Message.MaskRetryCategory);
        Assert.Equal(
            CommandFlags.CommandRetryWriteAccumulating,
            CommandFlags.None.WithDefaultCategory(RedisCommand.EVAL) & Message.MaskRetryCategory);
    }

    [Fact]
    public void ReadOnlyScriptsRequireServer7()
    {
        Assert.False(new RedisFeatures(new System.Version(6, 2, 0)).ReadOnlyScripts);
        Assert.True(new RedisFeatures(RedisFeatures.v7_0_0_rc1).ReadOnlyScripts);
        Assert.True(new RedisFeatures(new System.Version(7, 0, 0)).ReadOnlyScripts);
    }
}
