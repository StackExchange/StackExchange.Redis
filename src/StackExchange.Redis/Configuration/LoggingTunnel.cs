using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Buffers;
using RESPite.Messages;
using static StackExchange.Redis.PhysicalConnection;

namespace StackExchange.Redis.Configuration;

/// <summary>
/// Captures redis traffic; intended for debug use.
/// </summary>
[Obsolete("This API is experimental, has security and performance implications, and may change without notice", false)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental API")]
public abstract class LoggingTunnel : Tunnel
{
    private readonly ConfigurationOptions _options;
    private readonly bool _ssl;
    private readonly Tunnel? _tail;

    internal sealed class StreamRespReader(Stream source, bool isInbound) : IDisposable
    {
        private CycleBuffer _readBuffer = CycleBuffer.Create();
        private RespScanState _state;
        private bool _reading, _disposed; // we need to track the state of the reader to avoid releasing the buffer while it's in use

        internal bool TryTakeOne(out ContextualRedisResult result, bool withData = true)
        {
            var fullBuffer = _readBuffer.GetAllCommitted();
            var newData = fullBuffer.Slice(_state.TotalBytes);
            var status = RespFrameScanner.Default.TryRead(ref _state, newData);
            switch (status)
            {
                case OperationStatus.Done:
                    var frame = fullBuffer.Slice(0, _state.TotalBytes);
                    var reader = new RespReader(frame);
                    reader.MovePastBof();
                    bool isOutOfBand = reader.Prefix is RespPrefix.Push
                                       || (isInbound && reader.IsAggregate &&
                                           !IsArrayOutOfBand(in reader));

                    RedisResult? parsed;
                    if (withData)
                    {
                        if (!RedisResult.TryCreate(null, ref reader, out parsed))
                        {
                            ThrowInvalidReadStatus(OperationStatus.InvalidData);
                        }
                    }
                    else
                    {
                        parsed = null;
                    }
                    result = new(parsed, isOutOfBand);
                    return true;
                case OperationStatus.NeedMoreData:
                    result = default;
                    return false;
                default:
                    ThrowInvalidReadStatus(status);
                    goto case OperationStatus.NeedMoreData; // never reached
            }
        }

        private static bool IsArrayOutOfBand(in RespReader source)
        {
            var reader = source;
            int len;
            if (!reader.IsStreaming
                && (len = reader.AggregateLength()) >= 2
                && (reader.SafeTryMoveNext() & reader.IsInlineScalar & !reader.IsError))
            {
                const int MAX_TYPE_LEN = 16;
                var span = reader.TryGetSpan(out var tmp)
                    ? tmp
                    : StackCopyLengthChecked(in reader, stackalloc byte[MAX_TYPE_LEN]);

                var hash = FastHash.HashCS(span);
                switch (hash)
                {
                    case PushMessage.HashCS when PushMessage.IsCS(hash, span) & len >= 3:
                    case PushPMessage.HashCS when PushPMessage.IsCS(hash, span) & len >= 4:
                    case PushSMessage.HashCS when PushSMessage.IsCS(hash, span) & len >= 3:
                        return true;
                }
            }

            return false;
        }

        public ValueTask<ContextualRedisResult> ReadOneAsync(CancellationToken cancellationToken = default)
            => TryTakeOne(out var result) ? new(result) : ReadMoreAsync(cancellationToken);

        [DoesNotReturn]
        private static void ThrowInvalidReadStatus(OperationStatus status)
            => throw new InvalidOperationException($"Unexpected read status: {status}");

        private async ValueTask<ContextualRedisResult> ReadMoreAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var buffer = _readBuffer.GetUncommittedMemory();
                Debug.Assert(!buffer.IsEmpty, "rule out zero-length reads");
                _reading = true;
                var read = await source.ReadAsync(buffer, cancellationToken).ForAwait();
                _reading = false;
                if (read <= 0)
                {
                    // EOF
                    return default;
                }
                _readBuffer.Commit(read);

                if (TryTakeOne(out var result)) return result;
            }
        }

        public void Dispose()
        {
            bool disposed = _disposed;
            _disposed = true;
            _state = default;

            if (!(_reading | disposed)) _readBuffer.Release();
            _readBuffer = default;
            if (!disposed) source.Dispose();
        }

        public async ValueTask<long> ValidateAsync(CancellationToken cancellationToken = default)
        {
            long count = 0;
            while (true)
            {
                var buffer = _readBuffer.GetUncommittedMemory();
                Debug.Assert(!buffer.IsEmpty, "rule out zero-length reads");
                _reading = true;
                var read = await source.ReadAsync(buffer, cancellationToken).ForAwait();
                _reading = false;
                if (read <= 0)
                {
                    // EOF
                    return count;
                }
                _readBuffer.Commit(read);
                while (TryTakeOne(out _, withData: false)) count++;
            }
        }
    }

    /// <summary>
    /// Replay the RESP messages for a pair of streams, invoking a callback per operation.
    /// </summary>
    public static async Task<long> ReplayAsync(Stream @out, Stream @in, Action<RedisResult, RedisResult> pair)
    {
        long count = 0;
        using var outReader = new StreamRespReader(@out, isInbound: false);
        using var inReader = new StreamRespReader(@in, isInbound: true);
        while (true)
        {
            if (!outReader.TryTakeOne(out var sent)) sent = await outReader.ReadOneAsync().ForAwait();
            ContextualRedisResult received;
            try
            {
                do
                {
                    if (!inReader.TryTakeOne(out received)) received = await inReader.ReadOneAsync().ForAwait();
                    if (received.IsOutOfBand && received.Result is not null)
                    {
                        // spoof an empty request for OOB messages
                        pair(RedisResult.NullSingle, received.Result);
                    }
                }
                while (received.IsOutOfBand);
            }
            catch (Exception ex)
            {
                // if we got an exception following a command, spoof that as a pair,
                // so we see the message that had a corrupted reply
                if (sent.Result is not null)
                {
                    pair(sent.Result, RedisResult.Create(ex.Message, ResultType.Error));
                }
                throw; // still surface the original exception
            }

            if (sent.Result is null || received.Result is null) break; // no more paired messages

            pair(sent.Result, received.Result);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Replay the RESP messages all the streams in a folder, invoking a callback per operation.
    /// </summary>
    /// <param name="path">The directory of captured files to replay.</param>
    /// <param name="pair">Operation to perform per replayed message pair.</param>
    public static async Task<long> ReplayAsync(string path, Action<RedisResult, RedisResult> pair)
    {
        long total = 0;
        foreach (var outPath in Directory.EnumerateFiles(path, "*.out"))
        {
            var inPath = Path.ChangeExtension(outPath, "in");
            if (!File.Exists(outPath)) continue;

            using var outFile = File.OpenRead(outPath);
            using var inFile = File.OpenRead(inPath);
            total += await ReplayAsync(outFile, inFile, pair).ForAwait();
        }
        return total;
    }

    /// <summary>
    /// Validate a RESP stream and return the number of top-level RESP fragments.
    /// </summary>
    /// <param name="path">The path of a single file to validate, or a directory of captured files to validate.</param>
    public static async Task<long> ValidateAsync(string path)
    {
        if (File.Exists(path))
        {
            using var singleFile = File.OpenRead(path);
            return await ValidateAsync(singleFile).ForAwait();
        }
        else if (Directory.Exists(path))
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path))
            {
                try
                {
                    using var folderFile = File.OpenRead(file);
                    total += await ValidateAsync(folderFile).ForAwait();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(ex.Message + " in " + file, ex);
                }
            }
            return total;
        }
        else
        {
            throw new FileNotFoundException(path);
        }
    }

    /// <summary>
    /// Validate a RESP stream and return the number of top-level RESP fragments.
    /// </summary>
    public static async Task<long> ValidateAsync(Stream stream)
    {
        using var reader = new StreamRespReader(stream, isInbound: false);
        return await reader.ValidateAsync();
    }

    internal readonly struct ContextualRedisResult
    {
        public readonly RedisResult? Result;
        public readonly bool IsOutOfBand;
        public ContextualRedisResult(RedisResult? result, bool isOutOfBand)
        {
            Result = result;
            IsOutOfBand = isOutOfBand;
        }
    }

    /// <summary>
    /// Create a new instance of a <see cref="LoggingTunnel"/>.
    /// </summary>
    protected LoggingTunnel(ConfigurationOptions? options = null, Tunnel? tail = null)
    {
        options ??= new();
        _options = options;
        _ssl = options.Ssl;
        _tail = tail;
        options.Ssl = false; // disable here, since we want to log *decrypted*
    }

    /// <summary>
    /// Configures the provided options to perform file-based logging to a directory;
    /// files will be sequential per stream starting from zero, and will blindly overwrite existing files.
    /// </summary>
    public static void LogToDirectory(ConfigurationOptions options, string path)
    {
        var tunnel = new DirectoryLoggingTunnel(path, options, options.Tunnel);
        options.Tunnel = tunnel;
    }

    private sealed class DirectoryLoggingTunnel : LoggingTunnel
    {
        private readonly string path;
        private int _nextIndex = -1;

        internal DirectoryLoggingTunnel(string path, ConfigurationOptions? options = null, Tunnel? tail = null)
            : base(options, tail)
        {
            this.path = path;
            if (!Directory.Exists(path)) throw new InvalidOperationException("Directory does not exist: " + path);
        }

        protected override Stream Log(Stream stream, EndPoint endpoint, ConnectionType connectionType)
        {
            int index = Interlocked.Increment(ref _nextIndex);
            var name = $"{Format.ToString(endpoint)} {connectionType} {index}.tmp";
            foreach (var c in InvalidChars)
            {
                name = name.Replace(c, ' ');
            }
            name = Path.Combine(path, name);
            var reads = File.Create(Path.ChangeExtension(name, ".in"));
            var writes = File.Create(Path.ChangeExtension(name, ".out"));
            return new LoggingDuplexStream(stream, reads, writes);
        }

        private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();
    }

    /// <inheritdoc/>
    public override async ValueTask<Stream?> BeforeAuthenticateAsync(EndPoint endpoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken)
    {
        Stream? stream = null;
        if (_tail is not null)
        {
            stream = await _tail.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken).ForAwait();
        }
        stream ??= new NetworkStream(socket ?? throw new InvalidOperationException("No stream or socket available"));
        if (_ssl)
        {
            stream = await TlsHandshakeAsync(stream, endpoint).ForAwait();
        }
        return Log(stream, endpoint, connectionType);
    }

    /// <summary>
    /// Perform logging on the provided stream.
    /// </summary>
    protected abstract Stream Log(Stream stream, EndPoint endpoint, ConnectionType connectionType);

    /// <inheritdoc/>
    public override ValueTask BeforeSocketConnectAsync(EndPoint endPoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken)
    {
        return _tail is null ? base.BeforeSocketConnectAsync(endPoint, connectionType, socket, cancellationToken)
            : _tail.BeforeSocketConnectAsync(endPoint, connectionType, socket, cancellationToken);
    }

    /// <inheritdoc/>
    public override ValueTask<EndPoint?> GetSocketConnectEndpointAsync(EndPoint endpoint, CancellationToken cancellationToken)
    {
        return _tail is null ? base.GetSocketConnectEndpointAsync(endpoint, cancellationToken)
            : _tail.GetSocketConnectEndpointAsync(endpoint, cancellationToken);
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously - netfx back-compat mode
    private async Task<Stream> TlsHandshakeAsync(Stream stream, EndPoint endpoint)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        // mirrors TLS handshake from PhysicalConnection, but wouldn't help to share code here
        var host = _options.SslHost;
        if (host.IsNullOrWhiteSpace())
        {
            host = Format.ToStringHostOnly(endpoint);
        }

        var ssl = new SslStream(
            innerStream: stream,
            leaveInnerStreamOpen: false,
            userCertificateValidationCallback: _options.CertificateValidationCallback ?? PhysicalConnection.GetAmbientIssuerCertificateCallback(),
            userCertificateSelectionCallback: _options.CertificateSelectionCallback ?? PhysicalConnection.GetAmbientClientCertificateCallback(),
            encryptionPolicy: EncryptionPolicy.RequireEncryption);

#if NETCOREAPP3_1_OR_GREATER
        var configOptions = _options.SslClientAuthenticationOptions?.Invoke(host);
        if (configOptions is not null)
        {
            await ssl.AuthenticateAsClientAsync(configOptions).ForAwait();
        }
        else
        {
            await ssl.AuthenticateAsClientAsync(host, _options.SslProtocols, _options.CheckCertificateRevocation).ForAwait();
        }
#else
        await ssl.AuthenticateAsClientAsync(host, _options.SslProtocols, _options.CheckCertificateRevocation).ForAwait();
#endif
        return ssl;
    }

    /// <summary>
    /// Get a typical text representation of a redis command.
    /// </summary>
    public static string DefaultFormatCommand(RedisResult value)
    {
        try
        {
            if (value.IsNull) return "(null)";
            if (value.Type == ResultType.Array)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < value.Length; i++)
                {
                    var item = value[i];
                    if (i != 0) sb.Append(' ');
                    if (IsSimple(item))
                    {
                        sb.Append(item.AsString());
                    }
                    else
                    {
                        sb.Append("...");
                        break;
                    }
                }
                return sb.ToString();
            }
        }
        catch { }
        return value.Type.ToString();

        static bool IsSimple(RedisResult value)
        {
            try
            {
                switch (value.Resp2Type)
                {
                    case ResultType.Array: return false;
                    case ResultType.Error: return true;
                    default:
                        var blob = value.AsByteArray(); // note non-alloc in the remaining cases
                        if (blob is null) return true;
                        if (blob.Length >= 50) return false;
                        for (int i = 0; i < blob.Length; i++)
                        {
                            char c = (char)blob[i];
                            if (c < ' ' || c > '~') return false;
                        }
                        return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Get a typical text representation of a redis response.
    /// </summary>
    public static string DefaultFormatResponse(RedisResult value)
    {
        try
        {
            if (value.IsNull) return "(null)";
            switch (value.Type.ToResp2())
            {
                case ResultType.Integer:
                case ResultType.BulkString:
                case ResultType.SimpleString:
                    return value.AsString()!;
                case ResultType.Error:
                    return "-" + value.ToString();
                case ResultType.Array:
                    return $"[{value.Length}]";
            }
        }
        catch (Exception ex)
        {
            Debug.Write(ex.Message);
        }
        return value.Type.ToString();
    }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    protected sealed class LoggingDuplexStream : Stream
    {
        private readonly Stream _inner, _reads, _writes;

        internal LoggingDuplexStream(Stream inner, Stream reads, Stream writes)
        {
            _inner = inner;
            _reads = reads;
            _writes = writes;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanWrite => _inner.CanWrite;

        public override bool CanSeek => false; // duplex
        public override bool CanTimeout => _inner.CanTimeout;
        public override int ReadTimeout { get => _inner.ReadTimeout; set => _inner.ReadTimeout = value; }
        public override int WriteTimeout { get => _inner.WriteTimeout; set => _inner.WriteTimeout = value; }
        public override long Length => throw new NotSupportedException(); // duplex
        public override long Position
        {
            get => throw new NotSupportedException(); // duplex
            set => throw new NotSupportedException(); // duplex
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); // duplex
        public override void SetLength(long value) => throw new NotSupportedException(); // duplex

        // we don't use these APIs
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) => throw new NotSupportedException();
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) => throw new NotSupportedException();
        public override int EndRead(IAsyncResult asyncResult) => throw new NotSupportedException();
        public override void EndWrite(IAsyncResult asyncResult) => throw new NotSupportedException();

        public override void Flush()
        {
            // note we don't flush _reads, as that could be cross-threaded
            // (flush is a write operation, not a read one)
            _writes.Flush();
            _inner.Flush();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            var writesTask = _writes.FlushAsync().ForAwait();
            await _inner.FlushAsync().ForAwait();
            await writesTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                try { _reads.Flush(); } catch { }
                _reads.Dispose();
                try { _writes.Flush(); } catch { }
                _writes.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Close()
        {
            _inner.Close();
            try { _reads.Flush(); } catch { }
            _reads.Close();
            try { _writes.Flush(); } catch { }
            _writes.Close();
            base.Close();
        }

#if NETCOREAPP3_0_OR_GREATER
        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ForAwait();
            try { await _reads.FlushAsync().ForAwait(); } catch { }
            await _reads.DisposeAsync().ForAwait();
            try { await _writes.FlushAsync().ForAwait(); } catch { }
            await _writes.DisposeAsync().ForAwait();
            await base.DisposeAsync().ForAwait();
        }
#endif

        public override int ReadByte()
        {
            var val = _inner.ReadByte();
            if (val >= 0)
            {
                _reads.WriteByte((byte)val);
                _reads.Flush();
            }
            return val;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var len = _inner.Read(buffer, offset, count);
            if (len > 0)
            {
                _reads.Write(buffer, offset, len);
                _reads.Flush();
            }
            return len;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var len = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ForAwait();
            if (len > 0)
            {
                await _reads.WriteAsync(buffer, offset, len, cancellationToken).ForAwait();
                await _reads.FlushAsync(cancellationToken).ForAwait();
            }
            return len;
        }
#if NETCOREAPP3_0_OR_GREATER
        public override int Read(Span<byte> buffer)
        {
            var len = _inner.Read(buffer);
            if (len > 0)
            {
                _reads.Write(buffer.Slice(0, len));
                _reads.Flush();
            }
            return len;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var len = await _inner.ReadAsync(buffer, cancellationToken).ForAwait();
            if (len > 0)
            {
                await _reads.WriteAsync(buffer.Slice(0, len), cancellationToken).ForAwait();
                await _reads.FlushAsync(cancellationToken).ForAwait();
            }
            return len;
        }
#endif

        public override void WriteByte(byte value)
        {
            _writes.WriteByte(value);
            _inner.WriteByte(value);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            _writes.Write(buffer, offset, count);
            _inner.Write(buffer, offset, count);
        }
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var writesTask = _writes.WriteAsync(buffer, offset, count, cancellationToken).ForAwait();
            await _inner.WriteAsync(buffer, offset, count, cancellationToken).ForAwait();
            await writesTask;
        }
#if NETCOREAPP3_0_OR_GREATER
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _writes.Write(buffer);
            _inner.Write(buffer);
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            var writesTask = _writes.WriteAsync(buffer, cancellationToken).ForAwait();
            await _inner.WriteAsync(buffer, cancellationToken).ForAwait();
            await writesTask;
        }
#endif
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
