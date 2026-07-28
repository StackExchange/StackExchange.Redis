using System;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Parsing of multi-group (Active-Active) connection strings. Groups are delimited by a <b>bare</b> <c>|</c>
/// comma-token (i.e. <c>,|,</c>); a <c>|</c> glued to text or inside a value is never a delimiter, which is what
/// keeps the (awkward) value-escaping rules unchanged. Per-member <c>weight=</c>/<c>member=</c> live within a group.
/// </summary>
public class MultiGroupConfigTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:6379,ssl=true,password=abc")]
    [InlineData("a:6379,b:6380,password=has|pipe|inside")] // a '|' inside a value is NOT a delimiter
    [InlineData("a:6379|b:6380")] // a '|' glued to text is NOT a delimiter (it's inside one comma-token)
    public void SingleGroupStringsAreNotMultiGroup(string config)
        => Assert.Null(ConfigurationOptions.SplitGroups(config));

    [Fact]
    public void SplitsGroupsOnBareDelimiter()
    {
        var groups = ConfigurationOptions.SplitGroups(
            "east-1:6379,east-2:6379,password=pA,weight=10,member=US East,|,west-1:6379,password=pB,weight=5,member=US West");

        Assert.NotNull(groups);
        Assert.Equal(2, groups!.Count);

        var east = groups[0];
        Assert.Equal(2, east.EndPoints.Count);
        Assert.Equal("pA", east.Password);
        Assert.Equal(10d, east.MemberWeight);
        Assert.Equal("US East", east.MemberName);

        var west = groups[1];
        Assert.Single(west.EndPoints);
        Assert.Equal("pB", west.Password);
        Assert.Equal(5d, west.MemberWeight);
        Assert.Equal("US West", west.MemberName);
    }

    [Fact]
    public void MissingWeightAndNameAreNull()
    {
        var groups = ConfigurationOptions.SplitGroups("a:6379,|,b:6379");
        Assert.NotNull(groups);
        Assert.Equal(2, groups!.Count);
        Assert.Null(groups[0].MemberWeight);
        Assert.Null(groups[0].MemberName);
    }

    [Fact]
    public void LeadingDelimiterIsAnIgnoredMultiGroupMarker()
    {
        // a leading '|' is an explicit "this is multi-group" opt-in; it does not create an empty first group
        var groups = ConfigurationOptions.SplitGroups("|,a:6379,member=A,|,b:6379,member=B");
        Assert.NotNull(groups);
        Assert.Equal(2, groups!.Count);
        Assert.Equal("A", groups[0].MemberName);
        Assert.Equal("B", groups[1].MemberName);
    }

    [Fact]
    public void PipeInsidePasswordSurvivesGroupSplit()
    {
        var groups = ConfigurationOptions.SplitGroups("a:6379,password=p|A,|,b:6379,password=pB");
        Assert.NotNull(groups);
        Assert.Equal(2, groups!.Count);
        Assert.Equal("p|A", groups[0].Password); // the in-value '|' is preserved; only the bare '|' delimited
        Assert.Equal("pB", groups[1].Password);
    }

    [Fact]
    public void ParsingAMultiGroupStringAsASingleConfigThrows()
    {
        var ex = Assert.Throws<ArgumentException>(() => ConfigurationOptions.Parse("a:6379,|,b:6379"));
        Assert.Contains("multi-group", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WeightAndMemberRoundTripThroughToString()
    {
        var round = ConfigurationOptions.Parse("localhost:6379,weight=2.5,member=Primary");
        Assert.Equal(2.5d, round.MemberWeight);
        Assert.Equal("Primary", round.MemberName);

        var text = round.ToString();
        Assert.Contains("weight=2.5", text);
        Assert.Contains("member=Primary", text);

        var reparsed = ConfigurationOptions.Parse(text);
        Assert.Equal(2.5d, reparsed.MemberWeight);
        Assert.Equal("Primary", reparsed.MemberName);
    }
}
