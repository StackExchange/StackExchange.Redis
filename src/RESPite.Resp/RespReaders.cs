//using System;

//namespace RESPite.Resp;

///// <summary>
///// Provides common RESP reader implementations
///// </summary>
//public static class RespReaders
//{
//    private static readonly Impl common = new();
//    /// <summary>
//    /// Reads <see cref="String"/> payloads
//    /// </summary>
//    public static IRespReader<Empty, string?> String => common;
//    /// <summary>
//    /// Reads <see cref="Int32"/> payloads
//    /// </summary>
//    public static IRespReader<Empty, int> Int32 => common;
//    /// <summary>
//    /// Reads 'OK' acknowledgements
//    /// </summary>
//    public static IRespReader<Empty, Empty> OK => common;
//    internal sealed class Impl : IRespReader<Empty, Empty>, IRespReader<Empty, string?>, IRespReader<Empty, int>
//    {
//        string? IRespReader<Empty, string?>.Read(in Empty state, ref RespReader reader) => reader.ReadString();
//        Empty IRespReader<Empty, Empty>.Read(in Empty state, ref RespReader reader)
//        {
//            if (!reader.IsOK()) Throw();
//            return default;
//            static void Throw()
//            => throw new InvalidOperationException("Did not receive expected response: '+OK'");
//        }
//        int IRespReader<Empty, int>.Read(in Empty state, ref RespReader reader) => reader.ReadInt32();
//    }
//}
