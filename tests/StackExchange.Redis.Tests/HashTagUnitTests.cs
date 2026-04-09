using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

public class HashTagUnitTests
{
    [Fact]
    public void TestHashTagCoverage()
    {
        HashSet<string> uniques = [];
        Assert.Equal("", ServerSelectionStrategy.GetHashTag(ServerSelectionStrategy.NoSlot));
        Assert.Equal("", ServerSelectionStrategy.GetHashTag(ServerSelectionStrategy.MultipleSlots));
        Span<byte> buffer = stackalloc byte[3];
        for (int i = 0; i < ServerSelectionStrategy.TotalSlots; i++)
        {
            var tag = ServerSelectionStrategy.GetHashTag(i);
            Assert.False(string.IsNullOrEmpty(tag));
            Assert.True(uniques.Add(tag));

            var len = Encoding.ASCII.GetBytes(tag, buffer);
            var slot = ServerSelectionStrategy.GetClusterSlot(buffer.Slice(0, len));
            Assert.Equal(i, slot);
        }
        Assert.Equal(ServerSelectionStrategy.TotalSlots, uniques.Count);
    }
}
