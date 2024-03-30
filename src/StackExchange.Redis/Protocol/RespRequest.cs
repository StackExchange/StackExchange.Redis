using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Buffers;
using System.Buffers.Text;
#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS1591 // new API

namespace StackExchange.Redis.Protocol;

[Experimental(ExperimentalDiagnosticID)]
public abstract class RespRequest
{
    internal const string ExperimentalDiagnosticID = "SERED002";
    protected RespRequest() { }
    public abstract void Write(ref RespWriter writer);
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
public static class RespReaders
{
    private static readonly Impl common = new();
    public static IRespReader<string?> String => common;
    internal sealed class Impl : IRespReader<string?>
    {
        public string? Read(ref RespReader reader)
        {
            reader.ReadNext();
            return reader.ReadString();
        }
    }

    public static IRespReader OK = new UnsafeFixedSimpleResponse("OK"u8);
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
internal sealed class SimpleOKResponse : IRespReader
{
    void IRespReader.Read(ref RespReader reader)
    {
        if (!reader.IsOK()) Throw();
        static void Throw()
        => throw new InvalidOperationException("Did not receive expected response: '+OK'");
    }
}
[Experimental(RespRequest.ExperimentalDiagnosticID)]
internal unsafe sealed class UnsafeFixedSimpleResponse : IRespReader
{
    private readonly byte* _ptr;
    private readonly int _length;
    private string? _message;
    public UnsafeFixedSimpleResponse(ReadOnlySpan<byte> fixedMessage) // caller **must** use a fixed span; "..."u8 satisfies this
    {
        _length = fixedMessage.Length;
        _ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(fixedMessage));
    }

    void IRespReader.Read(ref RespReader reader)
    {
        if (!(reader.Prefix == RespPrefix.SimpleString && reader.Is(new(_ptr, _length)))) Throw();

    }
    [DoesNotReturn]
    private void Throw() => throw new InvalidOperationException(Message);
    private string Message => _message ??= $"Did not receive expected response: '+{Encoding.ASCII.GetString(_ptr, _length)}'";
}



[Experimental(RespRequest.ExperimentalDiagnosticID)]
internal class NewCommandMap
{
    public static NewCommandMap Create(Dictionary<string, string?> overrides)
    {
        if (overrides is { Count: > 0 })
        {
            IDictionary<string, string?> typed = overrides;
            PreparedRespWriters prepared = new(in PreparedRespWriters.Default, ref typed);
            if (overrides.Count > 0)
            {
                return new(typed, prepared);
            }
        }
        return Default;
    }

    public static NewCommandMap Default { get; } = new(null, PreparedRespWriters.Default);

    private readonly PreparedRespWriters _commands;
    internal ref readonly PreparedRespWriters RawCommands => ref _commands; // avoid stack copies; this is large

    private NewCommandMap(IDictionary<string, string?>? overrides, in PreparedRespWriters commands)
    {
        _commands = commands;
#if NET8_0_OR_GREATER
        _overrides = ((FrozenDictionary<string, string?>?)overrides) ?? FrozenDictionary<string, string?>.Empty;
#else
        _overrides = ((Dictionary<string, string?>?)overrides) ?? SharedEmpty;
#endif
    }

    internal string? Normalize(string command, out RedisCommand known)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            known = RedisCommand.UNKNOWN;
            return null;
        }
        if (!Enum.TryParse(command, true, out known) || known == RedisCommand.NONE)
        {
            known = RedisCommand.UNKNOWN;
        }
        if (_overrides.TryGetValue(command, out var mapped)) return mapped;

        if (known != RedisCommand.UNKNOWN)
        {
            // normalize case (this is non-allocating for known enum values)
            command = known.ToString(); 
        }
        return command;
    }

#if NET8_0_OR_GREATER
    private readonly FrozenDictionary<string, string?> _overrides;
#else
    private readonly Dictionary<string, string?> _overrides;
    private static readonly Dictionary<string, string?> SharedEmpty = new();
#endif

    internal readonly struct PreparedRespWriters
    {
        private static readonly PreparedRespWriters _default = new(true);
        public static ref readonly PreparedRespWriters Default => ref _default; // avoid stack copies; this is large

        public readonly IRespWriter? Ping;
        public readonly IRespWriter? Quit;

        internal PreparedRespWriters(in PreparedRespWriters template, ref IDictionary<string, string?> overrides)
        {
            this = template;
            // we want a defensive copy of the overrides (for Execute etc); make sure it is case-insensitive, ignore X=X, and UC
            var deltas = from pair in overrides
                         let value = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim().ToUpperInvariant()
                         where !string.IsNullOrWhiteSpace(pair.Key) // ignore "" etc
                            && pair.Key.Trim() == pair.Key // ignore " PING" etc - this is a no-op for valid data
                            && !StringComparer.OrdinalIgnoreCase.Equals(pair.Key, value) // ignore "PING=PING"
                         select new KeyValuePair<string,string?>(pair.Key, value); // upper-casify
#if NET8_0_OR_GREATER
            overrides = FrozenDictionary.ToFrozenDictionary(deltas, StringComparer.OrdinalIgnoreCase);
#else
            overrides = new Dictionary<string, string?>(overrides.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in deltas) overrides.Add(pair.Key, pair.Value);
#endif
            if (overrides.TryGetValue("ping", out string? cmd)) Ping = RawFixedMessageWriter.Create(cmd);
            if (overrides.TryGetValue("quit", out cmd)) Quit = RawFixedMessageWriter.Create(cmd);

        }
        private PreparedRespWriters(bool dummy)
        {
            _ = dummy;
            Ping = new RawUnsafeFixedMessageWriter("*1\r\n$4\r\nPING\r\n"u8);
            Quit = new RawUnsafeFixedMessageWriter("*1\r\n$4\r\nQUIT\r\n"u8);
        }
    }
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
internal unsafe sealed class RawUnsafeFixedMessageWriter : IRespWriter
{
    private readonly byte* _ptr;
    private readonly int _length;
    public RawUnsafeFixedMessageWriter(ReadOnlySpan<byte> fixedMessage) // caller **must** use a fixed span; "..."u8 satisfies this
    {
        _length = fixedMessage.Length;
        _ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(fixedMessage));
    }

    public void Write(ref RespWriter writer) => writer.WriteRaw(new ReadOnlySpan<byte>(_ptr, _length));
}
[Experimental(RespRequest.ExperimentalDiagnosticID)]
internal sealed class RawFixedMessageWriter : IRespWriter
{
    private readonly byte[] _prepared;
    public static RawFixedMessageWriter? Create(string? command)
        => string.IsNullOrWhiteSpace(command) ? null : new(command!);
    private RawFixedMessageWriter(string command)
    {
        var buffer = RespWriter.Create(preambleReservation: 0);
        buffer.WriteCommand(command, 0);
        var leased = buffer.Detach();
        _prepared = leased.GetBuffer().ToArray();
        leased.Recycle();
    }

    public void Write(ref RespWriter writer) => writer.WriteRaw(_prepared);
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
public interface IWhatever
{
    TResponse Execute<TResponse>(IRespWriter writer, IRespReader<TResponse> reader);
    TResponse Execute<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TResponse> reader);
    TResponse Execute<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TRequest, TResponse> reader);
    void Execute<TRequest>(in TRequest request, IRespWriter<TRequest> writer, IRespReader reader);
    void Execute(IRespWriter writer, IRespReader reader);

    Task<TResponse> ExecuteAsync<TResponse>(IRespWriter writer, IRespReader<TResponse> reader);
    Task<TResponse> ExecuteAsync<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TResponse> reader);
    Task<TResponse> ExecuteAsync<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TRequest, TResponse> reader);
    Task ExecuteAsync<TRequest>(in TRequest request, IRespWriter<TRequest> writer, IRespReader reader);
    Task ExecuteAsync(IRespWriter writer, IRespReader reader);
}
[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
public static class WhateverExtensions
{
    public static TResponse Execute<TRequest, TResponse>(this IWhatever whatever, in TRequest request, IRespProcessor<TRequest, TResponse> processor)
        => whatever.Execute<TRequest, TResponse>(request, processor, processor);
    public static Task<TResponse> ExecuteAsync<TRequest, TResponse>(this IWhatever whatever, in TRequest request, IRespProcessor<TRequest, TResponse> processor)
        => whatever.ExecuteAsync<TRequest, TResponse>(request, processor, processor);
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
internal class Whatever : IWhatever
{
    private static RequestBuffer WriteToLease(IRespWriter writer)
    {
        var target = RespWriter.Create();
        writer.Write(ref target);
        var buffer = target.Detach();
        buffer.DebugValidateCommand();
        return buffer;
    }
    private static RequestBuffer WriteToLease<TRequest>(IRespWriter<TRequest> writer, in TRequest request)
    {
        var target = RespWriter.Create();
        writer.Write(in request, ref target);
        var buffer = target.Detach();
        buffer.DebugValidateCommand();
        return buffer;
    }

    TResponse IWhatever.Execute<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TResponse> reader)
    {
        var payload = WriteToLease(writer, in request);
        var msg = MessageSyncPlaceholder<TResponse>.Create(payload, reader);
        lock (msg.SyncLock)
        {
            Enqueue(msg);
            Monitor.Wait(msg.SyncLock);
            return msg.WaitLocked();
        }
    }

    private void Enqueue(IMessage msg) => throw new NotImplementedException();

    TResponse IWhatever.Execute<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TRequest, TResponse> reader)
    {
        var payload = WriteToLease(writer, request);
        var msg = MessageSyncPlaceholder<TResponse>.Create(payload, request, reader);
        lock (msg.SyncLock)
        {
            Enqueue(msg);
            return msg.WaitLocked();
        }
    }

    void IWhatever.Execute<TRequest>(in TRequest request, IRespWriter<TRequest> writer, IRespReader reader)
    {
        var payload = WriteToLease(writer, request);
        var msg = MessageSyncPlaceholder.Create(payload, reader);
        lock (msg.SyncLock)
        {
            Enqueue(msg);
            msg.WaitLocked();
        }
    }
    void IWhatever.Execute(IRespWriter writer, IRespReader reader)
    {
        var payload = WriteToLease(writer);
        var msg = MessageSyncPlaceholder.Create(payload, reader);
        lock (msg.SyncLock)
        {
            Enqueue(msg);
            msg.WaitLocked();
        }
    }

    Task<TResponse> IWhatever.ExecuteAsync<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TResponse> reader)
    {
        var payload = WriteToLease(writer, in request);
        var msg = MessageAsyncPlaceholder<TResponse>.Create(payload, reader);
        Enqueue(msg);
        return msg.Task;
    }
    Task<TResponse> IWhatever.ExecuteAsync<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TRequest, TResponse> reader)
    {
        var payload = WriteToLease(writer, in request);
        var msg = MessageAsyncPlaceholder<TResponse>.Create(payload, request, reader);
        Enqueue(msg);
        return msg.Task;
    }

    TResponse IWhatever.Execute<TResponse>(IRespWriter writer, IRespReader<TResponse> reader)
    {
        var payload = WriteToLease(writer);
        var msg = MessageSyncPlaceholder<TResponse>.Create(payload, reader);
        lock (msg.SyncLock)
        {
            Enqueue(msg);
            return msg.WaitLocked();
        }
    }
    Task<TResponse> IWhatever.ExecuteAsync<TResponse>(IRespWriter writer, IRespReader<TResponse> reader)
    {
        var payload = WriteToLease(writer);
        var msg = MessageAsyncPlaceholder<TResponse>.Create(payload, reader);
        Enqueue(msg);
        return msg.Task;
    }

    Task IWhatever.ExecuteAsync<TRequest>(in TRequest request, IRespWriter<TRequest> writer, IRespReader reader)
    {
        var payload = WriteToLease(writer, request);
        var msg = MessageAsyncPlaceholder.Create(payload, reader);
        Enqueue(msg);
        return msg.Task;
    }
    Task IWhatever.ExecuteAsync(IRespWriter writer, IRespReader reader)
    {
        var payload = WriteToLease(writer);
        var msg = MessageAsyncPlaceholder.Create(payload, reader);
        Enqueue(msg);
        return msg.Task;
    }
}

