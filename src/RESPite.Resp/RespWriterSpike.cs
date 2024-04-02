//using RESPite.Buffers.Internal;
//using RESPite.Internal;
//using System;
//using System.Buffers;
//using System.Diagnostics;
//using System.Runtime.CompilerServices;
//using System.Runtime.InteropServices;
//using System.Text;

//namespace RESPite.Resp;

///// <summary>
///// Low-level RESP writing API
///// </summary>
//public ref struct RespWriter
//{
//    private BufferCore _buffer;
//    private readonly int _preambleReservation;
//    private int _argCountIncludingCommand, _argIndexIncludingCommand;

//    internal static RespWriter Create(SlabManager? slabManager = null, int preambleReservation = 64)
//        => new(slabManager ?? SlabManager.Ambient, preambleReservation);

//    private RespWriter(SlabManager slabManager, int preambleReservation)
//    {
//        _preambleReservation = preambleReservation;
//        _argCountIncludingCommand = _argIndexIncludingCommand = 0;
//        _buffer = new(slabManager);
//        _buffer.Commit(preambleReservation);
//    }



//    private const int NullLength = 5; // $-1\r\n 

//    internal void Recycle() => _buffer.Dispose();

//    internal static readonly UTF8Encoding UTF8 = new(false);

//    /// <summary>
//    /// Write a RESP command header
//    /// </summary>
//    /// <param name="command">The command to write</param>
//    /// <param name="argCount">The number of additional parameters expected</param>
//    public void WriteCommand(string command, int argCount) => WriteCommand(command.AsSpan(), argCount);

//    private const int MAX_UTF8_BYTES_PER_CHAR = 4, MAX_CHARS_FOR_STACKALLOC_ENCODE = 64,
//        ENCODE_STACKALLOC_BYTES = MAX_CHARS_FOR_STACKALLOC_ENCODE * MAX_UTF8_BYTES_PER_CHAR;

//    /// <summary>
//    /// Write a RESP command header
//    /// </summary>
//    /// <param name="command">The command to write</param>
//    /// <param name="argCount">The number of additional parameters expected</param>
//    public void WriteCommand(scoped ReadOnlySpan<char> command, int argCount)
//    {
//        if (command.Length <= MAX_CHARS_FOR_STACKALLOC_ENCODE)
//        {
//            WriteCommand(Utf8Encode(command, stackalloc byte[ENCODE_STACKALLOC_BYTES]), argCount);
//        }
//        else
//        {
//            WriteCommandSlow(ref this, command, argCount);
//        }

//        static void WriteCommandSlow(ref RespWriter @this, scoped ReadOnlySpan<char> command, int argCount)
//        {
//            @this.WriteCommand(Utf8EncodeLease(command, out var lease), argCount);
//            ArrayPool<byte>.Shared.Return(lease);
//        }
//    }

//    private static unsafe ReadOnlySpan<byte> Utf8Encode(scoped ReadOnlySpan<char> source, Span<byte> target)
//    {
//        return target.Slice(0, UTF8.GetBytes(source, target));
//    }
//    private static ReadOnlySpan<byte> Utf8EncodeLease(scoped ReadOnlySpan<char> value, out byte[] arr)
//    {
//        arr = ArrayPool<byte>.Shared.Rent(MAX_UTF8_BYTES_PER_CHAR * value.Length);
//        return new ReadOnlySpan<byte>(arr, 0, UTF8.GetBytes(value, arr));
//    }
//    internal readonly void AssertFullyWritten()
//    {
//        if (_argCountIncludingCommand != _argIndexIncludingCommand) Throw(_argIndexIncludingCommand, _argCountIncludingCommand);

//        static void Throw(int count, int total) => throw new InvalidOperationException($"Not all command arguments ({count - 1} of {total - 1}) have been written");
//    }

//    /// <summary>
//    /// Write a RESP command header
//    /// </summary>
//    /// <param name="command">The command to write</param>
//    /// <param name="argCount">The number of additional parameters expected</param>
//    public void WriteCommand(scoped ReadOnlySpan<byte> command, int argCount)
//    {
//        if (_argCountIncludingCommand > 0) ThrowCommandAlreadyWritten();
//        if (command.IsEmpty) ThrowEmptyCommand();
//        if (argCount < 0) ThrowNegativeArgs();
//        _argCountIncludingCommand = argCount + 1;
//        _argIndexIncludingCommand = 1;

//        var payloadAndFooter = command.Length + 2;

//        // optimize for single buffer-fetch path
//        var worstCase = MaxProtocolBytesIntegerInt32 + MaxProtocolBytesIntegerInt32 + command.Length + 2;
//        if (_buffer.TryGetWritableSpan(worstCase, out var span))
//        {
//            ref byte head = ref MemoryMarshal.GetReference(span);
//            var header = WriteCountPrefix(RespPrefix.Array, _argCountIncludingCommand, span);
//#if NETCOREAPP3_1_OR_GREATER
//            header += WriteCountPrefix(RespPrefix.BulkString, command.Length,
//                MemoryMarshal.CreateSpan(ref Unsafe.Add(ref head, header), MaxProtocolBytesIntegerInt32));
//            command.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref head, header), command.Length));
//#else
//            header += WriteCountPrefix(RespPrefix.BulkString, command.Length, span.Slice(header));
//            command.CopyTo(span.Slice(header));
//#endif

//            Unsafe.WriteUnaligned(ref Unsafe.Add(ref head, header + command.Length), CrLf);
//            _buffer.Commit(header + command.Length + 2);
//            return; // yay!
//        }

