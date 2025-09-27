// using System;
// using System.Buffers;
// using System.Diagnostics;
//
// namespace Resp;
//
// /// <summary>
// /// Utility methods for <see cref="RespReader"/>s.
// /// </summary>
// internal static class RespReaderExtensions
// {
//     public static RedisValue ReadRedisValue(in this RespReader reader)
//     {
//         reader.DemandScalar();
//         if (reader.IsNull) return RedisValue.Null;
//
//         var len = reader.ScalarLength();
//         switch (reader.Prefix)
//         {
//             case RespPrefix.Boolean:
//                 return reader.ReadBoolean();
//             case RespPrefix.Integer:
//                 return reader.ReadInt64();
//             case RespPrefix.Double:
//                 return reader.ReadDouble();
//         }
//
//         if (len == 0) return RedisValue.EmptyString;
//
//         // try to be efficient with obvious numbers and short strings
//         if (reader.TryReadInt64(out var i64))
//         {
//             return i64;
//         }
//
//         if (reader.TryReadDouble(out var f64, allowTokens: false))
//         {
//             return f64;
//         }
//
//         if (reader.TryReadShortAscii(out var s))
//         {
//             return s;
//         }
//
//         // otherwise, copy out the blob
//         var result = new byte[len];
//         int actual = reader.CopyTo(result);
//         Debug.Assert(actual == len);
//         return result;
//     }
//
//     public static RedisKey ReadRedisKey(in this RespReader reader)
//     {
//         reader.DemandScalar();
//         if (reader.IsNull) return RedisKey.Null;
//
//         var len = reader.ScalarLength();
//         if (len == 0) return "";
//
//         if (reader.TryReadShortAscii(out var s))
//         {
//             return s;
//         }
//
//         // copy out the blob
//         var result = new byte[len];
//         int actual = reader.CopyTo(result);
//         Debug.Assert(actual == len);
//         return result;
//     }
//
//     /*
//
//     /// <summary>
//     /// Interpret a scalar value as a <see cref="LeasedString"/> value.
//     /// </summary>
//     public static LeasedString ReadLeasedString(in this RespReader reader)
//     {
//         if (reader.TryGetSpan(out var span)) return reader.IsNull ? default : new LeasedString(span);
//
//         var len = reader.ScalarLength();
//         var result = new LeasedString(len, out var memory);
//         int actual = reader.CopyTo(memory.Span);
//         Debug.Assert(actual == len);
//         return result;
//     }
//
//     /// <summary>
//     /// Interpret an aggregate value as a <see cref="LeasedStrings"/> value.
//     /// </summary>
//     public static LeasedStrings ReadLeasedStrings(in this RespReader reader)
//     {
//         Debug.Assert(reader.IsAggregate, "should have already checked for aggregate");
//         reader.DemandAggregate();
//         if (reader.IsNull) return default;
//
//         int count = 0, bytes = 0;
//         foreach (var child in reader.AggregateChildren())
//         {
//             count++;
//             bytes += child.ScalarLength();
//         }
//         if (count == 0) return LeasedStrings.Empty;
//
//         var builder = new LeasedStrings.Builder(count, bytes);
//         foreach (var child in reader.AggregateChildren())
//         {
//             if (child.IsNull)
//             {
//                 builder.AddNull();
//             }
//             else
//             {
//                 var len = child.ScalarLength();
//                 var span = builder.Add(len);
//                 child.CopyTo(span);
//             }
//         }
//         return builder.Create();
//     }
//
//     /// <summary>
//     /// Indicates whether the given value is an byte match.
//     /// </summary>
//     public static bool Is(in this RespReader reader, in SimpleString value)
//     {
//         if (value.TryGetBytes(span: out var span))
//         {
//             return reader.Is(span) & reader.IsNull == value.IsNull;
//         }
//
//         var len = value.GetByteCount();
//         var oversized = ArrayPool<byte>.Shared.Rent(len);
//         var actual = value.CopyTo(oversized);
//         Debug.Assert(actual == len);
//         var result = reader.Is(new ReadOnlySpan<byte>(oversized, 0, len));
//         ArrayPool<byte>.Shared.Return(oversized);
//         return result;
//     }
//     */
// }