internal interface IMessage // nongeneric API for the message queue
{
    bool TrySetException(Exception fault);
    bool TrySetCanceled(CancellationToken token);
    void Recycle();
    void TrySetResult(in ReadOnlySequence<byte> result);
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
internal abstract class MessageSyncPlaceholder<TResponse> : IMessage
{
    public virtual void Recycle() => _payload.Recycle();
    private readonly RequestBuffer _payload;
    protected MessageSyncPlaceholder(in RequestBuffer payload)
        => _payload = payload;

    public void TrySetResult(in ReadOnlySequence<byte> payload)
    {
        try
        {
            var reader = new RespReader(in payload);
            if (!reader.ReadNext()) RespReader.ThrowEOF();
            if (reader.IsError)
            {
                TrySetException(reader.ReadError());
            }
            else
            {
                TrySetResult(Parse(ref reader));
            }
            Debug.Assert(!reader.ReadNext(), "not fully consumed");
        }
        catch(Exception ex)
        {
            TrySetException(ex);
        }
    }
    protected abstract TResponse Parse(ref RespReader reader);

    public bool TrySetException(Exception fault)
    {
        lock (SyncLock)
        {
            if (_fault is not null) return false;
            _fault = fault;
            Monitor.PulseAll(SyncLock);
            return true;
        }
    }
    public bool TrySetResult(TResponse result)
    {
        lock (SyncLock)
        {
            if (_fault is not null) return false;
            _result = result;
            Monitor.PulseAll(SyncLock);
            return true;
        }
    }

    public static MessageSyncPlaceholder<TResponse> Create(in RequestBuffer payload, IRespReader<TResponse> reader) => new StatelessMessage(payload, reader);
    public static MessageSyncPlaceholder<TResponse> Create<TRequest>(in RequestBuffer payload, in TRequest request, IRespReader<TRequest, TResponse> reader)
        => new StatefulMessage<TRequest>(payload, request, reader);
    public object SyncLock => this;
    private Exception? _fault;
    private TResponse? _result;
    public TResponse WaitLocked()
    {
        Monitor.Wait(SyncLock);
        if (_fault is not null) throw _fault;
        return _result!;
    }

    bool IMessage.TrySetCanceled(CancellationToken token) => TrySetException(new OperationCanceledException(token));

    private sealed class StatelessMessage : MessageSyncPlaceholder<TResponse>
    {
        private readonly IRespReader<TResponse> _reader;
        protected override TResponse Parse(ref RespReader reader) => _reader.Read(ref reader);
        public StatelessMessage(in RequestBuffer payload, IRespReader<TResponse> reader) : base(payload)
        {
            _reader = reader;
        }
    }
    private sealed class StatefulMessage<TRequest> : MessageSyncPlaceholder<TResponse>
    {
        private readonly TRequest _request;
        private readonly IRespReader<TRequest, TResponse> _reader;
        protected override TResponse Parse(ref RespReader reader) => _reader.Read(in _request, ref reader);

        public StatefulMessage(in RequestBuffer payload, in TRequest request, IRespReader<TRequest, TResponse> reader) : base(payload)
        {
            _request = request;
            _reader = reader;
        }
        public override void Recycle()
        {
            base.Recycle();
            Unsafe.AsRef(in _request) = default!;
        }
    }
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
internal sealed class MessageSyncPlaceholder : IMessage
{
    public void Recycle() => _payload.Recycle();
    private readonly RequestBuffer _payload;
    private readonly IRespReader _reader;
    private MessageSyncPlaceholder(in RequestBuffer payload, IRespReader reader)
    {
        _payload = payload;
        _reader = reader;
    }
    public void TrySetResult(in ReadOnlySequence<byte> payload)
    {
        try
        {
            var reader = new RespReader(in payload);
            if (!reader.ReadNext()) RespReader.ThrowEOF();
            if (reader.IsError)
            {
                TrySetException(reader.ReadError());
            }
            else
            {
                _reader.Read(ref reader);
                TrySetResult();
            }
            Debug.Assert(!reader.ReadNext(), "not fully consumed");
        }
        catch (Exception ex)
        {
            TrySetException(ex);
        }
    }

    public bool TrySetException(Exception fault)
    {
        lock (SyncLock)
        {
            if (_fault is not null) return false;
            _fault = fault;
            Monitor.PulseAll(SyncLock);
            return true;
        }
    }
    public bool TrySetResult()
    {
        lock (SyncLock)
        {
            if (_fault is not null) return false;
            Monitor.PulseAll(SyncLock);
            return true;
        }
    }

    public static MessageSyncPlaceholder Create(in RequestBuffer payload, IRespReader reader) => new(payload, reader);
    public object SyncLock => this;
    private Exception? _fault;
    public void WaitLocked()
    {
        Monitor.Wait(SyncLock);
        if (_fault is not null) throw _fault;
    }

    bool IMessage.TrySetCanceled(CancellationToken token) => TrySetException(new OperationCanceledException(token));
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
internal abstract class MessageAsyncPlaceholder<TResponse> : TaskCompletionSource<TResponse>, IMessage
{
    public virtual void Recycle() => _payload.Recycle();
    private readonly RequestBuffer _payload;
    protected MessageAsyncPlaceholder(in RequestBuffer payload) : base(TaskCreationOptions.RunContinuationsAsynchronously)
        => _payload = payload;

    public static MessageAsyncPlaceholder<TResponse> Create(in RequestBuffer payload, IRespReader<TResponse> reader) => new StatelessMessage(payload, reader);
    public static MessageAsyncPlaceholder<TResponse> Create<TRequest>(in RequestBuffer payload, in TRequest request, IRespReader<TRequest, TResponse> reader)
        => new StatefulMessage<TRequest>(payload, request, reader);
    public void TrySetResult(in ReadOnlySequence<byte> payload)
    {
        try
        {
            var reader = new RespReader(in payload);
            if (!reader.ReadNext()) RespReader.ThrowEOF();
            if (reader.IsError)
            {
                TrySetException(reader.ReadError());
            }
            else
            {
                TrySetResult(Parse(ref reader));
            }
            Debug.Assert(!reader.ReadNext(), "not fully consumed");
        }
        catch (Exception ex)
        {
            TrySetException(ex);
        }
    }
    protected abstract TResponse Parse(ref RespReader reader);
    
    private sealed class StatelessMessage : MessageAsyncPlaceholder<TResponse>
    {
        private readonly IRespReader<TResponse> _reader;
        protected override TResponse Parse(ref RespReader reader) => _reader.Read(ref reader);
        public StatelessMessage(in RequestBuffer payload, IRespReader<TResponse> reader) : base(payload)
        {
            _reader = reader;
        }
    }
    private sealed class StatefulMessage<TRequest> : MessageAsyncPlaceholder<TResponse>
    {
        private readonly TRequest _request;
        private readonly IRespReader<TRequest, TResponse> _reader;
        protected override TResponse Parse(ref RespReader reader) => _reader.Read(in _request, ref reader);
        public StatefulMessage(in RequestBuffer payload, in TRequest request, IRespReader<TRequest, TResponse> reader) : base(payload)
        {
            _request = request;
            _reader = reader;
        }

