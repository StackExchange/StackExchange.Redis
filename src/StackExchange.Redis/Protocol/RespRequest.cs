//using Pipelines.Sockets.Unofficial.Arenas;
//using System;
//using System.Buffers;
//using System.Buffers.Text;
//#if NET8_0_OR_GREATER
//using System.Collections.Frozen;
//#endif
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Diagnostics.CodeAnalysis;
//using System.IO;
//using System.Linq;
//using System.Runtime.CompilerServices;
//using System.Runtime.InteropServices;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;

//#pragma warning disable CS1591 // new API

//namespace StackExchange.Redis.Protocol;

//[Experimental(ExperimentalDiagnosticID)]
//public abstract class RespRequest
//{
//    internal const string ExperimentalDiagnosticID = "SERED002";
//    protected RespRequest() { }
//    public abstract void Write(ref RespWriter writer);
//}



//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//internal unsafe sealed class UnsafeFixedSimpleResponse : IRespReader
//{
//    private readonly byte* _ptr;
//    private readonly int _length;
//    private string? _message;
//    public UnsafeFixedSimpleResponse(ReadOnlySpan<byte> fixedMessage) // caller **must** use a fixed span; "..."u8 satisfies this
//    {
//        _length = fixedMessage.Length;
//        _ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(fixedMessage));
//    }

//    void IRespReader.Read(ref RespReader reader)
//    {
//        if (!(reader.Prefix == RespPrefix.SimpleString && reader.Is(new(_ptr, _length)))) Throw();

//    }
//    [DoesNotReturn]
//    private void Throw() => throw new InvalidOperationException(Message);
//    private string Message => _message ??= $"Did not receive expected response: '+{Encoding.ASCII.GetString(_ptr, _length)}'";
//}



//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//internal class NewCommandMap
//{
//    public static NewCommandMap Create(Dictionary<string, string?> overrides)
//    {
//        if (overrides is { Count: > 0 })
//        {
//            IDictionary<string, string?> typed = overrides;
//            PreparedRespWriters prepared = new(in PreparedRespWriters.Default, ref typed);
//            if (overrides.Count > 0)
//            {
//                return new(typed, prepared);
//            }
//        }
//        return Default;
//    }

//    public static NewCommandMap Default { get; } = new(null, PreparedRespWriters.Default);

//    private readonly PreparedRespWriters _commands;
//    internal ref readonly PreparedRespWriters RawCommands => ref _commands; // avoid stack copies; this is large

//    private NewCommandMap(IDictionary<string, string?>? overrides, in PreparedRespWriters commands)
//    {
//        _commands = commands;
//#if NET8_0_OR_GREATER
//        _overrides = ((FrozenDictionary<string, string?>?)overrides) ?? FrozenDictionary<string, string?>.Empty;
//#else
//        _overrides = ((Dictionary<string, string?>?)overrides) ?? SharedEmpty;
//#endif
//    }

//    internal string? Normalize(string command, out RedisCommand known)
//    {
//        if (string.IsNullOrWhiteSpace(command))
//        {
//            known = RedisCommand.UNKNOWN;
//            return null;
//        }
//        if (!Enum.TryParse(command, true, out known) || known == RedisCommand.NONE)
//        {
//            known = RedisCommand.UNKNOWN;
//        }
//        if (_overrides.TryGetValue(command, out var mapped)) return mapped;

//        if (known != RedisCommand.UNKNOWN)
//        {
//            // normalize case (this is non-allocating for known enum values)
//            command = known.ToString(); 
//        }
//        return command;
//    }

//#if NET8_0_OR_GREATER
//    private readonly FrozenDictionary<string, string?> _overrides;
//#else
//    private readonly Dictionary<string, string?> _overrides;
//    private static readonly Dictionary<string, string?> SharedEmpty = new();
//#endif

//    internal readonly struct PreparedRespWriters
//    {
//        private static readonly PreparedRespWriters _default = new(true);
//        public static ref readonly PreparedRespWriters Default => ref _default; // avoid stack copies; this is large

//        public readonly IRespWriter? Ping;
//        public readonly IRespWriter? Quit;

