using System;
using System.Runtime.InteropServices;
using System.Text;
using RESPite;
using Xunit;
using Xunit.Sdk;

#pragma warning disable CS8981, SA1134, SA1300, SA1303, SA1502 // names are weird in this test!
// ReSharper disable InconsistentNaming - to better represent expected literals
// ReSharper disable IdentifierTypo
namespace StackExchange.Redis.Tests;

public partial class AsciiHashUnitTests
{
    // note: if the hashing algorithm changes, we can update the last parameter freely; it doesn't matter
    // what it *is* - what matters is that we can see that it has entropy between different values
    [Theory]
    [InlineData(1, a.Length, a.Text, a.HashCS, 97)]
    [InlineData(2, ab.Length, ab.Text, ab.HashCS, 25185)]
    [InlineData(3, abc.Length, abc.Text, abc.HashCS, 6513249)]
    [InlineData(4, abcd.Length, abcd.Text, abcd.HashCS, 1684234849)]
    [InlineData(5, abcde.Length, abcde.Text, abcde.HashCS, 435475931745)]
    [InlineData(6, abcdef.Length, abcdef.Text, abcdef.HashCS, 112585661964897)]
    [InlineData(7, abcdefg.Length, abcdefg.Text, abcdefg.HashCS, 29104508263162465)]
    [InlineData(8, abcdefgh.Length, abcdefgh.Text, abcdefgh.HashCS, 7523094288207667809)]

    [InlineData(1, x.Length, x.Text, x.HashCS, 120)]
    [InlineData(2, xx.Length, xx.Text, xx.HashCS, 30840)]
    [InlineData(3, xxx.Length, xxx.Text, xxx.HashCS, 7895160)]
    [InlineData(4, xxxx.Length, xxxx.Text, xxxx.HashCS, 2021161080)]
    [InlineData(5, xxxxx.Length, xxxxx.Text, xxxxx.HashCS, 517417236600)]
    [InlineData(6, xxxxxx.Length, xxxxxx.Text, xxxxxx.HashCS, 132458812569720)]
    [InlineData(7, xxxxxxx.Length, xxxxxxx.Text, xxxxxxx.HashCS, 33909456017848440)]
    [InlineData(8, xxxxxxxx.Length, xxxxxxxx.Text, xxxxxxxx.HashCS, 8680820740569200760)]

    [InlineData(20, abcdefghijklmnopqrst.Length, abcdefghijklmnopqrst.Text, abcdefghijklmnopqrst.HashCS, 7523094288207667809)]

    // show that foo_bar is interpreted as foo-bar
    [InlineData(7, foo_bar.Length, foo_bar.Text, foo_bar.HashCS, 32195221641981798, "foo-bar", nameof(foo_bar))]
    [InlineData(7, foo_bar_hyphen.Length, foo_bar_hyphen.Text, foo_bar_hyphen.HashCS, 32195221641981798, "foo-bar", nameof(foo_bar_hyphen))]
    [InlineData(7, foo_bar_underscore.Length, foo_bar_underscore.Text, foo_bar_underscore.HashCS, 32195222480842598, "foo_bar", nameof(foo_bar_underscore))]
    public void Validate(int expectedLength, int actualLength, string actualValue, long actualHash, long expectedHash, string? expectedValue = null, string originForDisambiguation = "")
    {
        _ = originForDisambiguation; // to allow otherwise-identical test data to coexist
        Assert.Equal(expectedLength, actualLength);
        Assert.Equal(expectedHash, actualHash);
        var bytes = Encoding.UTF8.GetBytes(actualValue);
        Assert.Equal(expectedLength, bytes.Length);
        Assert.Equal(expectedHash, AsciiHash.HashCS(bytes));
        Assert.Equal(expectedHash, AsciiHash.HashCS(actualValue.AsSpan()));

        if (expectedValue is not null)
        {
            Assert.Equal(expectedValue, actualValue);
        }
    }

    [Fact]
    public void AsciiHashIs_Short()
    {
        ReadOnlySpan<byte> value = "abc"u8;
        var hash = AsciiHash.HashCS(value);
        Assert.Equal(abc.HashCS, hash);
        Assert.True(abc.IsCS(value, hash));

        value = "abz"u8;
        hash = AsciiHash.HashCS(value);
        Assert.NotEqual(abc.HashCS, hash);
        Assert.False(abc.IsCS(value, hash));
    }