        public override void Recycle()
        {
            base.Recycle();
            Unsafe.AsRef(in _request) = default!;
        }
    }
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
internal sealed class MessageAsyncPlaceholder :
#if NET5_0_OR_GREATER
    TaskCompletionSource, IMessage
#else
    TaskCompletionSource<bool>, IMessage
#endif
{
    public void Recycle() => _payload.Recycle();
    private readonly RequestBuffer _payload;
    private readonly IRespReader _reader;
    internal MessageAsyncPlaceholder(in RequestBuffer payload, IRespReader reader) : base(TaskCreationOptions.RunContinuationsAsynchronously)
    {
        _payload = payload;
        _reader = reader;
    }

    public static MessageAsyncPlaceholder Create(in RequestBuffer payload, IRespReader reader) => new(payload, reader);
    public void TrySetResult(in ReadOnlySequence<byte> payload)
    {
        try
        {
            var reader = new RespReader(in payload);
            if (!reader.ReadNext()) RespReader.ThrowEOF();
            if (reader.IsError)
            {
                TrySetException(reader.ReadError());
            }
            else
            {
                _reader.Read(ref reader);
#if NET5_0_OR_GREATER
                TrySetResult();
#else
                TrySetResult(true);
#endif
            }
            Debug.Assert(!reader.ReadNext(), "not fully consumed");
        }
        catch (Exception ex)
        {
            TrySetException(ex);
        }
    }
}


[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
public interface IRespWriter // base-class for reusable writers that do not need input state
{
    void Write(ref RespWriter writer);
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
public interface IRespWriter<TRequest> // base-class for reusable writers that need input state
{
    void Write(in TRequest request, ref RespWriter writer);
}
[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
public interface IRespReader<TResponse> // base-class for reusable parsers that don't need input state
{
    TResponse Read(ref RespReader reader);
}
[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
public interface IRespReader // base-class for reusable parsers that don't need input state or return a value
{
    void Read(ref RespReader reader);
}
[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
public interface IRespReader<TRequest, TResponse> // base-class for reusable parsers that need input state
{
    TResponse Read(in TRequest request, ref RespReader reader);
}
[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
public interface IRespProcessor<TRequest, TResponse>
    : IRespWriter<TRequest>, IRespReader<TRequest, TResponse>
{
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
public enum RespPrefix : byte
{
    None = 0,
    SimpleString = (byte)'+',
    SimpleError = (byte)'-',
    Integer = (byte)':',
    BulkString = (byte)'$',
    Array = (byte)'*',
    Null = (byte)'_',
    Boolean = (byte)'#',
    Double = (byte)',',
    BigNumber = (byte)'(',
    BulkError = (byte)'!',
    VerbatimString = (byte)'=',
    Map = (byte)'%',
    Set = (byte)'~',
    Push = (byte)'>',

    // these are not actually implemented by any server; no
    // longer part of RESP3?
    // Stream = (byte)';',
    // UnboundEnd = (byte)'.',
    // Attribute = (byte)'|',
}

//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//public abstract class RespProcessor<T>
//{
//    public abstract T Parse(in RespChunk value);
//}

internal sealed partial class RefCountedSequenceSegment<T> : ReadOnlySequenceSegment<T>, IMemoryOwner<T>
{
#if DEBUG
    private readonly long _id = Interlocked.Increment(ref _debugTotalLeased);
    private static long _debugTotalLeased, _debugTotalReturned;
    internal static long DebugOutstanding => Volatile.Read(ref _debugTotalLeased) - Volatile.Read(ref _debugTotalReturned);
    internal static long DebugTotalLeased => Volatile.Read(ref _debugTotalLeased);
    partial void DebugDecrOutstanding()
    {
        Interlocked.Increment(ref _debugTotalReturned);
    }
    partial void DebugMessage(string message) => Debug.WriteLine($"[{_id}@{Volatile.Read(ref _refCount)}]: {message}");
#endif
    [Conditional("DEBUG")]
    partial void DebugMessage([CallerMemberName] string message = "");
    [Conditional("DEBUG")]
    partial void DebugDecrOutstanding();

    public override string ToString() => $"(ref-count: {RefCount}) {base.ToString()}";
    private int _refCount;
    private readonly IDisposable _handle;
    internal int RefCount => Volatile.Read(ref _refCount);
    private static void ThrowDisposed() => throw new ObjectDisposedException(nameof(RefCountedSequenceSegment<T>));
    private sealed class DisposedMemoryManager : MemoryManager<T>
    {
        public static readonly ReadOnlyMemory<T> Instance;
        private static readonly bool _triggered;
        static DisposedMemoryManager()
        {
            // accessing .Memory touches .Span for .Length, so
            // we need to delay making it throw
            Instance = new DisposedMemoryManager().Memory;
            _triggered = true;
        }

        protected override void Dispose(bool disposing) { }

        // note that we deliberately spoof a non-empty length, to avoid IsEmpty short-circuits,
        // because we *want* people to know that they're doing something wrong
        public override Span<T> GetSpan() { if (_triggered) ThrowDisposed(); return new T[8]; }

        public override MemoryHandle Pin(int elementIndex = 0) { if (_triggered) ThrowDisposed(); return default; }
        public override void Unpin() { if (_triggered) ThrowDisposed(); }
        protected override bool TryGetArray(out ArraySegment<T> segment)
        {
            if (_triggered) ThrowDisposed();
            segment = default;
            return default;
        }
    }

    public RefCountedSequenceSegment(IDisposable handle, Memory<T> memory, RefCountedSequenceSegment<T>? previous = null)
    {
        _handle = handle;
        _refCount = 1;
        Memory = memory;
        if (previous is not null)
        {
            RunningIndex = previous.RunningIndex + previous.Memory.Length;
            previous.Next = this;
        }
        DebugMessage();
    }

    Memory<T> IMemoryOwner<T>.Memory => MemoryMarshal.AsMemory(Memory);

    public void Dispose()
    {
        int oldCount;
        do
        {
            oldCount = Volatile.Read(ref _refCount);
            if (oldCount == 0) return; // already released
        } while (Interlocked.CompareExchange(ref _refCount, oldCount - 1, oldCount) != oldCount);
        DebugMessage();
        if (oldCount == 1) // then we killed it
        {
            Release();
        }
    }

    public void AddRef()
    {
        int oldCount;
        do
        {
            oldCount = Volatile.Read(ref _refCount);
            if (oldCount == 0) ThrowDisposed();
        } while (Interlocked.CompareExchange(ref _refCount, checked(oldCount + 1), oldCount) != oldCount);
        DebugMessage();
    }

    private void Release()
    {
        var memory = Memory;
        Memory = DisposedMemoryManager.Instance;
        _handle.Dispose();
        DebugDecrOutstanding();
        DebugMessage();
    }

    internal new RefCountedSequenceSegment<T>? Next
    {
        get => (RefCountedSequenceSegment<T>?)base.Next;
        set => base.Next = value;
    }
}

public readonly struct LeasedSequence<T> : IDisposable
{
#if DEBUG
    [SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Debug API")]
    public static long DebugOutstanding => RefCountedSequenceSegment<byte>.DebugOutstanding;
    [SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Debug API")]
    public static long DebugTotalLeased => RefCountedSequenceSegment<byte>.DebugTotalLeased;
#endif

    public LeasedSequence(scoped in ReadOnlySequence<T> value) => _value = value;
    private readonly ReadOnlySequence<T> _value;

    public override string ToString() => _value.ToString();
    public long Length => _value.Length;
    public bool IsEmpty => _value.IsEmpty;
    public bool IsSingleSegment => _value.IsSingleSegment;
    public SequencePosition Start => _value.Start;
    public SequencePosition End => _value.End;
    public SequencePosition GetPosition(long offset) => _value.GetPosition(offset);
    public SequencePosition GetPosition(long offset, SequencePosition origin) => _value.GetPosition(offset, origin);

    public ReadOnlyMemory<T> First => _value.First;
#if NETCOREAPP3_0_OR_GREATER
    public ReadOnlySpan<T> FirstSpan => _value.FirstSpan;
#else
    public ReadOnlySpan<T> FirstSpan => _value.First.Span;
#endif

    public bool TryGet(ref SequencePosition position, out ReadOnlyMemory<T> memory, bool advance = true)
        => _value.TryGet(ref position, out memory, advance);
    public ReadOnlySequence<T>.Enumerator GetEnumerator() => _value.GetEnumerator();

    public static implicit operator ReadOnlySequence<T>(LeasedSequence<T> value) => value._value;

    // we do *not* assume that slices take additional leases; usually slicing is a transient operation
    public ReadOnlySequence<T> Slice(long start) => _value.Slice(start);
    public ReadOnlySequence<T> Slice(SequencePosition start) => _value.Slice(start);
    public ReadOnlySequence<T> Slice(int start, int length) => _value.Slice(start, length);
    public ReadOnlySequence<T> Slice(int start, SequencePosition end) => _value.Slice(start, end);
    public ReadOnlySequence<T> Slice(long start, long length) => _value.Slice(start, length);
    public ReadOnlySequence<T> Slice(long start, SequencePosition end) => _value.Slice(start, end);
    public ReadOnlySequence<T> Slice(SequencePosition start, int length) => _value.Slice(start, length);
    public ReadOnlySequence<T> Slice(SequencePosition start, long length) => _value.Slice(start, length);
    public ReadOnlySequence<T> Slice(SequencePosition start, SequencePosition end) => _value.Slice(start, end);

    public void Dispose()
    {
        if (_value.Start.GetObject() is ReadOnlySequenceSegment<T> segment)
        {
            var end = _value.End.GetObject();
            do
            {
                if (segment is IDisposable d)
                {
                    d.Dispose();
                }
            }
            while (!ReferenceEquals(segment, end) && (segment = segment!.Next!) is not null);
        }
    }

    public void AddRef()
    {
        if (_value.Start.GetObject() is ReadOnlySequenceSegment<T> segment)
        {
            var end = _value.End.GetObject();
            do
            {
                if (segment is RefCountedSequenceSegment<T> counted)
                {
                    counted.AddRef();
                }
            }
            while (!ReferenceEquals(segment, end) && (segment = segment!.Next!) is not null);
        }
    }
}

/// <summary>
/// Abstract source of streaming RESP data; the implementation is responsible
/// for retaining a back buffer of pending bytes, and exposing those bytes via <see cref="GetBuffer"/>;
/// additional data is requested via <see cref="TryReadAsync(CancellationToken)"/>, and
/// is consumed via <see cref="Take(long)"/>. The data returned from <see cref="Take(long)"/>
/// can optionally be a chain of <see cref="SequenceSegment{T}"/> that additionally
/// implement <see cref="IDisposable"/>, in which case the <see cref="LeasedSequence{T}"/>
/// will dispose them appropriately (allowing for buffer pool scenarios). Note also that
/// the buffer returned from <see cref="Take"/> does not need to be the same chain as
/// used in <see cref="GetBuffer"/> - it is permitted to copy (etc) the data when consuming.
/// </summary>
[Experimental(RespRequest.ExperimentalDiagnosticID)]
public abstract partial class RespSource : IAsyncDisposable
{
    public static RespSource Create(Stream source, bool closeStream = false)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (!source.CanRead) throw new ArgumentException("Source stream cannot be read", nameof(source));

        if (source is MemoryStream ms && ms.TryGetBuffer(out var segment) && segment.Array is not null)
        {
            return Create(new ReadOnlySequence<byte>(segment.Array, segment.Offset, segment.Count));
        }

        return new StreamRespSource(source, closeStream);
    }

    protected abstract ReadOnlySequence<byte> GetBuffer();

    public static RespSource Create(in ReadOnlySequence<byte> payload) => new InMemoryRespSource(payload);
    public static RespSource Create(ReadOnlyMemory<byte> payload) => new InMemoryRespSource(new(payload));

    private protected RespSource() { }

    protected abstract ValueTask<bool> TryReadAsync(CancellationToken cancellationToken);

    [Conditional("DEBUG")]
    static partial void DebugWrite(in ReadOnlySequence<byte> data);

#if DEBUG
    static partial void DebugWrite(in ReadOnlySequence<byte> data)
    {
        try
        {
            var reader = new RespReader(data);
            reader.ReadNext();
            Debug.WriteLine(reader.ToString());
        }
        catch(Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }
#endif

    // internal abstract long Scan(long skip, ref int count);
    public async ValueTask<LeasedSequence<byte>> ReadNextAsync(CancellationToken cancellationToken = default)
    {
        int pending = 1;
        long totalConsumed = 0;
        while (pending != 0)
        {
            var consumed = Scan(GetBuffer().Slice(totalConsumed), ref pending);
            totalConsumed += consumed;

            if (pending != 0)
            {
                if (!await TryReadAsync(cancellationToken))
                {
                    if (totalConsumed != 0)
                    {
                        throw new EndOfStreamException();
                    }
                    return default;
                }
            }
        }
        var chunk = Take(totalConsumed);
        if (chunk.Length != totalConsumed) Throw();
        return new(chunk);

        static void Throw() => throw new InvalidOperationException("Buffer length mismatch in " + nameof(ReadNextAsync));

        // can't use ref-struct in async method
        static long Scan(in ReadOnlySequence<byte> payload, ref int count)
        {
            var reader = new RespReader(in payload);
            while (count > 0 && reader.ReadNext())
            {
                count = count - 1 + reader.ChildCount;
            }
            Debug.Assert(reader.BytesConsumed <= payload.Length);
            return reader.BytesConsumed;
        }
    }

    protected abstract ReadOnlySequence<byte> Take(long bytes);

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }

    private sealed class InMemoryRespSource : RespSource
    {
        private ReadOnlySequence<byte> _remaining;
        public InMemoryRespSource(in ReadOnlySequence<byte> value)
            => _remaining = value;

        protected override ReadOnlySequence<byte> GetBuffer() => _remaining;
        protected override ReadOnlySequence<byte> Take(long bytes)
        {
            var take = _remaining.Slice(0, bytes);
            _remaining = _remaining.Slice(take.End);
            new LeasedSequence<byte>(take).AddRef();
            return take;
        }
        protected override ValueTask<bool> TryReadAsync(CancellationToken cancellationToken) => default; // nothing more to get
    }

    private sealed class StreamRespSource : RespSource
    {
        private readonly Stream _source;
        private readonly bool _closeStream;
        private RotatingBufferCore _buffer;
        internal StreamRespSource(Stream source, bool closeStream)
        {
            _buffer = new(new SlabManager());
            _source = source;
            _closeStream = closeStream;
        }

        protected override ReadOnlySequence<byte> GetBuffer() => _buffer.GetBuffer();


#if NETCOREAPP3_1_OR_GREATER
        public override ValueTask DisposeAsync()
        {
            _buffer.Dispose();
            _buffer.SlabManager.Dispose();
            return _closeStream ? _source.DisposeAsync() : default;
        }
#else
        public override ValueTask DisposeAsync()
        {
            _buffer.Dispose();
            _buffer.SlabManager.Dispose();
            if (_closeStream) _source.Dispose();
            return default;
        }
#endif
        protected override ValueTask<bool> TryReadAsync(CancellationToken cancellationToken)
        {
            var readBuffer = _buffer.GetWritableTail();
            Debug.Assert(!readBuffer.IsEmpty, "should have space");
#if NETCOREAPP3_1_OR_GREATER
            var pending = _source.ReadAsync(readBuffer, cancellationToken);
            if (!pending.IsCompletedSuccessfully) return Awaited(this, pending);
#else
            // we know it is an array; happy to explode weirdly otherwise!
            if (!MemoryMarshal.TryGetArray<byte>(readBuffer, out var segment)) ThrowNotArray();
            var pending = _source.ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken);
            if (pending.Status != TaskStatus.RanToCompletion) return Awaited(this, pending);

            static void ThrowNotArray() => throw new InvalidOperationException("Unable to obtain array from tail buffer");
#endif

            // synchronous happy case
            var bytes = pending.GetAwaiter().GetResult();
            if (bytes > 0)
            {
                _buffer.Commit(bytes);
                return new(true);
            }
            return default;

            static async ValueTask<bool> Awaited(StreamRespSource @this,
#if NETCOREAPP3_1_OR_GREATER
                ValueTask<int> pending
#else
                Task<int> pending
#endif
                )
            {
                var bytes = await pending;
                if (bytes > 0)
                {
                    @this._buffer.Commit(bytes);
                    return true;
                }
                return false;
            }
        }

        protected override ReadOnlySequence<byte> Take(long bytes) => _buffer.DetachRotating(bytes);
    }
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
public ref struct RespReader
{
    private readonly ReadOnlySequence<byte> _fullPayload;
    private SequencePosition _segPos;
    private long _positionBase;
    private int _bufferIndex; // after TryRead, this should be positioned immediately before the actual data
    private int _bufferLength;
    private int _length; // for null: -1; for scalars: the length of the payload; for aggregates: the child count
    private RespPrefix _prefix;
    /// <summary>
    /// Returns the position after the end of the current element
    /// </summary>
    public readonly long BytesConsumed => _positionBase + _bufferIndex + TrailingLength;

    //internal int DebugBufferIndex => _bufferIndex;

    public readonly RespPrefix Prefix
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _prefix;
    }

    /// <summary>
    /// Returns as much data as possible into the buffer, ignoring
    /// any data that cannot fit into <paramref name="target"/>, and
    /// returning the segment representing copied data.
    /// </summary>
    public readonly Span<byte> CopyTo(Span<byte> target)
    {
        if (!IsScalar) return default; // only possible for scalars
        if (TryGetValueSpan(out var source))
        {
            if (source.Length > target.Length)
            {
                source = source.Slice(0, target.Length);
            }
            else if (source.Length < target.Length)
            {
                target = target.Slice(0, source.Length);
            }
            source.CopyTo(target);
            return target;
        }
        throw new NotImplementedException();
    }

    /// <summary>
    /// Returns true if the value is a valid scalar value <em>that is available as a single contiguous chunk</em>;
    /// a value could be a valid scalar but if it spans segments, this will report <c>false</c>; alternative APIs
    /// are available to inspect the value.
    /// </summary>
    internal readonly bool TryGetValueSpan(out ReadOnlySpan<byte> span)
    {
        if (!IsScalar || _length < 0)
        {
            span = default;
            return false; // only possible for scalars
        }
        if (_length == 0)
        {
            span = default;
            return true;
        }

        if (_bufferIndex + _length <= _bufferLength)
        {
#if NET7_0_OR_GREATER
            span = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref _bufferRoot, _bufferIndex), _length);
#else
            span = _bufferSpan.Slice(_bufferIndex, _length);
#endif
            return true;
        }

        // not available as a convenient contiguous chunk
        span = default;
        return false;
    }
    public readonly string? ReadString()
    {
        if (!IsScalar || _length < 0) return null;
        if (_length == 0) return "";
        if (TryGetValueSpan(out var span))
        {
#if NETCOREAPP3_0_OR_GREATER
            return RespWriter.UTF8.GetString(span);
#else
            unsafe
            {
                fixed (byte* ptr = span)
                {
                    return RespWriter.UTF8.GetString(ptr, span.Length);
                }
            }
#endif
        }
        return SlowReadString();
    }
    private readonly string SlowReadString()
    {
        // simple cases and pre-conditions already checked
        byte[]? lease = null;
        Span<byte> buffer = _length <= 128 ? stackalloc byte[128] : new(lease = ArrayPool<byte>.Shared.Rent(_length), 0, _length);
        var reader = new SlowReader(in this);
        var len = reader.Fill(buffer);
        Debug.Assert(len == _length);
#if NETCOREAPP3_1_OR_GREATER
        var s = RespWriter.UTF8.GetString(buffer);
#else
        string s;
        unsafe
        {
            fixed (byte* ptr = buffer)
            {
                s = RespWriter.UTF8.GetString(ptr, buffer.Length);
            }
        }
#endif
        if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
        return s;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AssertClLfUnsafe(scoped ref byte source, int offset)
    {
        if (Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref source, offset)) != RespWriter.CrLf)
        {
            ThrowProtocolFailure("Expected CR/LF");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AssertClLfUnsafe(scoped ref readonly byte source)
    {
        if (Unsafe.ReadUnaligned<ushort>(ref Unsafe.AsRef(in source)) != RespWriter.CrLf)
        {
            ThrowProtocolFailure("Expected CR/LF");
        }
    }

#if NET7_0_OR_GREATER
    private ref byte _bufferRoot;
    private readonly ref byte CurrentUnsafe => ref Unsafe.Add(ref _bufferRoot, _bufferIndex);
    private readonly RespPrefix PeekPrefix() => (RespPrefix)Unsafe.Add(ref _bufferRoot, _bufferIndex);
    private readonly ReadOnlySpan<byte> PeekPastPrefix() => MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.Add(ref _bufferRoot, _bufferIndex + 1), _bufferLength - (_bufferIndex + 1));
    private readonly ReadOnlySpan<byte> PeekCurrent() => MemoryMarshal.CreateReadOnlySpan(
        ref Unsafe.Add(ref _bufferRoot, _bufferIndex), _bufferLength - _bufferIndex);
    private readonly void AssertCrlfPastPrefixUnsafe(int offset) => AssertClLfUnsafe(ref _bufferRoot, _bufferIndex + offset + 1);
    private void SetCurrent(ReadOnlySpan<byte> current)
    {
        _positionBase += _bufferLength; // accumulate previous length
        _bufferIndex = 0;
        _bufferLength = current.Length;
        _bufferRoot = ref MemoryMarshal.GetReference(current);
    }
#else
    private ReadOnlySpan<byte> _bufferSpan;
    private readonly ref byte CurrentUnsafe => ref Unsafe.AsRef(in _bufferSpan[_bufferIndex]);
    private readonly RespPrefix PeekPrefix() => (RespPrefix)_bufferSpan[_bufferIndex];
    private readonly ReadOnlySpan<byte> PeekCurrent() => _bufferSpan.Slice(_bufferIndex);
    private readonly ReadOnlySpan<byte> PeekPastPrefix() => _bufferSpan.Slice(_bufferIndex + 1);
    private readonly void AssertCrlfPastPrefixUnsafe(int offset)
        => AssertClLfUnsafe(in _bufferSpan[_bufferIndex + offset + 1]);
    private void SetCurrent(ReadOnlySpan<byte> current)
    {
        _positionBase += _bufferLength; // accumulate previous length
        _bufferIndex = 0;
        _bufferLength = current.Length;
        _bufferSpan = current;
    }
#endif

    public RespReader(byte[] value) : this(new ReadOnlySpan<byte>(value)) { }
    public RespReader(ReadOnlyMemory<byte> value) : this(value.Span) { }
    public RespReader(ReadOnlySpan<byte> value)
    {
        _fullPayload = default;
        _positionBase = _bufferIndex = _bufferLength = 0;
        _length = -1;
        _prefix = RespPrefix.None;
#if NET7_0_OR_GREATER
        _bufferRoot = ref Unsafe.NullRef<byte>();
#else
        _bufferSpan = default;
#endif
        _segPos = default;
        SetCurrent(value);
    }
    public RespReader(scoped in ReadOnlySequence<byte> value)
    {
        _fullPayload = value;
        _positionBase = _bufferIndex = _bufferLength = 0;
        _length = -1;
        _prefix = RespPrefix.None;
#if NET7_0_OR_GREATER
        _bufferRoot = ref Unsafe.NullRef<byte>();
#else
        _bufferSpan = default;
#endif
        if (value.IsSingleSegment)
        {
            _segPos = default;
#if NETCOREAPP3_1_OR_GREATER
            SetCurrent(value.FirstSpan);
#else
            SetCurrent(value.First.Span);
#endif
        }
        else
        {
            _segPos = value.Start;
            if (value.TryGet(ref _segPos, out var current))
            {
                SetCurrent(current.Span);
            }
        }
    }

    private static ReadOnlySpan<byte> CrLf => "\r\n"u8;

    public readonly int ScalarLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Prefix switch
        {
            RespPrefix.SimpleString or RespPrefix.SimpleError or RespPrefix.Integer
            or RespPrefix.Boolean or RespPrefix.Double or RespPrefix.BigNumber
            or RespPrefix.BulkError or RespPrefix.BulkString or RespPrefix.VerbatimString when _length > 0 => _length,
            _ => 0,
        };
    }

    public readonly int ChildCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Prefix switch
        {
            RespPrefix.Array or RespPrefix.Set or RespPrefix.Push when _length > 0 => _length,
            RespPrefix.Map when _length > 0 => 2 * _length,
            _ => 0,
        };
    }

    /// <summary>
    /// Indicates a type with a discreet value - string, integer, etc - <see cref="TryGetValueSpan(out ReadOnlySpan{byte})"/>,
    /// <see cref="Is(ReadOnlySpan{byte})"/>, <see cref="CopyTo(Span{byte})"/> etc are meaningful
    /// </summary>
    public readonly bool IsScalar
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Prefix switch
        {
            RespPrefix.SimpleString or RespPrefix.SimpleError or RespPrefix.Integer
            or RespPrefix.Boolean or RespPrefix.Double or RespPrefix.BigNumber
            or RespPrefix.BulkError or RespPrefix.BulkString or RespPrefix.VerbatimString => true,
            _ => false,
        };
    }

    public readonly bool IsError
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Prefix is RespPrefix.BulkError or RespPrefix.SimpleError;
    }

    internal readonly Exception ReadError()
    {
        var message = ReadString();
        if (string.IsNullOrWhiteSpace(message)) message = "unknown RESP error";
        return new RedisServerException(message!);
    }

    /// <summary>
    /// Indicates a collection type - array, set, etc - <see cref="ChildCount"/>, <see cref="SkipChildren()"/> are are meaningful
    /// </summary>
    public readonly bool IsAggregate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Prefix switch
        {
            RespPrefix.Array or RespPrefix.Set or RespPrefix.Map or RespPrefix.Push => true,
            _ => false,
        };
    }

    private static bool TryReadIntegerCrLf(ReadOnlySpan<byte> bytes, out int value, out int byteCount)
    {
        var end = bytes.IndexOf(CrLf);
        if (end < 0)
        {
            byteCount = value = 0;
            if (bytes.Length >= RespWriter.MaxRawBytesInt32 + 2)
            {
                ThrowProtocolFailure("Unterminated or over-length integer"); // should have failed; report failure to prevent infinite loop
            }
            return false;
        }
        if (!(Utf8Parser.TryParse(bytes, out value, out byteCount) && byteCount == end))
            ThrowProtocolFailure("Unable to parse integer");
        byteCount += 2; // include the CrLf
        return true;
    }

    private static void ThrowProtocolFailure(string message)
        => throw new InvalidOperationException("RESP protocol failure: " + message); // protocol exception?

    public readonly bool IsNull
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length < 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetCurrent()
    {
        _prefix = RespPrefix.None;
        _length = -1;
    }

    private void AdvanceSlow(long bytes)
    {
        while (bytes > 0)
        {
            var available = _bufferLength - _bufferIndex;
            if (bytes <= available)
            {
                _bufferIndex += (int)bytes;
                return;
            }
            bytes -= available;
            if (_fullPayload.IsSingleSegment || !_fullPayload.TryGet(ref _segPos, out var next))
            {
                throw new EndOfStreamException();
            }
            SetCurrent(next.Span);
        }
    }

    /// <summary>
    /// Body length of scalar values, plus any terminating sentinels
    /// </summary>
    private readonly int TrailingLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsScalar && _length >= 0 ? _length + 2 : 0;
    }

