using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RESPite.Internal;
using RESPite.Messages;
using RESPite.Resp.Readers;

namespace RESPite.Resp.Writers;

/// <summary>
/// Common shared <see cref="IRespWriter{TRequest}"/> implementation.
/// </summary>
/// <remarks>Optionally allows a pre-computed pinned prefix to be supplied; otherwise, it is computed at construction.</remarks>
public abstract class CommandWriter
{
    private int _prefixLength;
    private
#if !NET6_0_OR_GREATER
        readonly
#endif
        unsafe void* _pinnedPrefix;
    private byte[]? _lazyPrefix;

    /// <summary>
    /// Represents an inactive writer for type <typeparamref name="TRequest"/>.
    /// </summary>
    public static IRespWriter<TRequest> Disabled<TRequest>() => DisabledWriter<TRequest>.Instance;

    private sealed class DisabledWriter<TRequest> : IRespWriter<TRequest>
    {
        public static readonly DisabledWriter<TRequest> Instance = new();

        IRespWriter<TRequest> IRespWriter<TRequest>.WithAlias(string command)
            => string.IsNullOrWhiteSpace(command) ? this : throw new InvalidOperationException("This command is disabled (aliased to empty).");
        void IRespWriter<TRequest>.Write(in TRequest request, ref RespWriter writer) => throw new InvalidOperationException("This command is disabled (aliased to empty).");
        void IWriter<TRequest>.Write(in TRequest request, IBufferWriter<byte> target) => throw new InvalidOperationException("This command is disabled (aliased to empty).");

        public override string ToString() => "(disabled)";
    }

    /// <summary>
    /// The number of parameters associated with this command, if it is fixed, or <c>-1</c> otherwise. Note that this is <b>excluding</b> the <see cref="Command"/>, i.e.
    /// a RESP command such as <c>"GET mykey</c> would report <c>1</c> here.
    /// </summary>
    protected int ArgCount { get; }

    /// <summary>
    /// The command associated with this writer.
    /// </summary>
    public string Command { get; }

    /// <inheritdoc />
    public override string ToString() => Command;

    /// <summary>
    /// Create a new instance, precomputing a prefix for the specified <paramref name="command"/>.
    /// </summary>
    public CommandWriter(string command, int argCount) : this(command, argCount, default) { }

    /// <summary>
    /// Create a new instance.
    /// </summary>
    /// <remarks>If <paramref name="pinnedPrefix"/> is supplied, it <b>MUST</b> be externally pinned, for example a <c>"..."u8</c> literal.</remarks>
    internal unsafe CommandWriter(string command, int argCount, ReadOnlySpan<byte> pinnedPrefix)
    {
        ArgCount = argCount;
        if (string.IsNullOrWhiteSpace(command)) ThrowEmptyCommand();
        Command = command.Trim();
        if (!pinnedPrefix.IsEmpty)
        {
            _pinnedPrefix = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(pinnedPrefix));
            _prefixLength = pinnedPrefix.Length;
            _lazyPrefix = null;
        }
        DebugVerifyPrefix();

        static void ThrowEmptyCommand() => throw new ArgumentException($"The command cannot be empty; for disabled commands, use {nameof(CommandWriter)}.{nameof(CommandWriter.Disabled)}", nameof(command));
    }

    private unsafe byte[] CreatePrefix()
    {
        static void ThrowNotFixedLength() => throw new InvalidOperationException($"This command does not have fixed length; uou must override {nameof(CommandWriter<int>.Write)} appropriately.");

        if (ArgCount < 0) ThrowNotFixedLength();

        // *XX\r\n$YY\r\nCOMMAND\r\n : length of command, plus two integers, plus 8 symbols
        Span<byte> buffer = stackalloc byte[Constants.UTF8.GetMaxByteCount(Command.Length)
            + (2 * Constants.MaxRawBytesInt32) + 8];
        var writer = new RespWriter(buffer);
        writer.WriteArray(ArgCount + 1);
        writer.WriteBulkString(Command);
        _prefixLength = writer.IndexInCurrentBuffer;
        buffer = buffer.Slice(0, _prefixLength);

        Debug.Assert(_prefixLength >= (10 + Command.Length), "suspicious RESP command");
#if NET6_0_OR_GREATER
        // might as well generate on the pinned heap and use the raw approach
        var tmp = GC.AllocateUninitializedArray<byte>(_prefixLength, pinned: true);
        buffer.CopyTo(tmp);
        _pinnedPrefix = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(tmp));
        _lazyPrefix = tmp;
#else
        _lazyPrefix = buffer.ToArray();