//        // slow path, multiple buffer fetches
//        WriteCountPrefix(RespPrefix.Array, _argCountIncludingCommand);
//        WriteCountPrefix(RespPrefix.BulkString, command.Length);
//        WriteRaw(command);
//        WriteRaw(CrlfBytes);


//        static void ThrowCommandAlreadyWritten() => throw new InvalidOperationException(nameof(WriteCommand) + " can only be called once");
//        static void ThrowEmptyCommand() => throw new ArgumentOutOfRangeException(nameof(command), "command cannot be empty");
//        static void ThrowNegativeArgs() => throw new ArgumentOutOfRangeException(nameof(argCount), "argCount cannot be negative");
//    }

//    private static int WriteCountPrefix(RespPrefix prefix, int count, Span<byte> target)
//    {
//        var len = Format.FormatInt32(count, target.Slice(1)); // we only want to pay for this one slice
//        if (target.Length < len + 3) Throw();
//        ref byte head = ref MemoryMarshal.GetReference(target);
//        head = (byte)prefix;
//        Unsafe.WriteUnaligned(ref Unsafe.Add(ref head, len + 1), CrLf);
//        return len + 3;

//        static void Throw() => throw new InvalidOperationException("Insufficient buffer space to write count prefix");
//    }

//    private void WriteNullString() // private because I don't think this is allowed in client streams? check
//        => WriteRaw("$-1\r\n"u8);

//    private void WriteEmptyString() // private because I don't think this is allowed in client streams? check
//        => WriteRaw("$0\r\n\r\n"u8);

//    internal void WriteRaw(scoped ReadOnlySpan<byte> value)
//    {
//        while (!value.IsEmpty)
//        {
//            var target = _buffer.GetWritableTail().Span;
//            Debug.Assert(!target.IsEmpty, "need something!");

//            if (target.Length >= value.Length)
//            {
//                // it all fits
//                value.CopyTo(target);
//                _buffer.Commit(value.Length);
//                return;
//            }

//            // write what we can
//            value.Slice(target.Length).CopyTo(target);
//            _buffer.Commit(target.Length);
//            value = value.Slice(target.Length);
//        }
//    }

//    private void AddArg()
//    {
//        if (_argIndexIncludingCommand >= _argCountIncludingCommand) ThrowAllWritten(_argCountIncludingCommand);
//        _argIndexIncludingCommand++;

//        static void ThrowAllWritten(int advertised) => throw new InvalidOperationException($"All command arguments ({advertised - 1}) have already been written");
//    }

//    /// <summary>
//    /// Write a bulk string value
//    /// </summary>
//    public void WriteValue(scoped ReadOnlySpan<byte> value)
//    {
//        AddArg();
//        if (value.IsEmpty)
//        {
//            WriteEmptyString();
//            return;
//        }
//        // optimize for fitting everything into a single buffer-fetch
//        var payloadAndFooter = value.Length + 2;
//        var worstCase = MaxProtocolBytesIntegerInt32 + payloadAndFooter;
//        if (_buffer.TryGetWritableSpan(worstCase, out var span))
//        {
//            ref byte head = ref MemoryMarshal.GetReference(span);
//            var header = WriteCountPrefix(RespPrefix.BulkString, value.Length, span);
//#if NETCOREAPP3_1_OR_GREATER
//            value.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref head, header), payloadAndFooter));
//#else
//            value.CopyTo(span.Slice(header));
//#endif
//            Unsafe.WriteUnaligned(ref Unsafe.Add(ref head, header + value.Length), CrLf);
//            _buffer.Commit(header + payloadAndFooter);
//            return; // yay!
//        }

//        // slow path - involves multiple buffer fetches
//        WriteCountPrefix(RespPrefix.BulkString, value.Length);
//        WriteRaw(value);
//        WriteRaw(CrlfBytes);
//    }

//    private void WriteCountPrefix(RespPrefix prefix, int count)
//    {
//        Span<byte> buffer = stackalloc byte[MaxProtocolBytesIntegerInt32];
//        WriteRaw(buffer.Slice(0, WriteCountPrefix(prefix, count, buffer)));
//    }

//    internal static readonly ushort CrLf = BitConverter.IsLittleEndian ? (ushort)0x0A0D : (ushort)0x0D0A; // see: ASCII

//    internal static ReadOnlySpan<byte> CrlfBytes => "\r\n"u8;

//    /// <summary>
//    /// Write a bulk string value
//    /// </summary>
//    public void WriteValue(scoped ReadOnlySpan<char> value)
//    {
//        if (value.Length == 0)
//        {
//            AddArg();
//            WriteEmptyString();
//        }
//        else if (value.Length <= MAX_CHARS_FOR_STACKALLOC_ENCODE)
//        {
//            WriteValue(Utf8Encode(value, stackalloc byte[ENCODE_STACKALLOC_BYTES]));
//        }
//        else
//        {
//            WriteValue(Utf8EncodeLease(value, out var lease));
//            ArrayPool<byte>.Shared.Return(lease);
//        }
//    }

//    /// <summary>
//    /// Write a bulk string value
//    /// </summary>
//    public void WriteValue(string value)
//    {
//        if (value is null)
//        {
//            AddArg();
//            WriteNullString();
//        }
//        else WriteValue(value.AsSpan());
//    }

//    internal RequestBuffer Detach() => new RequestBuffer(_buffer.Detach(), _preambleReservation);
//}