    public bool ReadNext()
    {
        var skip = TrailingLength;
        if (_bufferIndex + skip <= _bufferLength)
        {
            _bufferIndex += skip; // available in the current buffer
        }
        else
        {
            AdvanceSlow(skip);
        }
        ResetCurrent();

        if (_bufferIndex + 3 <= _bufferLength) // shortest possible RESP fragment is length 3
        {
            switch (_prefix = PeekPrefix())
            {
                case RespPrefix.SimpleString:
                case RespPrefix.SimpleError:
                case RespPrefix.Integer:
                case RespPrefix.Boolean:
                case RespPrefix.Double:
                case RespPrefix.BigNumber:
                    // CRLF-terminated
                    _length = PeekPastPrefix().IndexOf(CrLf);
                    if (_length < 0) break; // can't find, need more data
                    _bufferIndex++; // skip past prefix (payload follows directly)
                    return true;
                case RespPrefix.BulkError:
                case RespPrefix.BulkString:
                case RespPrefix.VerbatimString:
                    // length prefix with value payload
                    var remaining = PeekPastPrefix();
                    if (!TryReadIntegerCrLf(remaining, out _length, out int consumed)) break;
                    if (_length >= 0) // not null (nulls don't have second CRLF)
                    {
                        // still need to valid terminating CRLF
                        if (remaining.Length < consumed + _length + 2) break; // need more data
                        AssertCrlfPastPrefixUnsafe(consumed + _length);
                    }
                    _bufferIndex += 1 + consumed;
                    return true;
                case RespPrefix.Array:
                case RespPrefix.Set:
                case RespPrefix.Map:
                case RespPrefix.Push:
                    // length prefix without value payload (child values follow)
                    if (!TryReadIntegerCrLf(PeekPastPrefix(), out _length, out consumed)) break;
                    _bufferIndex += consumed + 1;
                    return true;
                case RespPrefix.Null: // null
                    // note we already checked we had 3 bytes
                    AssertCrlfPastPrefixUnsafe(0);
                    _length = -1;
                    _bufferIndex += 3; // skip prefix+terminator
                    return true;
                default:
                    ThrowProtocolFailure("Unexpected protocol prefix: " + _prefix);
                    return false;
            }
        }

        return TryReadNextSlow();
    }

