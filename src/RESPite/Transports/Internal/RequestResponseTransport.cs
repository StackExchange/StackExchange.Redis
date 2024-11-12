using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite.Buffers;
using RESPite.Internal;
using RESPite.Messages;

namespace RESPite.Transports.Internal;

internal sealed class RequestResponseTransport<TState>(IByteTransport transport, IFrameScanner<TState> scanner, bool validateOutbound)
        : RequestResponseBase<TState>(transport, scanner, validateOutbound), IRequestResponseTransport
{ }

internal sealed class SyncRequestResponseTransport<TState>(ISyncByteTransport transport, IFrameScanner<TState> scanner, bool validateOutbound)
    : RequestResponseBase<TState>(transport, scanner, validateOutbound), ISyncRequestResponseTransport
{ }

internal sealed class AsyncRequestResponseTransport<TState>(IAsyncByteTransport transport, IFrameScanner<TState> scanner, bool validateOutbound)
    : RequestResponseBase<TState>(transport, scanner, validateOutbound), IAsyncRequestResponseTransport
{ }

internal abstract class RequestResponseBase<TState> : IRequestResponseBase
{
    private readonly IByteTransportBase _transport;
    private readonly IFrameScanner<TState> _scanner;
    private readonly int _flags;
    private const int SUPPORT_SYNC = 1 << 0, SUPPORT_ASYNC = 1 << 1, SUPPORT_LIFETIME = 1 << 2, SCAN_OUTBOUND = 1 << 3;

    public void Dispose() => (_transport as IDisposable)?.Dispose();

    public ValueTask DisposeAsync() => _transport is IAsyncDisposable d ? d.DisposeAsync() : default;

    public RequestResponseBase(IByteTransportBase transport, IFrameScanner<TState> scanner, bool validateOutbound)
    {
        _transport = transport;
        _scanner = scanner;
        if (transport is ISyncByteTransport) _flags |= SUPPORT_SYNC;
        if (transport is IAsyncByteTransport) _flags |= SUPPORT_ASYNC;
        if (scanner is IFrameScannerLifetime<TState>) _flags |= SUPPORT_LIFETIME;
#if DEBUG
        validateOutbound = true; // always pay the extra in debug
#endif
        if (validateOutbound && scanner is IFrameValidator) _flags |= SCAN_OUTBOUND;
    }

    [DoesNotReturn]
    private void ThrowNotSupported([CallerMemberName] string caller = "") => throw new NotSupportedException(caller);
    private IAsyncByteTransport AsAsync([CallerMemberName] string caller = "")
    {
        if ((_flags & SUPPORT_ASYNC) == 0) ThrowNotSupported(caller);
        return Unsafe.As<IAsyncByteTransport>(_transport); // type-tested in .ctor
    }

    private ISyncByteTransport AsSync([CallerMemberName] string caller = "")
    {
        if ((_flags & SUPPORT_SYNC) == 0) ThrowNotSupported(caller);
        return Unsafe.As<ISyncByteTransport>(_transport); // type-tested in .ctor
    }

    private IFrameValidator AsValidator([CallerMemberName] string caller = "")
    {
        if ((_flags & SCAN_OUTBOUND) == 0) ThrowNotSupported(caller);
        return Unsafe.As<IFrameValidator>(_scanner); // type-tested in .ctor
    }

    private IAsyncByteTransport AsAsyncPrechecked() => Unsafe.As<IAsyncByteTransport>(_transport);
    private bool WithLifetime => (_flags & SUPPORT_LIFETIME) != 0;

    private bool ScanOutbound => (_flags & SCAN_OUTBOUND) != 0;

