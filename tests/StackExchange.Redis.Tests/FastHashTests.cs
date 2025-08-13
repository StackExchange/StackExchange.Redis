using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

public class FastHashTests(ITestOutputHelper log)
{
    [Theory]
    [MemberData(nameof(HashLiterals))]
    public void Hash64(string type, string fieldName, long declaredHash, byte[] expectedBytes, byte[]? actualBytes)
    {
        Assert.NotNull(actualBytes); // missing the _u8 field, which must exist for corresponding equality test
        Assert.Equal(expectedBytes, actualBytes);
        var actualHash = FastHash.Hash64(actualBytes);
        log.WriteLine($"Hash64: {type} {fieldName}: {actualHash}");
        Assert.Equal(declaredHash, actualHash);

        // check equality between hash implementations
#pragma warning disable CS0618 // Type or member is obsolete
        var tmp = FastHash.Hash64Unsafe(actualBytes);
        log.WriteLine($"Hash64Unsafe: {tmp}");
        Assert.Equal(actualHash, tmp);

        tmp = FastHash.Hash64Fallback(actualBytes);
        log.WriteLine($"Hash64Fallback: {tmp}");
        Assert.Equal(actualHash, tmp);
#pragma warning restore CS0618 // Type or member is obsolete
    }

    public static IEnumerable<object?[]> HashLiterals()
    {
        foreach (var type in typeof(FastHash).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (field.IsLiteral && field.FieldType == typeof(long))
                {
                    // the expected bytes match fieldName unless there's a [DisplayName] on that field
                    var expectedBytes = Encoding.UTF8.GetBytes(
                        field.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                        ?? field.Name);
                    var declaredHash = (long)field.GetRawConstantValue()!;

                    byte[]? actualBytes = null;
                    var u8Prop = type.GetProperty(field.Name + "_u8");
                    if (u8Prop != null && u8Prop.PropertyType == typeof(ReadOnlySpan<byte>))
                    {
                        actualBytes = ReadBytes(u8Prop);
                    }
                    yield return [type.Name, field.Name, declaredHash, expectedBytes, actualBytes];
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