    /// <inheritdoc/>
    public override readonly string ToString()
    {
        if (IsScalar) return IsNull ? $"@{BytesConsumed} {Prefix}: {nameof(RespPrefix.Null)}" : $"@{BytesConsumed} {Prefix} with {ScalarLength} bytes '{ReadString()}'";
        if (IsAggregate) return IsNull ? $"@{BytesConsumed} {Prefix}: {nameof(RespPrefix.Null)}" : $"@{BytesConsumed} {Prefix} with {ChildCount} sub-items";
        return $"@{BytesConsumed} {Prefix}";
    }

    private bool NeedMoreData()
    {
        ResetCurrent();
        return false;
    }
    private bool TryReadNextSlow()
    {
        ResetCurrent();
        var reader = new SlowReader(in this);
        int next = reader.TryRead();
        if (next < 0) return NeedMoreData();
        switch (_prefix = (RespPrefix)next)
        {
            case RespPrefix.SimpleString:
            case RespPrefix.SimpleError:
            case RespPrefix.Integer:
            case RespPrefix.Boolean:
            case RespPrefix.Double:
            case RespPrefix.BigNumber:
                // CRLF-terminated
                if (!reader.TryFindCrLfWithoutMoving(out _length)) return NeedMoreData();
                break;
            case RespPrefix.BulkError:
            case RespPrefix.BulkString:
            case RespPrefix.VerbatimString:
                // length prefix with value payload
                if (!reader.TryReadLengthCrLf(out _length)) return NeedMoreData();
                if (!reader.TryAssertBytesCrLfWithoutMoving(_length)) return NeedMoreData();
                break;
            case RespPrefix.Array:
            case RespPrefix.Set:
            case RespPrefix.Map:
            case RespPrefix.Push:
                // length prefix without value payload (child values follow)
                if (!reader.TryReadLengthCrLf(out _length)) return NeedMoreData();
                break;
            case RespPrefix.Null: // null
                if (!reader.TryReadCrLf()) return NeedMoreData();
                break;
            default:
                ThrowProtocolFailure("Unexpected protocol prefix: " + _prefix);
                return NeedMoreData();
        }
        AdvanceSlow(reader.TotalConsumed);
        return true;
    }