    [Fact]
    public void AsciiHashIs_Long()
    {
        ReadOnlySpan<byte> value = "abcdefghijklmnopqrst"u8;
        var hash = AsciiHash.HashCS(value);
        Assert.Equal(abcdefghijklmnopqrst.HashCS, hash);
        Assert.True(abcdefghijklmnopqrst.IsCS(value, hash));

        value = "abcdefghijklmnopqrsz"u8;
        hash = AsciiHash.HashCS(value);
        Assert.Equal(abcdefghijklmnopqrst.HashCS, hash); // hash collision, fine
        Assert.False(abcdefghijklmnopqrst.IsCS(value, hash));
    }

    // Test case-sensitive and case-insensitive equality for various lengths
    [Theory]
    [InlineData("a")] // length 1
    [InlineData("ab")] // length 2
    [InlineData("abc")] // length 3
    [InlineData("abcd")] // length 4
    [InlineData("abcde")] // length 5
    [InlineData("abcdef")] // length 6
    [InlineData("abcdefg")] // length 7
    [InlineData("abcdefgh")] // length 8
    [InlineData("abcdefghi")] // length 9
    [InlineData("abcdefghij")] // length 10
    [InlineData("abcdefghijklmnop")] // length 16
    [InlineData("abcdefghijklmnopqrst")] // length 20
    public void CaseSensitiveEquality(string text)
    {
        var lower = Encoding.UTF8.GetBytes(text);
        var upper = Encoding.UTF8.GetBytes(text.ToUpperInvariant());

        var hashLowerCS = AsciiHash.HashCS(lower);
        var hashUpperCS = AsciiHash.HashCS(upper);

        // Case-sensitive: same case should match
        Assert.True(AsciiHash.EqualsCS(lower, lower), "CS: lower == lower");
        Assert.True(AsciiHash.EqualsCS(upper, upper), "CS: upper == upper");

        // Case-sensitive: different case should NOT match
        Assert.False(AsciiHash.EqualsCS(lower, upper), "CS: lower != upper");
        Assert.False(AsciiHash.EqualsCS(upper, lower), "CS: upper != lower");

        // Hashes should be different for different cases
        Assert.NotEqual(hashLowerCS, hashUpperCS);
    }

    [Theory]
    [InlineData("a")] // length 1
    [InlineData("ab")] // length 2
    [InlineData("abc")] // length 3
    [InlineData("abcd")] // length 4
    [InlineData("abcde")] // length 5
    [InlineData("abcdef")] // length 6
    [InlineData("abcdefg")] // length 7
    [InlineData("abcdefgh")] // length 8
    [InlineData("abcdefghi")] // length 9
    [InlineData("abcdefghij")] // length 10
    [InlineData("abcdefghijklmnop")] // length 16
    [InlineData("abcdefghijklmnopqrst")] // length 20
    public void CaseInsensitiveEquality(string text)
    {
        var lower = Encoding.UTF8.GetBytes(text);
        var upper = Encoding.UTF8.GetBytes(text.ToUpperInvariant());

        var hashLowerUC = AsciiHash.HashUC(lower);
        var hashUpperUC = AsciiHash.HashUC(upper);

        // Case-insensitive: same case should match
        Assert.True(AsciiHash.EqualsCI(lower, lower), "CI: lower == lower");
        Assert.True(AsciiHash.EqualsCI(upper, upper), "CI: upper == upper");

        // Case-insensitive: different case SHOULD match
        Assert.True(AsciiHash.EqualsCI(lower, upper), "CI: lower == upper");
        Assert.True(AsciiHash.EqualsCI(upper, lower), "CI: upper == lower");

        // CI hashes should be the same for different cases
        Assert.Equal(hashLowerUC, hashUpperUC);
    }