//        internal PreparedRespWriters(in PreparedRespWriters template, ref IDictionary<string, string?> overrides)
//        {
//            this = template;
//            // we want a defensive copy of the overrides (for Execute etc); make sure it is case-insensitive, ignore X=X, and UC
//            var deltas = from pair in overrides
//                         let value = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim().ToUpperInvariant()
//                         where !string.IsNullOrWhiteSpace(pair.Key) // ignore "" etc
//                            && pair.Key.Trim() == pair.Key // ignore " PING" etc - this is a no-op for valid data
//                            && !StringComparer.OrdinalIgnoreCase.Equals(pair.Key, value) // ignore "PING=PING"
//                         select new KeyValuePair<string,string?>(pair.Key, value); // upper-casify
//#if NET8_0_OR_GREATER
//            overrides = FrozenDictionary.ToFrozenDictionary(deltas, StringComparer.OrdinalIgnoreCase);
//#else
//            overrides = new Dictionary<string, string?>(overrides.Count, StringComparer.OrdinalIgnoreCase);
//            foreach (var pair in deltas) overrides.Add(pair.Key, pair.Value);
//#endif
//            if (overrides.TryGetValue("ping", out string? cmd)) Ping = RawFixedMessageWriter.Create(cmd);
//            if (overrides.TryGetValue("quit", out cmd)) Quit = RawFixedMessageWriter.Create(cmd);

//        }
//        private PreparedRespWriters(bool dummy)
//        {
//            _ = dummy;
//            Ping = new RawUnsafeFixedMessageWriter("*1\r\n$4\r\nPING\r\n"u8);
//            Quit = new RawUnsafeFixedMessageWriter("*1\r\n$4\r\nQUIT\r\n"u8);
//        }
//    }
//}

//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//internal unsafe sealed class RawUnsafeFixedMessageWriter : IRespWriter
//{
//    private readonly byte* _ptr;
//    private readonly int _length;
//    public RawUnsafeFixedMessageWriter(ReadOnlySpan<byte> fixedMessage) // caller **must** use a fixed span; "..."u8 satisfies this
//    {
//        _length = fixedMessage.Length;
//        _ptr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(fixedMessage));
//    }

//    public void Write(ref RespWriter writer) => writer.WriteRaw(new ReadOnlySpan<byte>(_ptr, _length));
//}
//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//internal sealed class RawFixedMessageWriter : IRespWriter
//{
//    private readonly byte[] _prepared;
//    public static RawFixedMessageWriter? Create(string? command)
//        => string.IsNullOrWhiteSpace(command) ? null : new(command!);
//    private RawFixedMessageWriter(string command)
//    {
//        var buffer = RespWriter.Create(preambleReservation: 0);
//        buffer.WriteCommand(command, 0);
//        var leased = buffer.Detach();
//        _prepared = leased.GetBuffer().ToArray();
//        leased.Recycle();
//    }

//    public void Write(ref RespWriter writer) => writer.WriteRaw(_prepared);
//}

//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
//public interface IWhatever
//{
//    TResponse Execute<TResponse>(IRespWriter writer, IRespReader<TResponse> reader);
//    TResponse Execute<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TResponse> reader);
//    TResponse Execute<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TRequest, TResponse> reader);
//    void Execute<TRequest>(in TRequest request, IRespWriter<TRequest> writer, IRespReader reader);
//    void Execute(IRespWriter writer, IRespReader reader);

//    Task<TResponse> ExecuteAsync<TResponse>(IRespWriter writer, IRespReader<TResponse> reader);
//    Task<TResponse> ExecuteAsync<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TResponse> reader);
//    Task<TResponse> ExecuteAsync<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TRequest, TResponse> reader);
//    Task ExecuteAsync<TRequest>(in TRequest request, IRespWriter<TRequest> writer, IRespReader reader);
//    Task ExecuteAsync(IRespWriter writer, IRespReader reader);
//}
//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
//public static class WhateverExtensions
//{
//    public static TResponse Execute<TRequest, TResponse>(this IWhatever whatever, in TRequest request, IRespProcessor<TRequest, TResponse> processor)
//        => whatever.Execute<TRequest, TResponse>(request, processor, processor);
//    public static Task<TResponse> ExecuteAsync<TRequest, TResponse>(this IWhatever whatever, in TRequest request, IRespProcessor<TRequest, TResponse> processor)
//        => whatever.ExecuteAsync<TRequest, TResponse>(request, processor, processor);
//}

//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//internal class Whatever : IWhatever
//{
//    private static RequestBuffer WriteToLease(IRespWriter writer)
//    {
//        var target = RespWriter.Create();
//        writer.Write(ref target);
//        var buffer = target.Detach();
//        buffer.DebugValidateCommand();
//        return buffer;
//    }
//    private static RequestBuffer WriteToLease<TRequest>(IRespWriter<TRequest> writer, in TRequest request)
//    {
//        var target = RespWriter.Create();
//        writer.Write(in request, ref target);
//        var buffer = target.Detach();
//        buffer.DebugValidateCommand();
//        return buffer;
//    }

