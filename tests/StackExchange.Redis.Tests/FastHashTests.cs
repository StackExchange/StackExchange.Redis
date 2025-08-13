using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

public class FastHashTests(ITestOutputHelper log)
{
    [Fact]
    public void CaseInsensitiveHash()
    {
        Assert.NotEqual("abc"u8.Hash64(), "ABC"u8.Hash64());
        Assert.Equal("abc"u8.Hash64CI(), "ABC"u8.Hash64CI());
    }

    [Fact]
    public void CaseSensitiveEquals()
    {
        Assert.True("ABC"u8.SequenceEqual("ABC"u8), "same");
        Assert.False("abc"u8.SequenceEqual("ABC"u8), "off-by-case");
    }

    [Theory]
    [MemberData(nameof(HashLiterals))]
    public void Hash64(string type, string fieldName, long declaredHash, byte[] expectedBytes, byte[]? actualBytes, int? length)
    {
        Assert.NotNull(actualBytes); // missing the _u8 field, which must exist for corresponding equality test
        Assert.Equal(expectedBytes, actualBytes);
        var actualHash = FastHash.Hash64(actualBytes);
        log.WriteLine($"{nameof(FastHash.Hash64)}: {type} {fieldName}: {actualHash}");
        Assert.Equal(declaredHash, actualHash);

        if (length.HasValue)
        {
            Assert.Equal(length.Value, actualBytes.Length);
        }

        // check equality between hash implementations
#pragma warning disable CS0618 // Type or member is obsolete
        var tmp = FastHash.Hash64Unsafe(actualBytes);
        log.WriteLine($"{nameof(FastHash.Hash64Unsafe)}: {tmp}");
        Assert.Equal(actualHash, tmp);

        tmp = FastHash.Hash64Fallback(actualBytes);
        log.WriteLine($"{nameof(FastHash.Hash64Fallback)}: {tmp}");
        Assert.Equal(actualHash, tmp);
#pragma warning restore CS0618 // Type or member is obsolete
    }

    public static IEnumerable<object?[]> HashLiterals()
    {
        var pending = new Queue<Type>();
        pending.Enqueue(typeof(FastHash));
        while (pending.TryDequeue(out var type))
        {
            // dive into nested types
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                pending.Enqueue(nested);
            }

            // enforce the length if the type is named "_{N}"
            object? length = null;
            if (type.Name.StartsWith("_") && int.TryParse(type.Name.Substring(1), NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out int i))
            {
                length = i;
            }

            // check for relevant fields
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (field.Name == "CaseMask" && type == typeof(FastHash)) continue; // not a hash

                if (field.IsLiteral && field.FieldType == typeof(long))
                {
                    // the expected bytes match fieldName (using - for _) unless there's a [Literal] on that field
                    var expectedBytes = Encoding.UTF8.GetBytes(
                        field.GetCustomAttribute<FastHash.LiteralAttribute>()?.Token
                        ?? field.Name.Replace("_", "-"));
                    var declaredHash = (long)field.GetRawConstantValue()!;

                    byte[]? actualBytes = null;
                    var u8Prop = type.GetProperty(field.Name + "_u8");
                    if (u8Prop != null && u8Prop.PropertyType == typeof(ReadOnlySpan<byte>))
                    {
                        actualBytes = ReadBytes(u8Prop);
                    }
                    yield return [type.Name, field.Name, declaredHash, expectedBytes, actualBytes, length];
                }
            }
        }
    }

    private static readonly MethodInfo ReadOnlySpanToArray = typeof(ReadOnlySpan<byte>).GetMethod(nameof(ReadOnlySpan<byte>.ToArray))!;
    private static byte[]? ReadBytes(PropertyInfo prop)
    {
        var getter = prop.GetMethod;
        if (getter is not { IsStatic: true } || getter.ReturnType != typeof(ReadOnlySpan<byte>)) return null;

        // we can't use prop.GetValue() because it's a ref struct (cannot be boxed); instead, we need to use ref-emit
        DynamicMethod dm = new DynamicMethod(prop.Name, typeof(byte[]), null, typeof(FastHashTests).Module);
        ILGenerator il = dm.GetILGenerator();
        var loc = il.DeclareLocal(typeof(ReadOnlySpan<byte>));
        il.EmitCall(OpCodes.Call, getter, null);
        il.Emit(OpCodes.Stloc, loc);
        il.Emit(OpCodes.Ldloca, loc);
        il.EmitCall(OpCodes.Call, ReadOnlySpanToArray, null);
        il.Emit(OpCodes.Ret);
        return (byte[]?)dm.Invoke(null, null);
    }
}