    public ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader, CancellationToken token = default)
    {
        var transport = AsAsync();
        var leased = writer.Serialize(in request);
        var content = leased.Content;

        if (ScanOutbound) Validate(in content);

        var pendingWrite = transport.WriteAsync(in content, token);
        if (!pendingWrite.IsCompletedSuccessfully) return AwaitedWrite(this, pendingWrite, leased, request, reader, token);

        pendingWrite.GetAwaiter().GetResult(); // ensure observed
        leased.Release();

        var pendingRead = transport.ReadOneAsync(_scanner, OutOfBandData, token);
        if (!pendingWrite.IsCompletedSuccessfully) return AwaitedRead(pendingRead, request, reader);

        leased = pendingRead.GetAwaiter().GetResult();
        content = leased.Content;
        var response = reader.Read(in request, in content);
        leased.Release();
        return new(response);

        static async ValueTask<TResponse> AwaitedWrite(RequestResponseBase<TState> @this, ValueTask pendingWrite, RefCountedBuffer<byte> leased, TRequest request, IReader<TRequest, TResponse> reader, CancellationToken token)
        {
            await pendingWrite.ConfigureAwait(false);
            leased.Release();

            leased = await @this.AsAsyncPrechecked().ReadOneAsync(@this._scanner, @this.OutOfBandData, token).ConfigureAwait(false);
            var response = reader.Read(in request, leased.Content);
            leased.Release();
            return response;
        }

        static async ValueTask<TResponse> AwaitedRead(ValueTask<RefCountedBuffer<byte>> pendingRead, TRequest request, IReader<TRequest, TResponse> reader)
        {
            var leased = await pendingRead.ConfigureAwait(false);
            var response = reader.Read(in request, leased.Content);
            leased.Release();
            return response;
        }
    }

    private void Validate(in ReadOnlySequence<byte> content)
    {
        try
        {
            if (content.IsEmpty)
            {
                Debug.WriteLine($"{GetType().Name} sending empty frame to transport");
            }
            else
            {
                Debug.WriteLine($"{GetType().Name} sending {content.Length} bytes to transport: {Constants.UTF8.GetString(content)}");
            }
            AsValidator().Validate(in content);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid outbound frame (${content.Length} bytes): {ex.Message}", ex);
        }
    }

    public ValueTask<TResponse> SendAsync<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader, CancellationToken token = default)
    {
        var transport = AsAsync();
        var leased = writer.Serialize(in request);
        var content = leased.Content;

        if (ScanOutbound) Validate(in content);

        var pendingWrite = transport.WriteAsync(in content, token);
        if (!pendingWrite.IsCompletedSuccessfully) return AwaitedWrite(this, pendingWrite, leased, reader, token);

        pendingWrite.GetAwaiter().GetResult(); // ensure observed
        leased.Release();

        var pendingRead = transport.ReadOneAsync(_scanner, OutOfBandData, token);
        if (!pendingRead.IsCompletedSuccessfully) return AwaitedRead(pendingRead, reader);

        leased = pendingRead.GetAwaiter().GetResult();
        content = leased.Content;
        var response = reader.Read(in Empty.Value, in content);
        leased.Release();
        return new(response);

        static async ValueTask<TResponse> AwaitedWrite(RequestResponseBase<TState> @this, ValueTask pendingWrite, RefCountedBuffer<byte> leased, IReader<Empty, TResponse> reader, CancellationToken token)
        {
            await pendingWrite.ConfigureAwait(false);
            leased.Release();

            leased = await @this.AsAsyncPrechecked().ReadOneAsync(@this._scanner, @this.OutOfBandData, token).ConfigureAwait(false);
            var response = reader.Read(in Empty.Value, leased.Content);
            leased.Release();
            return response;
        }

        static async ValueTask<TResponse> AwaitedRead(ValueTask<RefCountedBuffer<byte>> pendingRead, IReader<Empty, TResponse> reader)
        {
            var leased = await pendingRead.ConfigureAwait(false);
            var response = reader.Read(in Empty.Value, leased.Content);
            leased.Release();
            return response;
        }
    }
    public TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<TRequest, TResponse> reader)
    {
        var transport = AsSync();
        var leased = writer.Serialize(in request);
        var content = leased.Content;

        if (ScanOutbound) Validate(in content);

        transport.Write(in content);
        leased.Release();

        leased = transport.ReadOne(_scanner, OutOfBandData, WithLifetime);
        content = leased.Content;
        var response = reader.Read(in request, in content);
        leased.Release();
        return response;
    }
    public TResponse Send<TRequest, TResponse>(in TRequest request, IWriter<TRequest> writer, IReader<Empty, TResponse> reader)
    {
        var transport = AsSync();
        var leased = writer.Serialize(in request);
        var content = leased.Content;

        if (ScanOutbound) Validate(in content);

        transport.Write(in content);
        leased.Release();

        leased = transport.ReadOne(_scanner, OutOfBandData, WithLifetime);
        content = leased.Content;
        var response = reader.Read(in Empty.Value, in content);
        leased.Release();
        return response;
    }

    public event MessageCallback? OutOfBandData;
}