    [Theory]
    [InlineData("a")] // length 1
    [InlineData("ab")] // length 2
    [InlineData("abc")] // length 3
    [InlineData("abcd")] // length 4
    [InlineData("abcde")] // length 5
    [InlineData("abcdef")] // length 6
    [InlineData("abcdefg")] // length 7
    [InlineData("abcdefgh")] // length 8
    [InlineData("abcdefghi")] // length 9
    [InlineData("abcdefghij")] // length 10
    [InlineData("abcdefghijklmnop")] // length 16
    [InlineData("abcdefghijklmnopqrst")] // length 20
    [InlineData("foo-bar")] // foo_bar_hyphen
    [InlineData("foo_bar")] // foo_bar_underscore
    public void GeneratedTypes_CaseSensitive(string text)
    {
        var lower = Encoding.UTF8.GetBytes(text);
        var upper = Encoding.UTF8.GetBytes(text.ToUpperInvariant());

        var hashLowerCS = AsciiHash.HashCS(lower);
        var hashUpperCS = AsciiHash.HashCS(upper);

        // Use the generated types to verify CS behavior
        switch (text)
        {
            case "a":
                Assert.True(a.IsCS(lower, hashLowerCS));
                Assert.False(a.IsCS(lower, hashUpperCS));
                break;
            case "ab":
                Assert.True(ab.IsCS(lower, hashLowerCS));
                Assert.False(ab.IsCS(lower, hashUpperCS));
                break;
            case "abc":
                Assert.True(abc.IsCS(lower, hashLowerCS));
                Assert.False(abc.IsCS(lower, hashUpperCS));
                break;
            case "abcd":
                Assert.True(abcd.IsCS(lower, hashLowerCS));
                Assert.False(abcd.IsCS(lower, hashUpperCS));
                break;
            case "abcde":
                Assert.True(abcde.IsCS(lower, hashLowerCS));
                Assert.False(abcde.IsCS(lower, hashUpperCS));
                break;
            case "abcdef":
                Assert.True(abcdef.IsCS(lower, hashLowerCS));
                Assert.False(abcdef.IsCS(lower, hashUpperCS));
                break;
            case "abcdefg":
                Assert.True(abcdefg.IsCS(lower, hashLowerCS));
                Assert.False(abcdefg.IsCS(lower, hashUpperCS));
                break;
            case "abcdefgh":
                Assert.True(abcdefgh.IsCS(lower, hashLowerCS));
                Assert.False(abcdefgh.IsCS(lower, hashUpperCS));
                break;
            case "abcdefghijklmnopqrst":
                Assert.True(abcdefghijklmnopqrst.IsCS(lower, hashLowerCS));
                Assert.False(abcdefghijklmnopqrst.IsCS(lower, hashUpperCS));
                break;
            case "foo-bar":
                Assert.True(foo_bar_hyphen.IsCS(lower, hashLowerCS));
                Assert.False(foo_bar_hyphen.IsCS(lower, hashUpperCS));
                break;
            case "foo_bar":
                Assert.True(foo_bar_underscore.IsCS(lower, hashLowerCS));
                Assert.False(foo_bar_underscore.IsCS(lower, hashUpperCS));
                break;
        }
    }

    [Theory]
    [InlineData("a")] // length 1
    [InlineData("ab")] // length 2
    [InlineData("abc")] // length 3
    [InlineData("abcd")] // length 4
    [InlineData("abcde")] // length 5
    [InlineData("abcdef")] // length 6
    [InlineData("abcdefg")] // length 7
    [InlineData("abcdefgh")] // length 8
    [InlineData("abcdefghi")] // length 9
    [InlineData("abcdefghij")] // length 10
    [InlineData("abcdefghijklmnop")] // length 16
    [InlineData("abcdefghijklmnopqrst")] // length 20
    [InlineData("foo-bar")] // foo_bar_hyphen
    [InlineData("foo_bar")] // foo_bar_underscore
    public void GeneratedTypes_CaseInsensitive(string text)
    {
        var lower = Encoding.UTF8.GetBytes(text);
        var upper = Encoding.UTF8.GetBytes(text.ToUpperInvariant());

        var hashLowerUC = AsciiHash.HashUC(lower);
        var hashUpperUC = AsciiHash.HashUC(upper);

        // Use the generated types to verify CI behavior
        switch (text)
        {
            case "a":
                Assert.True(a.IsCI(lower, hashLowerUC));
                Assert.True(a.IsCI(upper, hashUpperUC));
                break;
            case "ab":
                Assert.True(ab.IsCI(lower, hashLowerUC));
                Assert.True(ab.IsCI(upper, hashUpperUC));
                break;
            case "abc":
                Assert.True(abc.IsCI(lower, hashLowerUC));
                Assert.True(abc.IsCI(upper, hashUpperUC));
                break;
            case "abcd":
                Assert.True(abcd.IsCI(lower, hashLowerUC));
                Assert.True(abcd.IsCI(upper, hashUpperUC));
                break;
            case "abcde":
                Assert.True(abcde.IsCI(lower, hashLowerUC));
                Assert.True(abcde.IsCI(upper, hashUpperUC));
                break;
            case "abcdef":
                Assert.True(abcdef.IsCI(lower, hashLowerUC));
                Assert.True(abcdef.IsCI(upper, hashUpperUC));
                break;
            case "abcdefg":
                Assert.True(abcdefg.IsCI(lower, hashLowerUC));
                Assert.True(abcdefg.IsCI(upper, hashUpperUC));
                break;
            case "abcdefgh":
                Assert.True(abcdefgh.IsCI(lower, hashLowerUC));
                Assert.True(abcdefgh.IsCI(upper, hashUpperUC));
                break;
            case "abcdefghijklmnopqrst":
                Assert.True(abcdefghijklmnopqrst.IsCI(lower, hashLowerUC));
                Assert.True(abcdefghijklmnopqrst.IsCI(upper, hashUpperUC));
                break;
            case "foo-bar":
                Assert.True(foo_bar_hyphen.IsCI(lower, hashLowerUC));
                Assert.True(foo_bar_hyphen.IsCI(upper, hashUpperUC));
                break;
            case "foo_bar":
                Assert.True(foo_bar_underscore.IsCI(lower, hashLowerUC));
                Assert.True(foo_bar_underscore.IsCI(upper, hashUpperUC));
                break;
        }
    }