#endif
        return _lazyPrefix;
    }

    /// <summary>
    /// Gets the precomputed prefix for this command.
    /// </summary>
    /// <remarks>Optionally allows a pre-computed pinned prefix to be supplied; otherwise, it is computed at construction.</remarks>
    protected unsafe ReadOnlySpan<byte> CommandAndArgCount => _pinnedPrefix is not null ? new ReadOnlySpan<byte>(_pinnedPrefix, _prefixLength) : (_lazyPrefix ?? CreatePrefix());

    [Conditional("DEBUG")]
    private void DebugVerifyPrefix()
    {
        if (ArgCount < 0) return; // dynamic size; no fixed preamble to verify
        try
        {
            var span = CommandAndArgCount;
            var reader = new RespReader(span);
            reader.ReadNextAggregate();
            reader.Demand(RespPrefix.Array);
            var len = reader.ChildCount;
            if (len != ArgCount + 1) throw new InvalidOperationException($"Invalid arg count: {len} vs {ArgCount + 1}");
            reader.ReadNextScalar();
            reader.Demand(RespPrefix.BulkString);
            var cmd = reader.ReadString();
            if (cmd != Command) throw new InvalidOperationException($"Invalid command: '{cmd}'");
            if (reader.TryReadNext()) throw new InvalidOperationException($"Unexpected token {reader.Prefix}");
            if (reader.BytesConsumed != span.Length) throw new InvalidOperationException("Data length mismatch");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"RESP prefix validation failure for '{Command}': {ex.Message}", ex);
        }
    }

    private protected static void ThrowWriteArgs() => throw new NotSupportedException($"Either {nameof(CommandWriter<int>.WriteArgs)} or {nameof(CommandWriter<int>.Write)} must be overridden");

    /// <summary>
    /// Allows custom commands to be issued (including the command itself).
    /// </summary>
    public static IRespWriter<LeasedStrings> AdHoc => AdHocCommandWriter.Instance;

    private sealed class AdHocCommandWriter : CommandWriter<LeasedStrings>
    {
        private AdHocCommandWriter() : base("ad-hoc", -1) { }
        public static readonly AdHocCommandWriter Instance = new();

        protected override IRespWriter<LeasedStrings> Create(string command) => this;
        public override void Write(in LeasedStrings request, ref RespWriter writer)
        {
            writer.WriteArray(request.Length);
            foreach (var value in request)
            {
                writer.WriteBulkString(value);
            }
        }
    }
}

/// <summary>
/// Common shared <see cref="IRespWriter{TRequest}"/> implementation.
/// </summary>
/// <remarks>Implementers of fixed-length commands must (for non-zero arg-counts) override <see cref="WriteArgs(in TRequest, ref RespWriter)"/>. Fully dynamic implementations must override <see cref="WriteArgs(in TRequest, ref RespWriter)"/> and write the command manually. All implementations may choose to override <see cref="WriteArgs(in TRequest, ref RespWriter)"/>, to minimize virtual calls.</remarks>
public abstract class CommandWriter<TRequest> : CommandWriter, IRespWriter<TRequest>
{
    /// <summary>
    /// Create a new instance, precomputing a prefix for the specified <paramref name="command"/>.
    /// </summary>
    public CommandWriter(string command, int argCount) : base(command, argCount) { }

    internal CommandWriter(string command, int argCount, ReadOnlySpan<byte> pinnedPrefix)
        : base(command, argCount, pinnedPrefix) { }

    /// <inheritdoc cref="IRespWriter{TRequest}.Write(in TRequest, ref RespWriter)"/>
    public virtual void Write(in TRequest request, IBufferWriter<byte> target)
    {
        RespWriter writer = new(target);
        Write(in request, ref writer);
        writer.Flush();
    }

    /// <inheritdoc cref="IRespWriter{TRequest}.Write(in TRequest, ref RespWriter)"/>
    public virtual void Write(in TRequest request, ref RespWriter writer)
    {
        writer.WriteRaw(CommandAndArgCount);
        WriteArgs(in request, ref writer);
    }

    /// <summary>
    /// Write the arguments associated with the <paramref name="request"/>.
    /// </summary>
    protected internal virtual void WriteArgs(in TRequest request, ref RespWriter writer)
    {
        if (ArgCount != 0) ThrowWriteArgs();
    }

    IRespWriter<TRequest> IRespWriter<TRequest>.WithAlias(string command)
    {
        if (command == Command) return this;
        if (string.IsNullOrWhiteSpace(command)) return Disabled<TRequest>();
        return Create(command);
    }

    /// <summary>
    /// Create a new instance of this type with the specified command.
    /// </summary>
    protected abstract IRespWriter<TRequest> Create(string command);
}