//    TResponse IWhatever.Execute<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TResponse> reader)
//    {
//        var payload = WriteToLease(writer, in request);
//        var msg = MessageSyncPlaceholder<TResponse>.Create(payload, reader);
//        lock (msg.SyncLock)
//        {
//            Enqueue(msg);
//            Monitor.Wait(msg.SyncLock);
//            return msg.WaitLocked();
//        }
//    }

//    private void Enqueue(IMessage msg) => throw new NotImplementedException();

//    TResponse IWhatever.Execute<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TRequest, TResponse> reader)
//    {
//        var payload = WriteToLease(writer, request);
//        var msg = MessageSyncPlaceholder<TResponse>.Create(payload, request, reader);
//        lock (msg.SyncLock)
//        {
//            Enqueue(msg);
//            return msg.WaitLocked();
//        }
//    }

//    void IWhatever.Execute<TRequest>(in TRequest request, IRespWriter<TRequest> writer, IRespReader reader)
//    {
//        var payload = WriteToLease(writer, request);
//        var msg = MessageSyncPlaceholder.Create(payload, reader);
//        lock (msg.SyncLock)
//        {
//            Enqueue(msg);
//            msg.WaitLocked();
//        }
//    }
//    void IWhatever.Execute(IRespWriter writer, IRespReader reader)
//    {
//        var payload = WriteToLease(writer);
//        var msg = MessageSyncPlaceholder.Create(payload, reader);
//        lock (msg.SyncLock)
//        {
//            Enqueue(msg);
//            msg.WaitLocked();
//        }
//    }

//    Task<TResponse> IWhatever.ExecuteAsync<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TResponse> reader)
//    {
//        var payload = WriteToLease(writer, in request);
//        var msg = MessageAsyncPlaceholder<TResponse>.Create(payload, reader);
//        Enqueue(msg);
//        return msg.Task;
//    }
//    Task<TResponse> IWhatever.ExecuteAsync<TRequest, TResponse>(in TRequest request, IRespWriter<TRequest> writer, IRespReader<TRequest, TResponse> reader)
//    {
//        var payload = WriteToLease(writer, in request);
//        var msg = MessageAsyncPlaceholder<TResponse>.Create(payload, request, reader);
//        Enqueue(msg);
//        return msg.Task;
//    }

//    TResponse IWhatever.Execute<TResponse>(IRespWriter writer, IRespReader<TResponse> reader)
//    {
//        var payload = WriteToLease(writer);
//        var msg = MessageSyncPlaceholder<TResponse>.Create(payload, reader);
//        lock (msg.SyncLock)
//        {
//            Enqueue(msg);
//            return msg.WaitLocked();
//        }
//    }
//    Task<TResponse> IWhatever.ExecuteAsync<TResponse>(IRespWriter writer, IRespReader<TResponse> reader)
//    {
//        var payload = WriteToLease(writer);
//        var msg = MessageAsyncPlaceholder<TResponse>.Create(payload, reader);
//        Enqueue(msg);
//        return msg.Task;
//    }

//    Task IWhatever.ExecuteAsync<TRequest>(in TRequest request, IRespWriter<TRequest> writer, IRespReader reader)
//    {
//        var payload = WriteToLease(writer, request);
//        var msg = MessageAsyncPlaceholder.Create(payload, reader);
//        Enqueue(msg);
//        return msg.Task;
//    }
//    Task IWhatever.ExecuteAsync(IRespWriter writer, IRespReader reader)
//    {
//        var payload = WriteToLease(writer);
//        var msg = MessageAsyncPlaceholder.Create(payload, reader);
//        Enqueue(msg);
//        return msg.Task;
//    }
//}

//internal interface IMessage // nongeneric API for the message queue
//{
//    bool TrySetException(Exception fault);
//    bool TrySetCanceled(CancellationToken token);
//    void Recycle();
//    void TrySetResult(in ReadOnlySequence<byte> result);
//}

//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
//internal abstract class MessageSyncPlaceholder<TResponse> : IMessage
//{
//    public virtual void Recycle() => _payload.Recycle();
//    private readonly RequestBuffer _payload;
//    protected MessageSyncPlaceholder(in RequestBuffer payload)
//        => _payload = payload;

//    public void TrySetResult(in ReadOnlySequence<byte> payload)
//    {
//        try
//        {
//            var reader = new RespReader(in payload);
//            if (!reader.ReadNext()) RespReader.ThrowEOF();
//            if (reader.IsError)
//            {
//                TrySetException(reader.ReadError());
//            }
//            else
//            {
//                TrySetResult(Parse(ref reader));
//            }
//            Debug.Assert(!reader.ReadNext(), "not fully consumed");
//        }
//        catch(Exception ex)
//        {
//            TrySetException(ex);
//        }
//    }
//    protected abstract TResponse Parse(ref RespReader reader);