    // Test each generated AsciiHash type individually for case sensitivity
    [Fact]
    public void GeneratedType_a_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "a"u8;
        ReadOnlySpan<byte> upper = "A"u8;

        Assert.True(a.IsCS(lower, AsciiHash.HashCS(lower)));
        Assert.False(a.IsCS(upper, AsciiHash.HashCS(upper)));
        Assert.True(a.IsCI(lower, AsciiHash.HashUC(lower)));
        Assert.True(a.IsCI(upper, AsciiHash.HashUC(upper)));
    }

    [Fact]
    public void GeneratedType_ab_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "ab"u8;
        ReadOnlySpan<byte> upper = "AB"u8;

        Assert.True(ab.IsCS(lower, AsciiHash.HashCS(lower)));
        Assert.False(ab.IsCS(upper, AsciiHash.HashCS(upper)));
        Assert.True(ab.IsCI(lower, AsciiHash.HashUC(lower)));
        Assert.True(ab.IsCI(upper, AsciiHash.HashUC(upper)));
    }

    [Fact]
    public void GeneratedType_abc_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abc"u8;
        ReadOnlySpan<byte> upper = "ABC"u8;

        Assert.True(abc.IsCS(lower, AsciiHash.HashCS(lower)));
        Assert.False(abc.IsCS(upper, AsciiHash.HashCS(upper)));
        Assert.True(abc.IsCI(lower, AsciiHash.HashUC(lower)));
        Assert.True(abc.IsCI(upper, AsciiHash.HashUC(upper)));
    }

    [Fact]
    public void GeneratedType_abcd_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcd"u8;
        ReadOnlySpan<byte> upper = "ABCD"u8;

        Assert.True(abcd.IsCS(lower, AsciiHash.HashCS(lower)));
        Assert.False(abcd.IsCS(upper, AsciiHash.HashCS(upper)));
        Assert.True(abcd.IsCI(lower, AsciiHash.HashUC(lower)));
        Assert.True(abcd.IsCI(upper, AsciiHash.HashUC(upper)));
    }

    [Fact]
    public void GeneratedType_abcde_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcde"u8;
        ReadOnlySpan<byte> upper = "ABCDE"u8;

        Assert.True(abcde.IsCS(lower, AsciiHash.HashCS(lower)));
        Assert.False(abcde.IsCS(upper, AsciiHash.HashCS(upper)));
        Assert.True(abcde.IsCI(lower, AsciiHash.HashUC(lower)));
        Assert.True(abcde.IsCI(upper, AsciiHash.HashUC(upper)));
    }

    [Fact]
    public void GeneratedType_abcdef_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcdef"u8;
        ReadOnlySpan<byte> upper = "ABCDEF"u8;

        Assert.True(abcdef.IsCS(lower, AsciiHash.HashCS(lower)));
        Assert.False(abcdef.IsCS(upper, AsciiHash.HashCS(upper)));
        Assert.True(abcdef.IsCI(lower, AsciiHash.HashUC(lower)));
        Assert.True(abcdef.IsCI(upper, AsciiHash.HashUC(upper)));
    }

    [Fact]
    public void GeneratedType_abcdefg_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcdefg"u8;
        ReadOnlySpan<byte> upper = "ABCDEFG"u8;

        Assert.True(abcdefg.IsCS(lower, AsciiHash.HashCS(lower)));
        Assert.False(abcdefg.IsCS(upper, AsciiHash.HashCS(upper)));
        Assert.True(abcdefg.IsCI(lower, AsciiHash.HashUC(lower)));
        Assert.True(abcdefg.IsCI(upper, AsciiHash.HashUC(upper)));
    }

    [Fact]
    public void GeneratedType_abcdefgh_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcdefgh"u8;
        ReadOnlySpan<byte> upper = "ABCDEFGH"u8;

        Assert.True(abcdefgh.IsCS(lower, AsciiHash.HashCS(lower)));
        Assert.False(abcdefgh.IsCS(upper, AsciiHash.HashCS(upper)));
        Assert.True(abcdefgh.IsCI(lower, AsciiHash.HashUC(lower)));
        Assert.True(abcdefgh.IsCI(upper, AsciiHash.HashUC(upper)));
    }

    [Fact]
    public void GeneratedType_abcdefghijklmnopqrst_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcdefghijklmnopqrst"u8;
        ReadOnlySpan<byte> upper = "ABCDEFGHIJKLMNOPQRST"u8;

        Assert.True(abcdefghijklmnopqrst.IsCS(lower, AsciiHash.HashCS(lower)));
        Assert.False(abcdefghijklmnopqrst.IsCS(upper, AsciiHash.HashCS(upper)));
        Assert.True(abcdefghijklmnopqrst.IsCI(lower, AsciiHash.HashUC(lower)));
        Assert.True(abcdefghijklmnopqrst.IsCI(upper, AsciiHash.HashUC(upper)));
    }

    [Fact]
    public void GeneratedType_foo_bar_CaseSensitivity()
    {
        // foo_bar is interpreted as foo-bar
        ReadOnlySpan<byte> lower = "foo-bar"u8;
        ReadOnlySpan<byte> upper = "FOO-BAR"u8;

        Assert.True(foo_bar.IsCS(lower, AsciiHash.HashCS(lower)));
        Assert.False(foo_bar.IsCS(upper, AsciiHash.HashCS(upper)));
        Assert.True(foo_bar.IsCI(lower, AsciiHash.HashUC(lower)));
        Assert.True(foo_bar.IsCI(upper, AsciiHash.HashUC(upper)));
    }

    [Fact]
    public void GeneratedType_foo_bar_hyphen_CaseSensitivity()
    {
        // foo_bar_hyphen is explicitly "foo-bar"
        ReadOnlySpan<byte> lower = "foo-bar"u8;
        ReadOnlySpan<byte> upper = "FOO-BAR"u8;

        Assert.True(foo_bar_hyphen.IsCS(lower, AsciiHash.HashCS(lower)));
        Assert.False(foo_bar_hyphen.IsCS(upper, AsciiHash.HashCS(upper)));
        Assert.True(foo_bar_hyphen.IsCI(lower, AsciiHash.HashUC(lower)));
        Assert.True(foo_bar_hyphen.IsCI(upper, AsciiHash.HashUC(upper)));
    }

    [AsciiHash] private static partial class a { }
    [AsciiHash] private static partial class ab { }
    [AsciiHash] private static partial class abc { }
    [AsciiHash] private static partial class abcd { }
    [AsciiHash] private static partial class abcde { }
    [AsciiHash] private static partial class abcdef { }
    [AsciiHash] private static partial class abcdefg { }
    [AsciiHash] private static partial class abcdefgh { }

    [AsciiHash] private static partial class abcdefghijklmnopqrst { }

    // show that foo_bar and foo-bar are different
    [AsciiHash] private static partial class foo_bar { }
    [AsciiHash("foo-bar")] private static partial class foo_bar_hyphen { }
    [AsciiHash("foo_bar")] private static partial class foo_bar_underscore { }

    [AsciiHash] private static partial class 窓 { }

    [AsciiHash] private static partial class x { }
    [AsciiHash] private static partial class xx { }
    [AsciiHash] private static partial class xxx { }
    [AsciiHash] private static partial class xxxx { }
    [AsciiHash] private static partial class xxxxx { }
    [AsciiHash] private static partial class xxxxxx { }
    [AsciiHash] private static partial class xxxxxxx { }
    [AsciiHash] private static partial class xxxxxxxx { }
}
