using System;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

#pragma warning disable CS8981, SA1134, SA1300, SA1303, SA1502 // names are weird in this test!
// ReSharper disable InconsistentNaming - to better represent expected literals
// ReSharper disable IdentifierTypo
namespace StackExchange.Redis.Tests;

public partial class FastHashTests
{
    // note: if the hashing algorithm changes, we can update the last parameter freely; it doesn't matter
    // what it *is* - what matters is that we can see that it has entropy between different values
    [Theory]
    [InlineData(1, a.Length, a.Text, a.Hash, 97)]
    [InlineData(2, ab.Length, ab.Text, ab.Hash, 25185)]
    [InlineData(3, abc.Length, abc.Text, abc.Hash, 6513249)]
    [InlineData(4, abcd.Length, abcd.Text, abcd.Hash, 1684234849)]
    [InlineData(5, abcde.Length, abcde.Text, abcde.Hash, 435475931745)]
    [InlineData(6, abcdef.Length, abcdef.Text, abcdef.Hash, 112585661964897)]
    [InlineData(7, abcdefg.Length, abcdefg.Text, abcdefg.Hash, 29104508263162465)]
    [InlineData(8, abcdefgh.Length, abcdefgh.Text, abcdefgh.Hash, 7523094288207667809)]

    [InlineData(1, x.Length, x.Text, x.Hash, 120)]
    [InlineData(2, xx.Length, xx.Text, xx.Hash, 30840)]
    [InlineData(3, xxx.Length, xxx.Text, xxx.Hash, 7895160)]
    [InlineData(4, xxxx.Length, xxxx.Text, xxxx.Hash, 2021161080)]
    [InlineData(5, xxxxx.Length, xxxxx.Text, xxxxx.Hash, 517417236600)]
    [InlineData(6, xxxxxx.Length, xxxxxx.Text, xxxxxx.Hash, 132458812569720)]
    [InlineData(7, xxxxxxx.Length, xxxxxxx.Text, xxxxxxx.Hash, 33909456017848440)]
    [InlineData(8, xxxxxxxx.Length, xxxxxxxx.Text, xxxxxxxx.Hash, 8680820740569200760)]

    [InlineData(3, 窓.Length, 窓.Text, 窓.Hash, 9677543, "窓")]
    [InlineData(20, abcdefghijklmnopqrst.Length, abcdefghijklmnopqrst.Text, abcdefghijklmnopqrst.Hash, 7523094288207667809)]

    // show that foo_bar is interpreted as foo-bar
    [InlineData(7, foo_bar.Length, foo_bar.Text, foo_bar.Hash, 32195221641981798, "foo-bar", nameof(foo_bar))]
    [InlineData(7, foo_bar_hyphen.Length, foo_bar_hyphen.Text, foo_bar_hyphen.Hash, 32195221641981798, "foo-bar", nameof(foo_bar_hyphen))]
    [InlineData(7, foo_bar_underscore.Length, foo_bar_underscore.Text, foo_bar_underscore.Hash, 32195222480842598, "foo_bar", nameof(foo_bar_underscore))]
    public void Validate(int expectedLength, int actualLength, string actualValue, long actualHash, long expectedHash, string? expectedValue = null, string originForDisambiguation = "")
    {
        _ = originForDisambiguation; // to allow otherwise-identical test data to coexist
        Assert.Equal(expectedLength, actualLength);
        Assert.Equal(expectedHash, actualHash);
        var bytes = Encoding.UTF8.GetBytes(actualValue);
        Assert.Equal(expectedLength, bytes.Length);
        Assert.Equal(expectedHash, FastHash.Hash64(bytes));
#pragma warning disable CS0618 // Type or member is obsolete
        Assert.Equal(expectedHash, FastHash.Hash64Fallback(bytes));
#pragma warning restore CS0618 // Type or member is obsolete
        if (expectedValue is not null)
        {
            Assert.Equal(expectedValue, actualValue);
        }
    }

    [Fact]
    public void FastHashIs_Short()
    {
        ReadOnlySpan<byte> value = "abc"u8;
        var hash = value.Hash64();
        Assert.Equal(abc.Hash, hash);
        Assert.True(abc.Is(hash, value));

        value = "abz"u8;
        hash = value.Hash64();
        Assert.NotEqual(abc.Hash, hash);
        Assert.False(abc.Is(hash, value));
    }

    [Fact]
    public void FastHashIs_Long()
    {
        ReadOnlySpan<byte> value = "abcdefghijklmnopqrst"u8;
        var hash = value.Hash64();
        Assert.Equal(abcdefghijklmnopqrst.Hash, hash);
        Assert.True(abcdefghijklmnopqrst.Is(hash, value));

        value = "abcdefghijklmnopqrsz"u8;
        hash = value.Hash64();
        Assert.Equal(abcdefghijklmnopqrst.Hash, hash); // hash collision, fine
        Assert.False(abcdefghijklmnopqrst.Is(hash, value));
    }

    [FastHash] private static partial class a { }
    [FastHash] private static partial class ab { }
    [FastHash] private static partial class abc { }
    [FastHash] private static partial class abcd { }
    [FastHash] private static partial class abcde { }
    [FastHash] private static partial class abcdef { }
    [FastHash] private static partial class abcdefg { }
    [FastHash] private static partial class abcdefgh { }

    [FastHash] private static partial class abcdefghijklmnopqrst { }

    // show that foo_bar and foo-bar are different
    [FastHash] private static partial class foo_bar { }
    [FastHash("foo-bar")] private static partial class foo_bar_hyphen { }
    [FastHash("foo_bar")] private static partial class foo_bar_underscore { }

    [FastHash] private static partial class 窓 { }

    [FastHash] private static partial class x { }
    [FastHash] private static partial class xx { }
    [FastHash] private static partial class xxx { }
    [FastHash] private static partial class xxxx { }
    [FastHash] private static partial class xxxxx { }
    [FastHash] private static partial class xxxxxx { }
    [FastHash] private static partial class xxxxxxx { }
    [FastHash] private static partial class xxxxxxxx { }
}
