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

public partial class FastHashTests(ITestOutputHelper log)
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

    [InlineData(3, 窓.Length, 窓.Text, 窓.HashCS, 9677543, "窓")]
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
        Assert.Equal(expectedHash, FastHash.HashCS(bytes));
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
        var hash = FastHash.HashCS(value);
        Assert.Equal(abc.HashCS, hash);
        Assert.True(abc.IsCS(hash, value));

        value = "abz"u8;
        hash = FastHash.HashCS(value);
        Assert.NotEqual(abc.HashCS, hash);
        Assert.False(abc.IsCS(hash, value));
    }

    [Fact]
    public void FastHashIs_Long()
    {
        ReadOnlySpan<byte> value = "abcdefghijklmnopqrst"u8;
        var hash = FastHash.HashCS(value);
        Assert.Equal(abcdefghijklmnopqrst.HashCS, hash);
        Assert.True(abcdefghijklmnopqrst.IsCS(hash, value));

        value = "abcdefghijklmnopqrsz"u8;
        hash = FastHash.HashCS(value);
        Assert.Equal(abcdefghijklmnopqrst.HashCS, hash); // hash collision, fine
        Assert.False(abcdefghijklmnopqrst.IsCS(hash, value));
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

        var hashLowerCS = FastHash.HashCS(lower);
        var hashUpperCS = FastHash.HashCS(upper);

        // Case-sensitive: same case should match
        Assert.True(FastHash.EqualsCS(lower, lower), "CS: lower == lower");
        Assert.True(FastHash.EqualsCS(upper, upper), "CS: upper == upper");

        // Case-sensitive: different case should NOT match
        Assert.False(FastHash.EqualsCS(lower, upper), "CS: lower != upper");
        Assert.False(FastHash.EqualsCS(upper, lower), "CS: upper != lower");

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

        var hashLowerCI = FastHash.HashCI(lower);
        var hashUpperCI = FastHash.HashCI(upper);

        // Case-insensitive: same case should match
        Assert.True(FastHash.EqualsCI(lower, lower), "CI: lower == lower");
        Assert.True(FastHash.EqualsCI(upper, upper), "CI: upper == upper");

        // Case-insensitive: different case SHOULD match
        Assert.True(FastHash.EqualsCI(lower, upper), "CI: lower == upper");
        Assert.True(FastHash.EqualsCI(upper, lower), "CI: upper == lower");

        // CI hashes should be the same for different cases
        Assert.Equal(hashLowerCI, hashUpperCI);
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

        var hashLowerCS = FastHash.HashCS(lower);
        var hashUpperCS = FastHash.HashCS(upper);

        // Use the generated types to verify CS behavior
        switch (text)
        {
            case "a":
                Assert.True(a.IsCS(hashLowerCS, lower));
                Assert.False(a.IsCS(hashUpperCS, upper));
                break;
            case "ab":
                Assert.True(ab.IsCS(hashLowerCS, lower));
                Assert.False(ab.IsCS(hashUpperCS, upper));
                break;
            case "abc":
                Assert.True(abc.IsCS(hashLowerCS, lower));
                Assert.False(abc.IsCS(hashUpperCS, upper));
                break;
            case "abcd":
                Assert.True(abcd.IsCS(hashLowerCS, lower));
                Assert.False(abcd.IsCS(hashUpperCS, upper));
                break;
            case "abcde":
                Assert.True(abcde.IsCS(hashLowerCS, lower));
                Assert.False(abcde.IsCS(hashUpperCS, upper));
                break;
            case "abcdef":
                Assert.True(abcdef.IsCS(hashLowerCS, lower));
                Assert.False(abcdef.IsCS(hashUpperCS, upper));
                break;
            case "abcdefg":
                Assert.True(abcdefg.IsCS(hashLowerCS, lower));
                Assert.False(abcdefg.IsCS(hashUpperCS, upper));
                break;
            case "abcdefgh":
                Assert.True(abcdefgh.IsCS(hashLowerCS, lower));
                Assert.False(abcdefgh.IsCS(hashUpperCS, upper));
                break;
            case "abcdefghijklmnopqrst":
                Assert.True(abcdefghijklmnopqrst.IsCS(hashLowerCS, lower));
                Assert.False(abcdefghijklmnopqrst.IsCS(hashUpperCS, upper));
                break;
            case "foo-bar":
                Assert.True(foo_bar_hyphen.IsCS(hashLowerCS, lower));
                Assert.False(foo_bar_hyphen.IsCS(hashUpperCS, upper));
                break;
            case "foo_bar":
                Assert.True(foo_bar_underscore.IsCS(hashLowerCS, lower));
                Assert.False(foo_bar_underscore.IsCS(hashUpperCS, upper));
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

        var hashLowerCI = FastHash.HashCI(lower);
        var hashUpperCI = FastHash.HashCI(upper);

        // Use the generated types to verify CI behavior
        switch (text)
        {
            case "a":
                Assert.True(a.IsCI(hashLowerCI, lower));
                Assert.True(a.IsCI(hashUpperCI, upper));
                break;
            case "ab":
                Assert.True(ab.IsCI(hashLowerCI, lower));
                Assert.True(ab.IsCI(hashUpperCI, upper));
                break;
            case "abc":
                Assert.True(abc.IsCI(hashLowerCI, lower));
                Assert.True(abc.IsCI(hashUpperCI, upper));
                break;
            case "abcd":
                Assert.True(abcd.IsCI(hashLowerCI, lower));
                Assert.True(abcd.IsCI(hashUpperCI, upper));
                break;
            case "abcde":
                Assert.True(abcde.IsCI(hashLowerCI, lower));
                Assert.True(abcde.IsCI(hashUpperCI, upper));
                break;
            case "abcdef":
                Assert.True(abcdef.IsCI(hashLowerCI, lower));
                Assert.True(abcdef.IsCI(hashUpperCI, upper));
                break;
            case "abcdefg":
                Assert.True(abcdefg.IsCI(hashLowerCI, lower));
                Assert.True(abcdefg.IsCI(hashUpperCI, upper));
                break;
            case "abcdefgh":
                Assert.True(abcdefgh.IsCI(hashLowerCI, lower));
                Assert.True(abcdefgh.IsCI(hashUpperCI, upper));
                break;
            case "abcdefghijklmnopqrst":
                Assert.True(abcdefghijklmnopqrst.IsCI(hashLowerCI, lower));
                Assert.True(abcdefghijklmnopqrst.IsCI(hashUpperCI, upper));
                break;
            case "foo-bar":
                Assert.True(foo_bar_hyphen.IsCI(hashLowerCI, lower));
                Assert.True(foo_bar_hyphen.IsCI(hashUpperCI, upper));
                break;
            case "foo_bar":
                Assert.True(foo_bar_underscore.IsCI(hashLowerCI, lower));
                Assert.True(foo_bar_underscore.IsCI(hashUpperCI, upper));
                break;
        }
    }

    // Test each generated FastHash type individually for case sensitivity
    [Fact]
    public void GeneratedType_a_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "a"u8;
        ReadOnlySpan<byte> upper = "A"u8;

        Assert.True(a.IsCS(FastHash.HashCS(lower), lower));
        Assert.False(a.IsCS(FastHash.HashCS(upper), upper));
        Assert.True(a.IsCI(FastHash.HashCI(lower), lower));
        Assert.True(a.IsCI(FastHash.HashCI(upper), upper));
    }

    [Fact]
    public void GeneratedType_ab_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "ab"u8;
        ReadOnlySpan<byte> upper = "AB"u8;

        Assert.True(ab.IsCS(FastHash.HashCS(lower), lower));
        Assert.False(ab.IsCS(FastHash.HashCS(upper), upper));
        Assert.True(ab.IsCI(FastHash.HashCI(lower), lower));
        Assert.True(ab.IsCI(FastHash.HashCI(upper), upper));
    }

    [Fact]
    public void GeneratedType_abc_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abc"u8;
        ReadOnlySpan<byte> upper = "ABC"u8;

        Assert.True(abc.IsCS(FastHash.HashCS(lower), lower));
        Assert.False(abc.IsCS(FastHash.HashCS(upper), upper));
        Assert.True(abc.IsCI(FastHash.HashCI(lower), lower));
        Assert.True(abc.IsCI(FastHash.HashCI(upper), upper));
    }

    [Fact]
    public void GeneratedType_abcd_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcd"u8;
        ReadOnlySpan<byte> upper = "ABCD"u8;

        Assert.True(abcd.IsCS(FastHash.HashCS(lower), lower));
        Assert.False(abcd.IsCS(FastHash.HashCS(upper), upper));
        Assert.True(abcd.IsCI(FastHash.HashCI(lower), lower));
        Assert.True(abcd.IsCI(FastHash.HashCI(upper), upper));
    }

    [Fact]
    public void GeneratedType_abcde_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcde"u8;
        ReadOnlySpan<byte> upper = "ABCDE"u8;

        Assert.True(abcde.IsCS(FastHash.HashCS(lower), lower));
        Assert.False(abcde.IsCS(FastHash.HashCS(upper), upper));
        Assert.True(abcde.IsCI(FastHash.HashCI(lower), lower));
        Assert.True(abcde.IsCI(FastHash.HashCI(upper), upper));
    }

    [Fact]
    public void GeneratedType_abcdef_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcdef"u8;
        ReadOnlySpan<byte> upper = "ABCDEF"u8;

        Assert.True(abcdef.IsCS(FastHash.HashCS(lower), lower));
        Assert.False(abcdef.IsCS(FastHash.HashCS(upper), upper));
        Assert.True(abcdef.IsCI(FastHash.HashCI(lower), lower));
        Assert.True(abcdef.IsCI(FastHash.HashCI(upper), upper));
    }

    [Fact]
    public void GeneratedType_abcdefg_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcdefg"u8;
        ReadOnlySpan<byte> upper = "ABCDEFG"u8;

        Assert.True(abcdefg.IsCS(FastHash.HashCS(lower), lower));
        Assert.False(abcdefg.IsCS(FastHash.HashCS(upper), upper));
        Assert.True(abcdefg.IsCI(FastHash.HashCI(lower), lower));
        Assert.True(abcdefg.IsCI(FastHash.HashCI(upper), upper));
    }

    [Fact]
    public void GeneratedType_abcdefgh_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcdefgh"u8;
        ReadOnlySpan<byte> upper = "ABCDEFGH"u8;

        Assert.True(abcdefgh.IsCS(FastHash.HashCS(lower), lower));
        Assert.False(abcdefgh.IsCS(FastHash.HashCS(upper), upper));
        Assert.True(abcdefgh.IsCI(FastHash.HashCI(lower), lower));
        Assert.True(abcdefgh.IsCI(FastHash.HashCI(upper), upper));
    }

    [Fact]
    public void GeneratedType_abcdefghijklmnopqrst_CaseSensitivity()
    {
        ReadOnlySpan<byte> lower = "abcdefghijklmnopqrst"u8;
        ReadOnlySpan<byte> upper = "ABCDEFGHIJKLMNOPQRST"u8;

        Assert.True(abcdefghijklmnopqrst.IsCS(FastHash.HashCS(lower), lower));
        Assert.False(abcdefghijklmnopqrst.IsCS(FastHash.HashCS(upper), upper));
        Assert.True(abcdefghijklmnopqrst.IsCI(FastHash.HashCI(lower), lower));
        Assert.True(abcdefghijklmnopqrst.IsCI(FastHash.HashCI(upper), upper));
    }

    [Fact]
    public void GeneratedType_foo_bar_CaseSensitivity()
    {
        // foo_bar is interpreted as foo-bar
        ReadOnlySpan<byte> lower = "foo-bar"u8;
        ReadOnlySpan<byte> upper = "FOO-BAR"u8;

        Assert.True(foo_bar.IsCS(FastHash.HashCS(lower), lower));
        Assert.False(foo_bar.IsCS(FastHash.HashCS(upper), upper));
        Assert.True(foo_bar.IsCI(FastHash.HashCI(lower), lower));
        Assert.True(foo_bar.IsCI(FastHash.HashCI(upper), upper));
    }

    [Fact]
    public void GeneratedType_foo_bar_hyphen_CaseSensitivity()
    {
        // foo_bar_hyphen is explicitly "foo-bar"
        ReadOnlySpan<byte> lower = "foo-bar"u8;
        ReadOnlySpan<byte> upper = "FOO-BAR"u8;

        Assert.True(foo_bar_hyphen.IsCS(FastHash.HashCS(lower), lower));
        Assert.False(foo_bar_hyphen.IsCS(FastHash.HashCS(upper), upper));
        Assert.True(foo_bar_hyphen.IsCI(FastHash.HashCI(lower), lower));
        Assert.True(foo_bar_hyphen.IsCI(FastHash.HashCI(upper), upper));
    }

    [Fact]
    public void KeyNotificationTypeFastHash_MinMaxBytes_ReflectsActualLengths()
    {
        // Use reflection to find all nested types in KeyNotificationTypeFastHash
        var fastHashType = typeof(KeyNotificationTypeFastHash);
        var nestedTypes = fastHashType.GetNestedTypes(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        int? minLength = null;
        int? maxLength = null;

        foreach (var nestedType in nestedTypes)
        {
            // Look for the Length field (generated by FastHash source generator)
            var lengthField = nestedType.GetField("Length", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (lengthField != null && lengthField.FieldType == typeof(int))
            {
                var length = (int)lengthField.GetValue(null)!;

                if (minLength == null || length < minLength)
                {
                    minLength = length;
                }

                if (maxLength == null || length > maxLength)
                {
                    maxLength = length;
                }
            }
        }

        // Assert that we found at least some nested types with Length fields
        Assert.NotNull(minLength);
        Assert.NotNull(maxLength);

        // Assert that MinBytes and MaxBytes match the actual min/max lengths
        log.WriteLine($"MinBytes: {KeyNotificationTypeFastHash.MinBytes}, MaxBytes: {KeyNotificationTypeFastHash.MaxBytes}");
        Assert.Equal(KeyNotificationTypeFastHash.MinBytes, minLength.Value);
        Assert.Equal(KeyNotificationTypeFastHash.MaxBytes, maxLength.Value);
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