//    public bool TrySetException(Exception fault)
//    {
//        lock (SyncLock)
//        {
//            if (_fault is not null) return false;
//            _fault = fault;
//            Monitor.PulseAll(SyncLock);
//            return true;
//        }
//    }
//    public bool TrySetResult(TResponse result)
//    {
//        lock (SyncLock)
//        {
//            if (_fault is not null) return false;
//            _result = result;
//            Monitor.PulseAll(SyncLock);
//            return true;
//        }
//    }

//    public static MessageSyncPlaceholder<TResponse> Create(in RequestBuffer payload, IRespReader<TResponse> reader) => new StatelessMessage(payload, reader);
//    public static MessageSyncPlaceholder<TResponse> Create<TRequest>(in RequestBuffer payload, in TRequest request, IRespReader<TRequest, TResponse> reader)
//        => new StatefulMessage<TRequest>(payload, request, reader);
//    public object SyncLock => this;
//    private Exception? _fault;
//    private TResponse? _result;
//    public TResponse WaitLocked()
//    {
//        Monitor.Wait(SyncLock);
//        if (_fault is not null) throw _fault;
//        return _result!;
//    }

//    bool IMessage.TrySetCanceled(CancellationToken token) => TrySetException(new OperationCanceledException(token));

//    private sealed class StatelessMessage : MessageSyncPlaceholder<TResponse>
//    {
//        private readonly IRespReader<TResponse> _reader;
//        protected override TResponse Parse(ref RespReader reader) => _reader.Read(ref reader);
//        public StatelessMessage(in RequestBuffer payload, IRespReader<TResponse> reader) : base(payload)
//        {
//            _reader = reader;
//        }
//    }
//    private sealed class StatefulMessage<TRequest> : MessageSyncPlaceholder<TResponse>
//    {
//        private readonly TRequest _request;
//        private readonly IRespReader<TRequest, TResponse> _reader;
//        protected override TResponse Parse(ref RespReader reader) => _reader.Read(in _request, ref reader);

//        public StatefulMessage(in RequestBuffer payload, in TRequest request, IRespReader<TRequest, TResponse> reader) : base(payload)
//        {
//            _request = request;
//            _reader = reader;
//        }
//        public override void Recycle()
//        {
//            base.Recycle();
//            Unsafe.AsRef(in _request) = default!;
//        }
//    }
//}

//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//internal sealed class MessageSyncPlaceholder : IMessage
//{
//    public void Recycle() => _payload.Recycle();
//    private readonly RequestBuffer _payload;
//    private readonly IRespReader _reader;
//    private MessageSyncPlaceholder(in RequestBuffer payload, IRespReader reader)
//    {
//        _payload = payload;
//        _reader = reader;
//    }
//    public void TrySetResult(in ReadOnlySequence<byte> payload)
//    {
//        try
//        {
//            var reader = new RespReader(in payload);
//            if (!reader.ReadNext()) RespReader.ThrowEOF();
//            if (reader.IsError)
//            {
//                TrySetException(reader.ReadError());
//            }
//            else
//            {
//                _reader.Read(ref reader);
//                TrySetResult();
//            }
//            Debug.Assert(!reader.ReadNext(), "not fully consumed");
//        }
//        catch (Exception ex)
//        {
//            TrySetException(ex);
//        }
//    }

//    public bool TrySetException(Exception fault)
//    {
//        lock (SyncLock)
//        {
//            if (_fault is not null) return false;
//            _fault = fault;
//            Monitor.PulseAll(SyncLock);
//            return true;
//        }
//    }
//    public bool TrySetResult()
//    {
//        lock (SyncLock)
//        {
//            if (_fault is not null) return false;
//            Monitor.PulseAll(SyncLock);
//            return true;
//        }
//    }

//    public static MessageSyncPlaceholder Create(in RequestBuffer payload, IRespReader reader) => new(payload, reader);
//    public object SyncLock => this;
//    private Exception? _fault;
//    public void WaitLocked()
//    {
//        Monitor.Wait(SyncLock);
//        if (_fault is not null) throw _fault;
//    }

//    bool IMessage.TrySetCanceled(CancellationToken token) => TrySetException(new OperationCanceledException(token));
//}

