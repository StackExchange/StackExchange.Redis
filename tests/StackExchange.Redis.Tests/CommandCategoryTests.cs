using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Every command this library knows how to name must also say how safe it is to replay.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a sweep and not just the <c>Debug.Assert</c>.</b> The assert only fires if a command is
/// actually executed, in a Debug build, by a test that happens to exist - so a command nobody exercises
/// keeps its gap indefinitely. Enumerating the enum finds them all at once, which is how the first four
/// turned up: <c>SDIFFCARD</c> and <c>SUNIONCARD</c> were missing while their siblings <c>SDIFF</c>,
/// <c>SINTER</c>, <c>SINTERCARD</c> and <c>SUNION</c> all sat in the read-only group, and
/// <c>SUNIONCARD</c> is on the new surface today.
/// </para>
/// <para>
/// <b>Why the gap matters even though "never" is the safe answer.</b> It is safe and silent and
/// one-directional: a read command that falls through simply stops being retried and stops being cached.
/// Nothing fails, nothing logs, it just gets slower - so nobody reports it, and the gap survives.
/// </para>
/// </remarks>
public class CommandCategoryTests
{
    private static RedisCommand[] AllCommands() => (RedisCommand[])Enum.GetValues(typeof(RedisCommand));

    [Fact]
    public void EveryCommandDeclaresARetryCategory()
    {
        var missing = AllCommands()
            .Where(command => CommandFlagsExtensions.GetDefaultCategory(command) is null)
            .Select(command => command.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([], missing);
    }

    /// <summary>
    /// A declared category is one of the ladder's values, not an arbitrary bit pattern.
    /// </summary>
    /// <remarks>
    /// The categories share a bit region, so an entry that or-ed two together would land on a third
    /// meaning rather than failing - worth ruling out, since the table is written by hand.
    /// </remarks>
    [Fact]
    public void EveryDeclaredCategoryIsOneOfTheLadder()
    {
        var ladder = new HashSet<CommandFlags>
        {
            CommandFlags.CommandRetryAlways,
            CommandFlags.CommandRetryConnection,
            CommandFlags.CommandRetryReadOnly,
            CommandFlags.CommandRetryWriteChecked,
            CommandFlags.CommandRetryWriteLastWins,
            CommandFlags.CommandRetryWriteAccumulating,
            CommandFlags.CommandRetryServerAdmin,
            CommandFlags.CommandRetryNever,
        };

        var offenders = new List<string>();
        foreach (var command in AllCommands())
        {
            if (CommandFlagsExtensions.GetDefaultCategory(command) is not { } category) continue;

            // the orthogonal flags ride alongside the ladder and are not part of it
            var rung = category & Message.MaskRetryCategory;
            if (!ladder.Contains(rung)) offenders.Add($"{command}={category}");
        }

        Assert.Equal([], offenders);
    }

    /// <summary>
    /// A command the table has no opinion on still reaches the callers as the safest answer.
    /// </summary>
    /// <remarks>
    /// The gap is a bug, but it must not be an unsafe one: <c>WithDefaultCategory</c> coalesces a missing
    /// entry to <see cref="CommandFlags.CommandRetryNever"/>, so the failure mode is "slower", never
    /// "replayed when it should not have been".
    /// </remarks>
    [Fact]
    public void AMissingEntryStillLandsOnNever()
    {
        // UNKNOWN is in the table deliberately; this pins what the coalesce does, using it as the stand-in
        Assert.Equal(
            CommandFlags.CommandRetryNever,
            CommandFlags.None.WithDefaultCategory(RedisCommand.UNKNOWN) & Message.MaskRetryCategory);
    }

    /// <summary>The caller's own category always wins over the table's.</summary>
    /// <remarks>
    /// This is what lets a caller doing something the command name cannot express - <c>XREAD</c> with
    /// <c>BLOCK</c>, <c>SORT</c> with <c>STORE</c> - say so, and it is why the surface can apply a default
    /// unconditionally without overriding anybody.
    /// </remarks>
    [Fact]
    public void TheCallersCategoryWins()
    {
        var flags = CommandFlags.CommandRetryNever.WithDefaultCategory(RedisCommand.GET);
        Assert.Equal(CommandFlags.CommandRetryNever, flags & Message.MaskRetryCategory);
    }
}