    private ref partial struct SlowReader
    {
        public SlowReader(in RespReader reader)
        {
#if NET7_0_OR_GREATER
            _full = ref reader._fullPayload;
#else
            _full = reader._fullPayload;
#endif
            _segPos = reader._segPos;
            _current = reader.PeekCurrent();
            _totalBase = _index = 0;
            DebugAssertValid();
        }

        [Conditional("DEBUG")]
        readonly partial void DebugAssertValid();
#if DEBUG
        readonly partial void DebugAssertValid()
        {
            Debug.Assert(_index >= 0 && _index <= _current.Length);
        }
#endif

        private bool TryAdvanceToData()
        {
            DebugAssertValid();
            while (CurrentRemainingBytes == 0)
            {
                if (_full.IsSingleSegment || !_full.TryGet(ref _segPos, out var next))
                {
                    return false;
                }
                _totalBase += _current.Length; // accumulate prior
                _current = next.Span;
                _index = 0;
            }
            DebugAssertValid();
            return true;
        }

        public int TryRead()
        {
            if (CurrentRemainingBytes == 0 && !TryAdvanceToData()) return -1;
            return _current[_index++];
        }

        private int CurrentRemainingBytes
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _current.Length - _index;
        }
        internal bool TryReadCrLf() // assert and advance
        {
            DebugAssertValid();
            if (CurrentRemainingBytes >= 2)
            {
                AssertClLfUnsafe(in _current[_index]);
                _index += 2;
                return true;
            }

            var x = TryRead();
            if (x < 0) return false;
            if (x != '\r') ThrowProtocolFailure("Expected CR/LF");
            x = TryRead();
            if (x < 0) return false;
            if (x != '\n') ThrowProtocolFailure("Expected CR/LF");
            return true;
        }

        internal bool TryReadLengthCrLf(out int length)
        {
            if (CurrentRemainingBytes >= RespWriter.MaxRawBytesInt32 + 2)
            {
                if (TryReadIntegerCrLf(_current.Slice(_index), out length, out int consumed))
                {
                    _index += consumed;
                    return true;
                }
            }
            else
            {
                Span<byte> buffer = stackalloc byte[RespWriter.MaxRawBytesInt32 + 2];
                SlowReader snapshot = this; // we might over-advance when filling the buffer
                length = snapshot.Fill(buffer);
                if (TryReadIntegerCrLf(buffer.Slice(0, length), out length, out int consumed))
                {
                    // we expect this to work - we just aw the bytes!
                    if (!TryAdvance(consumed)) Throw();
                    return true;

                    static void Throw() => throw new InvalidOperationException("Unexpected failure to advance in " + nameof(TryReadLengthCrLf));
                }
            }
            return false;
        }

        internal int Fill(scoped Span<byte> buffer)
        {
            DebugAssertValid();
            int total = 0;
            while (buffer.Length > 0 && TryAdvanceToData())
            {
                int available = CurrentRemainingBytes;
                if (available >= buffer.Length)
                {
                    // we have enough to finish
                    _current.Slice(_index, buffer.Length).CopyTo(buffer);
                    _index += buffer.Length;
                    total += buffer.Length;
                    break;
                }

                // not enough; copy what we have
                var source = _current.Slice(_index);
                source.CopyTo(buffer);
                _index += available;
                total += available;
                buffer = buffer.Slice(available);
            }
            DebugAssertValid();
            return total;
        }

        private bool TryAdvance(int bytes)
        {
            DebugAssertValid();
            while (bytes > 0 && TryAdvanceToData())
            {
                var available = CurrentRemainingBytes;
                if (bytes <= available)
                {
                    _index += bytes;
                    return true;
                }
                _index += available;
                bytes -= available;
            }
            DebugAssertValid();
            return bytes == 0;
        }

        internal readonly bool TryAssertBytesCrLfWithoutMoving(int length)
        {
            SlowReader copy = this;
            return copy.TryAdvance(length) && copy.TryReadCrLf();
        }

        internal readonly bool TryFindCrLfWithoutMoving(out int length)
        {
            DebugAssertValid();
            SlowReader copy = this; // don't want to advance
            length = 0;
            while (copy.TryAdvanceToData())
            {
                var index = copy._current.Slice(copy._index).IndexOf((byte)'\r');
                if (index >= 0)
                {
                    length += index;
                    if (!(copy.TryAdvance(index) && copy.TryReadCrLf())) ThrowProtocolFailure("Expected CR/LF");
                    return true;
                }
                var scanned = copy.CurrentRemainingBytes;
                length += scanned;
                copy._index += scanned;
            }
            return false;
        }

#if NET7_0_OR_GREATER
        private readonly ref readonly ReadOnlySequence<byte> _full;
#else
        private readonly ReadOnlySequence<byte> _full;
#endif
        private SequencePosition _segPos;
        private ReadOnlySpan<byte> _current;
        private int _index;
        private long _totalBase;
        public long TotalConsumed => _totalBase + _index;

    }

    /// <summary>Performs a byte-wise equality check on the payload</summary>
    public readonly bool Is(ReadOnlySpan<byte> value)
    {
        if (!(IsScalar && value.Length == _length)) return false;
        if (TryGetValueSpan(out var span))
        {
            return span.SequenceEqual(value);
        }
        return IsSlow(value);
    }
    private readonly bool IsSlow(ReadOnlySpan<byte> value)
    {
        // TODO: multi-segment IsSlow
        throw new NotImplementedException();
    }

    internal readonly bool IsOK() // go mad with this, because it is used so often
        => _length == 2 & _bufferIndex + 2 <= _bufferLength // single-buffer fast path - can we safely read 2 bytes?
        ? Prefix == RespPrefix.SimpleString & Unsafe.ReadUnaligned<ushort>(ref CurrentUnsafe) == OK
        : IsOKSlow();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private readonly bool IsOKSlow() => _length == 2 && Prefix == RespPrefix.SimpleString && IsSlow("OK"u8);

    // note this should be treated as "const" by modern JIT
    private static readonly ushort OK = BitConverter.IsLittleEndian ? (ushort)0x4F4B : (ushort)0x4B4F; // see: ASCII

    /// <summary>
    /// Skips all child/descendent nodes of this element, returning the number
    /// of elements skipped
    /// </summary>
    public int SkipChildren()
    {
        int remaining = ChildCount, total = 0;
        while (remaining > 0 && ReadNext())
        {
            total++;
            remaining = remaining - 1 + ChildCount;
        }
        if (remaining != 0) ThrowEOF();
        if (total != 0)
        {
            ResetCurrent(); // would be confusing to see the last descendent state
        }
        return total;
    }
    [DoesNotReturn]
    internal static void ThrowEOF() => throw new EndOfStreamException();
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
[SuppressMessage("Usage", "CA2231:Overload operator equals on overriding value type Equals", Justification = "API not necessary here")]
public readonly struct RequestBuffer
{
    private readonly ReadOnlySequence<byte> _buffer;
    private readonly int _preambleIndex, _payloadIndex;

    public long Length => _buffer.Length - _preambleIndex;

    private RequestBuffer(in ReadOnlySequence<byte> buffer, int preambleIndex, int payloadIndex)
    {
        _buffer = buffer;
        _preambleIndex = preambleIndex;
        _payloadIndex = payloadIndex;
    }

    internal RequestBuffer(in ReadOnlySequence<byte> buffer, int payloadIndex)
    {
        _buffer = buffer;
        _preambleIndex = _payloadIndex = payloadIndex;
    }

    public bool TryGetSpan(out ReadOnlySpan<byte> span)
    {
        var buffer = GetBuffer(); // handle preamble
        if (buffer.IsSingleSegment)
        {
#if NETCOREAPP3_1_OR_GREATER
            span = buffer.FirstSpan;
#else
            span = buffer.First.Span;
#endif
            return true;
        }
        span = default;
        return false;
    }

    public ReadOnlySequence<byte> GetBuffer() => _preambleIndex == 0 ? _buffer : _buffer.Slice(_preambleIndex);

    /// <summary>
    /// Gets a text (UTF8) representation of the RESP payload; this API is intended for debugging purposes only, and may
    /// be misleading for non-UTF8 payloads.
    /// </summary>
    public override string ToString()
    {
        var length = Length;
        if (length == 0) return "";
        if (length > 1024) return $"({length} bytes)";
        var buffer = GetBuffer();
#if NET6_0_OR_GREATER
        return RespWriter.UTF8.GetString(buffer);
#else
#if NETCOREAPP3_0_OR_GREATER
        if (buffer.IsSingleSegment)
        {
            return RespWriter.UTF8.GetString(buffer.FirstSpan);
        }
#endif
        var arr = ArrayPool<byte>.Shared.Rent((int)length);
        buffer.CopyTo(arr);
        var s = RespWriter.UTF8.GetString(arr, 0, (int)length);
        ArrayPool<byte>.Shared.Return(arr);
        return s;
#endif
    }

    /// <summary>
    /// Releases all buffers associated with this instance.
    /// </summary>
    public void Recycle()
    {
        var buffer = _buffer;
        // nuke self (best effort to prevent multi-release)
        Unsafe.AsRef(in this) = default;
        new LeasedSequence<byte>(buffer).Dispose();
    }

    /// <summary>
    /// Prepends the given preamble contents 
    /// </summary>
    public RequestBuffer WithPreamble(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty) return this; // trivial

        int length = value.Length, preambleIndex = _preambleIndex - length;
        if (preambleIndex < 0) Throw();
        var target = _buffer.Slice(preambleIndex, length);
        if (target.IsSingleSegment)
        {
            value.CopyTo(MemoryMarshal.AsMemory(target.First).Span);
        }
        else
        {
            MultiCopy(in target, value);
        }
        return new(_buffer, preambleIndex, _payloadIndex);

        static void Throw() => throw new InvalidOperationException("There is insufficient capacity to add the requested preamble");

        static void MultiCopy(in ReadOnlySequence<byte> buffer, ReadOnlySpan<byte> source)
        {
            // note that we've already asserted that the source is non-trivial
            var iter = buffer.GetEnumerator();
            while (iter.MoveNext())
            {
                var target = MemoryMarshal.AsMemory(iter.Current).Span;
                if (source.Length <= target.Length)
                {
                    source.CopyTo(target);
                    return;
                }
                source.Slice(0, target.Length).CopyTo(target);
                source = source.Slice(target.Length);
                Debug.Assert(!source.IsEmpty);
            }
            Debug.Assert(!source.IsEmpty);
            Throw();
            static void Throw() => throw new InvalidOperationException("Insufficient target space");
        }
    }

