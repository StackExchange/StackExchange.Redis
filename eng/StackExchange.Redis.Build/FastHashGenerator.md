# FastHashGenerator

Efficient matching of well-known short string tokens is a high-volume scenario, for example when matching RESP literals.

The purpose of this generator is to interpret inputs like:

``` c#
[FastHash] public static partial class bin { }
[FastHash] public static partial class f32 { }
```

Usually the token is inferred from the name; `[FastHash("real value")]` can be used if the token is not a valid identifier.
Underscore is replaced with hyphen, so a field called `my_token` has the default value `"my-token"`.
The generator demands *all* of `[FastHash] public static partial class`, and note that any *containing* types must
*also* be declared `partial`.

The output is of the form:

``` c#
static partial class bin
{
    public const int Length = 3;
    public const long Hash = 7235938;
    public static ReadOnlySpan<byte> U8 => @"bin"u8;
    public static string Text => @"bin";
    public static bool Is(long hash, in RawResult value) => hash == Hash && value.IsEqual(U8);
}
static partial class f32
{
    public const int Length = 3;
    public const long Hash = 3289958;
    public static ReadOnlySpan<byte> U8 => @"f32"u8;
    public static string Text => @"f32";
    public static bool Is(long hash, in RawResult value) => hash == Hash && value.IsEqual(U8);
}
```

(this API is strictly an internal implementation detail, and can change at any time)

This generated code allows for fast, efficient, and safe matching of well-known tokens, for example:

``` c#
var key = ...
var hash = key.Hash64();
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
must also perform a sequence equality check - the `Is` convenient method validates both hash and equality.

Note that `switch` requires `const` values, hence why we use generated *types* rather than partial-properties
that emit an instance with the known values. Also, the `"..."u8` syntax emits a span which is awkward to store, but
easy to return via a property.~~~~