//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//internal abstract class MessageAsyncPlaceholder<TResponse> : TaskCompletionSource<TResponse>, IMessage
//{
//    public virtual void Recycle() => _payload.Recycle();
//    private readonly RequestBuffer _payload;
//    protected MessageAsyncPlaceholder(in RequestBuffer payload) : base(TaskCreationOptions.RunContinuationsAsynchronously)
//        => _payload = payload;

//    public static MessageAsyncPlaceholder<TResponse> Create(in RequestBuffer payload, IRespReader<TResponse> reader) => new StatelessMessage(payload, reader);
//    public static MessageAsyncPlaceholder<TResponse> Create<TRequest>(in RequestBuffer payload, in TRequest request, IRespReader<TRequest, TResponse> reader)
//        => new StatefulMessage<TRequest>(payload, request, reader);
//    public void TrySetResult(in ReadOnlySequence<byte> payload)
//    {
//        try
//        {
//            var reader = new RespReader(in payload);
//            if (!reader.ReadNext()) RespReader.ThrowEOF();
//            if (reader.IsError)
//            {
//                TrySetException(reader.ReadError());
//            }
//            else
//            {
//                TrySetResult(Parse(ref reader));
//            }
//            Debug.Assert(!reader.ReadNext(), "not fully consumed");
//        }
//        catch (Exception ex)
//        {
//            TrySetException(ex);
//        }
//    }
//    protected abstract TResponse Parse(ref RespReader reader);
    
//    private sealed class StatelessMessage : MessageAsyncPlaceholder<TResponse>
//    {
//        private readonly IRespReader<TResponse> _reader;
//        protected override TResponse Parse(ref RespReader reader) => _reader.Read(ref reader);
//        public StatelessMessage(in RequestBuffer payload, IRespReader<TResponse> reader) : base(payload)
//        {
//            _reader = reader;
//        }
//    }
//    private sealed class StatefulMessage<TRequest> : MessageAsyncPlaceholder<TResponse>
//    {
//        private readonly TRequest _request;
//        private readonly IRespReader<TRequest, TResponse> _reader;
//        protected override TResponse Parse(ref RespReader reader) => _reader.Read(in _request, ref reader);
//        public StatefulMessage(in RequestBuffer payload, in TRequest request, IRespReader<TRequest, TResponse> reader) : base(payload)
//        {
//            _request = request;
//            _reader = reader;
//        }

//        public override void Recycle()
//        {
//            base.Recycle();
//            Unsafe.AsRef(in _request) = default!;
//        }
//    }
//}

//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//internal sealed class MessageAsyncPlaceholder :
//#if NET5_0_OR_GREATER
//    TaskCompletionSource, IMessage
//#else
//    TaskCompletionSource<bool>, IMessage
//#endif
//{
//    public void Recycle() => _payload.Recycle();
//    private readonly RequestBuffer _payload;
//    private readonly IRespReader _reader;
//    internal MessageAsyncPlaceholder(in RequestBuffer payload, IRespReader reader) : base(TaskCreationOptions.RunContinuationsAsynchronously)
//    {
//        _payload = payload;
//        _reader = reader;
//    }

//    public static MessageAsyncPlaceholder Create(in RequestBuffer payload, IRespReader reader) => new(payload, reader);
//    public void TrySetResult(in ReadOnlySequence<byte> payload)
//    {
//        try
//        {
//            var reader = new RespReader(in payload);
//            if (!reader.ReadNext()) RespReader.ThrowEOF();
//            if (reader.IsError)
//            {
//                TrySetException(reader.ReadError());
//            }
//            else
//            {
//                _reader.Read(ref reader);
//#if NET5_0_OR_GREATER
//                TrySetResult();
//#else
//                TrySetResult(true);
//#endif
//            }
//            Debug.Assert(!reader.ReadNext(), "not fully consumed");
//        }
//        catch (Exception ex)
//        {
//            TrySetException(ex);
//        }
//    }
//}

//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
//public interface IRespReader<TRequest, TResponse> // base-class for reusable parsers that need input state
//{
//    TResponse Read(in TRequest request, ref RespReader reader);
//}
//[Experimental(RespRequest.ExperimentalDiagnosticID)]
//[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental")]
//public interface IRespProcessor<TRequest, TResponse>
//    : IRespWriter<TRequest>, IRespReader<TRequest, TResponse>
//{
//}


////[Experimental(RespRequest.ExperimentalDiagnosticID)]
////public abstract class RespProcessor<T>
////{
////    public abstract T Parse(in RespChunk value);
////}





////[Experimental(RespRequest.ExperimentalDiagnosticID)]
////[SuppressMessage("Usage", "CA2231:Overload operator equals on overriding value type Equals", Justification = "API not necessary here")]