    /// <summary>
    /// Removes all preamble, reverting to just the original payload
    /// </summary>
    public RequestBuffer WithoutPreamble() => new RequestBuffer(_buffer, _payloadIndex, _payloadIndex);
    internal string GetCommand()
    {
        var buffer = WithoutPreamble().GetBuffer();
        var reader = new RespReader(in buffer);
        if (reader.ReadNext() && reader.Prefix == RespPrefix.Array && reader.ChildCount > 0
            && reader.ReadNext() && reader.Prefix == RespPrefix.BulkString)
        {
            return reader.ReadString() ?? "";
        }
        else
        {
            return "(unexpected RESP)";
        }
    }

    [Conditional("DEBUG")]
    internal void DebugValidateGenericFragment()
    {
#if DEBUG
        var buffer = WithoutPreamble().GetBuffer();
        Debug.Assert(!buffer.IsEmpty, "buffer should not be empty");
        var reader = new RespReader(in buffer);
        int remaining = 1;
        while (remaining > 0)
        {
            if (!reader.ReadNext()) RespReader.ThrowEOF();
            remaining = remaining - 1 + reader.ChildCount;
        }
        Debug.Assert(remaining == 0, "should have zero outstanding RESP fragments");
        Debug.Assert(reader.BytesConsumed == buffer.Length, "should be fully consumed");
#endif
    }

    [Conditional("DEBUG")]
    internal void DebugValidateCommand()
    {
#if DEBUG
        var buffer = WithoutPreamble().GetBuffer();
        Debug.Assert(!buffer.IsEmpty, "buffer should not be empty");
        var reader = new RespReader(in buffer);
        if (!reader.ReadNext()) RespReader.ThrowEOF();
        Debug.Assert(reader.Prefix == RespPrefix.Array, "root must be an array");
        Debug.Assert(reader.ChildCount > 0, "must have at least one element");
        int count = reader.ChildCount;
        for (int i = 0; i < count; i++)
        {
            if (!reader.ReadNext()) RespReader.ThrowEOF();
            Debug.Assert(reader.Prefix == RespPrefix.BulkString, "all parameters must be bulk strings");
        }
        Debug.Assert(!reader.ReadNext(), "should be nothing left");
        Debug.Assert(reader.BytesConsumed == buffer.Length, "should be fully consumed");
#endif
    }
}

[Experimental(RespRequest.ExperimentalDiagnosticID)]
public ref struct RespWriter
{
    private RotatingBufferCore _buffer;
    private readonly int _preambleReservation;
    private int _argCountIncludingCommand, _argIndexIncludingCommand;

    internal static RespWriter Create(SlabManager? slabManager = null, int preambleReservation = 64)
        => new(slabManager ?? SlabManager.Ambient, preambleReservation);

    private RespWriter(SlabManager slabManager, int preambleReservation)
    {
        _preambleReservation = preambleReservation;
        _argCountIncludingCommand = _argIndexIncludingCommand = 0;
        _buffer = new(slabManager);
        _buffer.Commit(preambleReservation);
    }

    internal const int MaxRawBytesInt32 = 10,
        MaxProtocolBytesIntegerInt32 = MaxRawBytesInt32 + 3, // ?X10X\r\n where ? could be $, *, etc - usually a length prefix
        MaxProtocolBytesBulkStringInt32 = MaxRawBytesInt32 + 7; // $10\r\nX10X\r\n
    /*
                    MaxBytesInt64 = 26, // $19\r\nX19X\r\n
                    MaxBytesSingle = 27; // $NN\r\nX...X\r\n - note G17 format, allow 20 for payload
    */

    private const int NullLength = 5; // $-1\r\n 

    internal void Recycle() => _buffer.Dispose();

    internal static readonly UTF8Encoding UTF8 = new(false);

    public void WriteCommand(string command, int argCount) => WriteCommand(command.AsSpan(), argCount);

    private const int MAX_UTF8_BYTES_PER_CHAR = 4, MAX_CHARS_FOR_STACKALLOC_ENCODE = 64,
        ENCODE_STACKALLOC_BYTES = MAX_CHARS_FOR_STACKALLOC_ENCODE * MAX_UTF8_BYTES_PER_CHAR;

    public void WriteCommand(scoped ReadOnlySpan<char> command, int argCount)
    {
        if (command.Length <= MAX_CHARS_FOR_STACKALLOC_ENCODE)
        {
            WriteCommand(Utf8Encode(command, stackalloc byte[ENCODE_STACKALLOC_BYTES]), argCount);
        }
        else
        {
            WriteCommandSlow(ref this, command, argCount);
        }

        static void WriteCommandSlow(ref RespWriter @this, scoped ReadOnlySpan<char> command, int argCount)
        {
            @this.WriteCommand(Utf8EncodeLease(command, out var lease), argCount);
            ArrayPool<byte>.Shared.Return(lease);
        }
    }

    private static unsafe ReadOnlySpan<byte> Utf8Encode(scoped ReadOnlySpan<char> source, Span<byte> target)
    {
        int len;
#if NETCOREAPP3_1_OR_GREATER
        len = UTF8.GetBytes(source, target);
#else
        fixed (byte* bPtr = target)
        fixed (char* cPtr = source)
        {
            len = UTF8.GetBytes(cPtr, source.Length, bPtr, target.Length);
        }
#endif
        return target.Slice(0, len);
    }
    private static ReadOnlySpan<byte> Utf8EncodeLease(scoped ReadOnlySpan<char> value, out byte[] arr)
    {
        arr = ArrayPool<byte>.Shared.Rent(MAX_UTF8_BYTES_PER_CHAR * value.Length);
        int len;
#if NETCOREAPP3_1_OR_GREATER
        len = UTF8.GetBytes(value, arr);
#else
        unsafe
        {
            fixed (char* cPtr = value)
            fixed (byte* bPtr = arr)
            {
                len = UTF8.GetBytes(cPtr, value.Length, bPtr, arr.Length);
            }
        }
#endif
        return new ReadOnlySpan<byte>(arr, 0, len);
    }
    internal readonly void AssertFullyWritten()
    {
        if (_argCountIncludingCommand != _argIndexIncludingCommand) Throw(_argIndexIncludingCommand, _argCountIncludingCommand);

        static void Throw(int count, int total) => throw new InvalidOperationException($"Not all command arguments ({count - 1} of {total - 1}) have been written");
    }
    public void WriteCommand(scoped ReadOnlySpan<byte> command, int argCount)
    {
        if (_argCountIncludingCommand > 0) ThrowCommandAlreadyWritten();
        if (command.IsEmpty) ThrowEmptyCommand();
        if (argCount < 0) ThrowNegativeArgs();
        _argCountIncludingCommand = argCount + 1;
        _argIndexIncludingCommand = 1;

        var payloadAndFooter = command.Length + 2;

        // optimize for single buffer-fetch path
        var worstCase = MaxProtocolBytesIntegerInt32 + MaxProtocolBytesIntegerInt32 + command.Length + 2;
        if (_buffer.TryGetWritableSpan(worstCase, out var span))
        {
            ref byte head = ref MemoryMarshal.GetReference(span);
            var header = WriteCountPrefix(RespPrefix.Array, _argCountIncludingCommand, span);
#if NETCOREAPP3_1_OR_GREATER
            header += WriteCountPrefix(RespPrefix.BulkString, command.Length,
                MemoryMarshal.CreateSpan(ref Unsafe.Add(ref head, header), MaxProtocolBytesIntegerInt32));
            command.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref head, header), command.Length));
#else
            header += WriteCountPrefix(RespPrefix.BulkString, command.Length, span.Slice(header));
            command.CopyTo(span.Slice(header));
#endif

            Unsafe.WriteUnaligned(ref Unsafe.Add(ref head, header + command.Length), CrLf);
            _buffer.Commit(header + command.Length + 2);
            return; // yay!
        }

