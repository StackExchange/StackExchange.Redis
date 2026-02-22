# AsciiHash

Efficient matching of well-known short string tokens is a high-volume scenario, for example when matching RESP literals.

The purpose of this generator is to efficiently interpret input tokens like `bin`, `f32`, etc - whether as byte or character data.

There are two ways of using this tool:

## Isolated literals

When using individual tokens, a `static partial class` can be used to generate helpers:

``` c#
[AsciiHash] public static partial class bin { }
[AsciiHash] public static partial class f32 { }
```

Usually the token is inferred from the name; `[AsciiHash("real value")]` can be used if the token is not a valid identifier.
Underscores are replaced with hyphens, so a field called `my_token` has the default value `"my-token"`.
The generator demands *all* of `[AsciiHash] public static partial class`, and note that any *containing* types must
*also* be declared `partial`.

The output is of the form:

``` c#
static partial class bin
{
    public const int Length = 3;
    public const long HashCS = ...
    public const long HashCI = ...
    public static ReadOnlySpan<byte> U8 => @"bin"u8;
    public static string Text => @"bin";
    public static bool IsCI(long hash, in RawResult value) => ...
    public static bool IsCS(long hash, in ReadOnlySpan<byte> value) => ...
}
```
The `CS` and `CI` are case-sensitive and case-insensitive tools, respectively.

(this API is strictly an internal implementation detail, and can change at any time)

This generated code allows for fast, efficient, and safe matching of well-known tokens, for example:

``` c#
var key = ...
var hash = key.HashCS();
switch (key.Length)
{
    case bin.Length when bin.Is(hash, key):
        // handle bin
        break;
    case f32.Length when f32.Is(hash, key):
        // handle f32
        break;
}
```

The switch on the `Length` is optional, but recommended - these low values can often be implemented (by the compiler)
as a simple jump-table, which is very fast. However, switching on the hash itself is also valid. All hash matches
must also perform a sequence equality check - the `Is(hash, value)` convenience method validates both hash and equality.

Note that `switch` requires `const` values, hence why we use generated *types* rather than partial-properties
that emit an instance with the known values. Also, the `"..."u8` syntax emits a span which is awkward to store, but
easy to return via a property.

## Enum parsing

When identifying multiple values, an `enum` may be more convenient. Consider:

``` c#
[AsciiHash]
public static partial bool TryParse(ReadOnlySpan<byte> value, out SomeEnum value);
```

This generates an efficient parser; inputs can be common `byte` or `char` types. Case sensitivity
is controlled by the optional `CaseSensitive` property on the attribute, or via a 3rd (`bool`) parameter
bbon the method, i.e.

``` c#
[AsciiHash(CaseSensitive = false)]
public static partial bool TryParse(ReadOnlySpan<byte> value, out SomeEnum value);
```

or

``` c#
[AsciiHash]
public static partial bool TryParse(ReadOnlySpan<byte> value, out SomeEnum value, bool caseSensitive = true);
```

Individual enum members can also be marked with `[AsciiHash("token value")]` to override the token payload.