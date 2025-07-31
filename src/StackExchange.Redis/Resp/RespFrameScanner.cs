// using System.Buffers;
// using System.Diagnostics;
// using static RESPite.Resp.RespConstants;
// namespace RESPite.Resp;
//
// /// <summary>
// /// Scans RESP frames.
// /// </summary>.
// internal sealed class RespFrameScanner : IFrameSacanner<ScanState>, IFrameValidator
// {
//     /// <summary>
//     /// Gets a frame scanner for RESP2 request/response connections, or RESP3 connections.
//     /// </summary>
//     public static RespFrameScanner Default { get; } = new(false);
//
//     /// <summary>
//     /// Gets a frame scanner that identifies RESP2 pub/sub messages.
//     /// </summary>
//     public static RespFrameScanner Subscription { get; } = new(true);
//     private RespFrameScanner(bool pubsub) => _pubsub = pubsub;
//     private readonly bool _pubsub;
//
//     void IFrameScanner<ScanState>.OnBeforeFrame(ref ScanState state, ref FrameScanInfo info)
//     {
//         state = ScanState.Create(_pubsub);
//         info.ReadHint = 3; // minimum legal RESP frame is: _\r\n
//     }
//
//     private static readonly uint FastNull = UnsafeCpuUInt32("_\r\n\0"u8),
//         SingleCharScalarMask = CpuUInt32(0xFF00FFFF),
//         SingleDigitInteger = UnsafeCpuUInt32(":\0\r\n"u8),
//         EitherBoolean = UnsafeCpuUInt32("#\0\r\n"u8),
//         FirstThree = CpuUInt32(0xFFFFFF00);
//     private static readonly ulong OK = UnsafeCpuUInt64("+OK\r\n\0\0\0"u8),
//         PONG = UnsafeCpuUInt64("+PONG\r\n\0"u8),
//         DoubleCharScalarMask = CpuUInt64(0xFF0000FFFF000000),
//         DoubleDigitInteger = UnsafeCpuUInt64(":\0\0\r\n"u8),
//         FirstFive = CpuUInt64(0xFFFFFFFFFF000000),
//         FirstSeven = CpuUInt64(0xFFFFFFFFFFFFFF00);
//
//     private const OperationStatus UseReader = (OperationStatus)(-1);
//     private static OperationStatus TryFastRead(ReadOnlySpan<byte> data, ref FrameScanInfo info)
//     {
//         // use silly math to detect the most common short patterns without needing
//         // to access a reader, or use indexof etc; handles:
//         // +OK\r\n
//         // +PONG\r\n
//         // :N\r\n for any single-digit N (integer)
//         // :NN\r\n for any double-digit N (integer)
//         // #N\r\n for any single-digit N (boolean)
//         // _\r\n (null)
//         uint hi, lo;
//         switch (data.Length)
//         {
//             case 0:
//             case 1:
//             case 2:
//                 return OperationStatus.NeedMoreData;
//             case 3:
//                 hi = (((uint)UnsafeCpuUInt16(data)) << 16) | (((uint)UnsafeCpuByte(data, 2)) << 8);
//                 break;
//             default:
//                 hi = UnsafeCpuUInt32(data);
//                 break;
//         }
//         if ((hi & FirstThree) == FastNull)
//         {
//             info.BytesRead = 3;
//             return OperationStatus.Done;
//         }
//
//         var masked = hi & SingleCharScalarMask;
//         if (masked == SingleDigitInteger || masked == EitherBoolean)
//         {
//             info.BytesRead = 4;
//             return OperationStatus.Done;
//         }
//
//         switch (data.Length)
//         {
//             case 3:
//                 return OperationStatus.NeedMoreData;
//             case 4:
//                 return UseReader;
//             case 5:
//                 lo = ((uint)data[4]) << 24;
//                 break;
//             case 6:
//                 lo = ((uint)UnsafeCpuUInt16(data, 4)) << 16;
//                 break;
//             case 7:
//                 lo = ((uint)UnsafeCpuUInt16(data, 4)) << 16 | ((uint)UnsafeCpuByte(data, 6)) << 8;
//                 break;
//             default:
//                 lo = UnsafeCpuUInt32(data, 4);
//                 break;
//         }
//         var u64 = BitConverter.IsLittleEndian ? ((((ulong)lo) << 32) | hi) : ((((ulong)hi) << 32) | lo);
//         if (((u64 & FirstFive) == OK) | ((u64 & DoubleCharScalarMask) == DoubleDigitInteger))
//         {
//             info.BytesRead = 5;
//             return OperationStatus.Done;
//         }
//         if ((u64 & FirstSeven) == PONG)
//         {
//             info.BytesRead = 7;
//             return OperationStatus.Done;
//         }
//         return UseReader;
//     }
//     OperationStatus IFrameScanner<ScanState>.TryRead(ref ScanState state, in ReadOnlySequence<byte> data, ref FrameScanInfo info)
//     {
//         if (!_pubsub & info.BytesRead == 0 & data.IsSingleSegment)
//         {
// #if NETCOREAPP3_1_OR_GREATER
//             var status = TryFastRead(data.FirstSpan, ref info);
// #else
//             var status = TryFastRead(data.First.Span, ref info);
// #endif
//             if (status != UseReader) return status;
//         }
//
//         return TryReadViaReader(ref state, in data, ref info);
//
//         static OperationStatus TryReadViaReader(ref ScanState state, in ReadOnlySequence<byte> data, ref FrameScanInfo info)
//         {
//             var reader = new RespReader(in data);
//             var complete = state.TryRead(ref reader, out var consumed);
//             info.BytesRead += consumed;
//             if (complete)
//             {
//                 info.IsOutOfBand = state.IsOutOfBand;
//                 return OperationStatus.Done;
//             }
//             return OperationStatus.NeedMoreData;
//         }
//     }
//
//     void IFrameScanner<ScanState>.Trim(ref ScanState state, ref ReadOnlySequence<byte> data, ref FrameScanInfo info)
//     {
//     }
//
//     void IFrameValidator.Validate(in ReadOnlySequence<byte> message)
//     {
//         if (message.IsEmpty) Throw("Empty RESP frame");
//         RespReader reader = new(in message);
//         reader.MoveNext(RespPrefix.Array);
//         reader.DemandNotNull();
//         if (reader.IsStreaming) Throw("Streaming is not supported in this context");
//         var count = reader.AggregateLength();
//         for (int i = 0; i < count; i++)
//         {
//             reader.MoveNext(RespPrefix.BulkString);
//             reader.DemandNotNull();
//             if (reader.IsStreaming) Throw("Streaming is not supported in this context");
//
//             if (i == 0 && reader.ScalarIsEmpty()) Throw("command must be non-empty");
//         }
//         reader.DemandEnd();
//
//         static void Throw(string message) => throw new InvalidOperationException(message);
//     }
// }