        // slow path, multiple buffer fetches
        WriteCountPrefix(RespPrefix.Array, _argCountIncludingCommand);
        WriteCountPrefix(RespPrefix.BulkString, command.Length);
        WriteRaw(command);
        WriteRaw(CrlfBytes);


        static void ThrowCommandAlreadyWritten() => throw new InvalidOperationException(nameof(WriteCommand) + " can only be called once");
        static void ThrowEmptyCommand() => throw new ArgumentOutOfRangeException(nameof(command), "command cannot be empty");
        static void ThrowNegativeArgs() => throw new ArgumentOutOfRangeException(nameof(argCount), "argCount cannot be negative");
    }

    private static int WriteCountPrefix(RespPrefix prefix, int count, Span<byte> target)
    {
        var len = Format.FormatInt32(count, target.Slice(1)); // we only want to pay for this one slice
        if (target.Length < len + 3) Throw();
        ref byte head = ref MemoryMarshal.GetReference(target);
        head = (byte)prefix;
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref head, len + 1), CrLf);
        return len + 3;

        static void Throw() => throw new InvalidOperationException("Insufficient buffer space to write count prefix");
    }

    private void WriteNullString() // private because I don't think this is allowed in client streams? check
        => WriteRaw("$-1\r\n"u8);

    private void WriteEmptyString() // private because I don't think this is allowed in client streams? check
        => WriteRaw("$0\r\n\r\n"u8);

    internal void WriteRaw(scoped ReadOnlySpan<byte> value)
    {
        while (!value.IsEmpty)
        {
            var target = _buffer.GetWritableTail().Span;
            Debug.Assert(!target.IsEmpty, "need something!");

            if (target.Length >= value.Length)
            {
                // it all fits
                value.CopyTo(target);
                _buffer.Commit(value.Length);
                return;
            }

            // write what we can
            value.Slice(target.Length).CopyTo(target);
            _buffer.Commit(target.Length);
            value = value.Slice(target.Length);
        }
    }

    private void AddArg()
    {
        if (_argIndexIncludingCommand >= _argCountIncludingCommand) ThrowAllWritten(_argCountIncludingCommand);
        _argIndexIncludingCommand++;

        static void ThrowAllWritten(int advertised) => throw new InvalidOperationException($"All command arguments ({advertised - 1}) have already been written");
    }

    public void WriteValue(scoped ReadOnlySpan<byte> value)
    {
        AddArg();
        if (value.IsEmpty)
        {
            WriteEmptyString();
            return;
        }
        // optimize for fitting everything into a single buffer-fetch
        var payloadAndFooter = value.Length + 2;
        var worstCase = MaxProtocolBytesIntegerInt32 + payloadAndFooter;
        if (_buffer.TryGetWritableSpan(worstCase, out var span))
        {
            ref byte head = ref MemoryMarshal.GetReference(span);
            var header = WriteCountPrefix(RespPrefix.BulkString, value.Length, span);
#if NETCOREAPP3_1_OR_GREATER
            value.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref head, header), payloadAndFooter));
#else
            value.CopyTo(span.Slice(header));
#endif
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref head, header + value.Length), CrLf);
            _buffer.Commit(header + payloadAndFooter);
            return; // yay!
        }

        // slow path - involves multiple buffer fetches
        WriteCountPrefix(RespPrefix.BulkString, value.Length);
        WriteRaw(value);
        WriteRaw(CrlfBytes);
    }

    private void WriteCountPrefix(RespPrefix prefix, int count)
    {
        Span<byte> buffer = stackalloc byte[MaxProtocolBytesIntegerInt32];
        WriteRaw(buffer.Slice(0, WriteCountPrefix(prefix, count, buffer)));
    }

    internal static readonly ushort CrLf = BitConverter.IsLittleEndian ? (ushort)0x0A0D : (ushort)0x0D0A; // see: ASCII

    internal static ReadOnlySpan<byte> CrlfBytes => "\r\n"u8;

    public void WriteValue(scoped ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            AddArg();
            WriteEmptyString();
        }
        else if (value.Length <= MAX_CHARS_FOR_STACKALLOC_ENCODE)
        {
            WriteValue(Utf8Encode(value, stackalloc byte[ENCODE_STACKALLOC_BYTES]));
        }
        else
        {
            WriteValue(Utf8EncodeLease(value, out var lease));
            ArrayPool<byte>.Shared.Return(lease);
        }
    }

    public void WriteValue(string value)
    {
        if (value is null)
        {
            AddArg();
            WriteNullString();
        }
        else WriteValue(value.AsSpan());
    }

    internal RequestBuffer Detach() => new RequestBuffer(_buffer.Detach(), _preambleReservation);
}

internal struct RotatingBufferCore : IDisposable, IBufferWriter<byte> // note mutable struct intended to encapsulate logic as a field inside a class instance
{
    private readonly SlabManager _slabManager;
    private RefCountedSequenceSegment<byte> _head, _tail;
    private readonly long _maxLength;
    private int _headOffset, _tailOffset, _tailSize;
    internal readonly long MaxLength => _maxLength;

    public SlabManager SlabManager => _slabManager;
    public RotatingBufferCore(SlabManager slabManager, int maxLength = 0)
    {
        if (maxLength <= 0) maxLength = int.MaxValue;
        _maxLength = maxLength;
        _headOffset = _tailOffset = _tailSize = 0;
        _slabManager = slabManager;
        Expand();
    }

    /// <summary>
    /// The immediately available contiguous bytes in the current buffer (or next buffer, if none)
    /// </summary>
    public readonly int AvailableBytes
    {
        get
        {
            var remaining = _tailSize - _tailOffset;
            return remaining == 0 ? _slabManager.ChunkSize : remaining;
        }
    }

    [MemberNotNull(nameof(_head))]
    [MemberNotNull(nameof(_tail))]
    private void Expand()
    {
        Debug.Assert(_tail is null || _tailOffset == _tail.Memory.Length, "tail page should be full");
        if (MaxLength > 0 && (GetBuffer().Length + _slabManager.ChunkSize) > MaxLength) ThrowQuota();

        var next = new RefCountedSequenceSegment<byte>(_slabManager.GetChunk(out var chunk), chunk, _tail);
        _tail = next;
        _tailOffset = 0;
        _tailSize = next.Memory.Length;
        if (_head is null)
        {
            _head = next;
            _headOffset = 0;
        }

        static void ThrowQuota() => throw new InvalidOperationException("Buffer quota exceeded");
    }

    public bool TryGetWritableSpan(int minSize, out Span<byte> span)
    {
        if (minSize <= AvailableBytes) // don't pay lookup cost if impossible
        {
            span = GetWritableTail().Span;
            return span.Length >= minSize;
        }
        span = default;
        return false;
    }

    public Memory<byte> GetWritableTail()
    {
        if (_tailOffset == _tailSize)
        {
            Expand();
        }
        // definitely something available; return the gap
        return MemoryMarshal.AsMemory(_tail.Memory).Slice(_tailOffset);
    }
    public readonly ReadOnlySequence<byte> GetBuffer() => _head is null ? default : new(_head, _headOffset, _tail, _tailOffset);
    internal void Commit(int bytes) // unlike Advance, this remains valid for data outside what has been written
    {
        if (bytes >= 0 && bytes <= _tailSize - _tailOffset)
        {
            _tailOffset += bytes;
        }
        else
        {
            CommitSlow(bytes);
        }
    }
    private void CommitSlow(int bytes) // multi-segment commits (valid even though it remains unwritten) and error-cases
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        while (bytes > 0)
        {
            var space = _tailSize - _tailOffset;
            if (bytes <= space)
            {
                _tailOffset += bytes;
            }
            else
            {
                _tailOffset += space;
                Expand(); // need more
            }
            bytes -= space;
        }
    }

    /// <summary>
    /// Detaches the entire committed chain to the caller without leaving things in a resumable state
    /// </summary>
    public ReadOnlySequence<byte> Detach()
    {
        var all = GetBuffer();
        _head = _tail = null!;
        _headOffset = _tailOffset = _tailSize = 0;
        return all;
    }

    /// <summary>
    /// Detaches the head portion of the committed chain, retaining the rest of the buffered data
    /// for additional use
    /// </summary>
    public ReadOnlySequence<byte> DetachRotating(long bytes)
    {
        // semantically, we're going to AddRef on all the nodes in take, and then
        // drop (and Dispose()) all nodes that we no longer need; but this means
        // that the only shared segment is the first one (and only if there is data left),
        // so we can manually check that one segment, rather than walk two chains
        var all = GetBuffer();
        var take = all.Slice(0, bytes);

        var end = take.End;
        var endSegment = (RefCountedSequenceSegment<byte>)end.GetObject()!;

        var bytesLeftLastPage = endSegment.Memory.Length - end.GetInteger();
        if (bytesLeftLastPage != 0 && (
            bytesLeftLastPage >= 64 // worth using for the next read, regardless
            || endSegment.Next is not null // we've already allocated another page, which means this page is full
            || _tailOffset != end.GetInteger() // (^^ final page) & we have additional read bytes
            ))
        {
            // keep sharing the last page of the outbound / first page of retained
            endSegment.AddRef();
            _head = endSegment;
            _headOffset = end.GetInteger();
        }
        else
        {
            // move to the next page
            _headOffset = 0;
            if (endSegment.Next is null)
            {
                // no next page buffered; reset completely
                Debug.Assert(ReferenceEquals(endSegment, _tail));
                _head = _tail = null!;
                Expand();
            }
            else
            {
                // start fresh from the next page
                var next = endSegment.Next;
                endSegment.Next = null; // walk never needed
                _head = next;
            }
        }
        return take;
    }

    public void Dispose()
    {
        LeasedSequence<byte> leased = new(GetBuffer());
        _head = _tail = null!;
        _headOffset = _tailOffset = _tailSize = 0;
        leased.Dispose();
    }

    void IBufferWriter<byte>.Advance(int count) => Commit(count);
    Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint) => GetWritableTail();
    Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint) => GetWritableTail().Span;
}
