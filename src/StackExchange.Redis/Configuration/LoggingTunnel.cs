using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Arenas;
using StackExchange.Redis.Protocol;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static StackExchange.Redis.PhysicalConnection;

namespace StackExchange.Redis.Configuration;

/// <summary>
/// Captures redis traffic; intended for debug use 
/// </summary>
[Experimental(ExperimentalDiagnosticID)]
[SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "Experimental API")]
public abstract class LoggingTunnel : Tunnel
{
    internal const string ExperimentalDiagnosticID = "SERED001";
    private readonly ConfigurationOptions _options;
    private readonly bool _ssl;
    private readonly Tunnel? _tail;

    /// <summary>
    /// Replay the RESP messages for a pair of streams, invoking a callback per operation
    /// </summary>
    public static async Task<long> ReplayAsync(Stream @out, Stream @in, Action<RedisResult, RedisResult> pair)
    {
        using Arena<RawResult> arena = new();
        var outPipe = StreamConnection.GetReader(@out);
        var inPipe = StreamConnection.GetReader(@in);

        long count = 0;
        while (true)
        {
            var sent = await ReadOneAsync(outPipe, arena, isInbound: false).ForAwait();
            ContextualRedisResult received;
            try
            {
                do
                {
                    received = await ReadOneAsync(inPipe, arena, isInbound: true).ForAwait();
                    if (received.IsOutOfBand && received.Result is not null)
                    {
                        // spoof an empty request for OOB messages
                        pair(RedisResult.NullSingle, received.Result);
                        count++;
                    }
                } while (received.IsOutOfBand);
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
    /// Replay the RESP messages all the streams in a folder, invoking a callback per operation
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
    /// Callback with a request/response RESP pair
    /// </summary>
    [Experimental(RespRequest.ExperimentalDiagnosticID)]
    public delegate void MessagePair(RespReader request, RespReader response);

    /// <summary>
    /// Callback with a RESP message
    /// </summary>
    [Experimental(RespRequest.ExperimentalDiagnosticID)]
    public delegate void Message(RespReader message);

    /// <summary>
    /// Iterate over a RESP stream invoking a callback per top-level message
    /// </summary>
    [Experimental(RespRequest.ExperimentalDiagnosticID)]
    [SuppressMessage("ApiDesign", "RS0027:API with optional parameter(s) should have the most parameters amongst its public overloads", Justification = "Validated")]
    public static async Task<long> ReplayAsync(string path, Message message, CancellationToken cancellationToken = default)
    {
        using var file = File.OpenRead(path);
        return await ReplayAsync(file, message, cancellationToken);
    }

    /// <summary>
    /// Iterate over a RESP stream invoking a callback per top-level message
    /// </summary>
    [SuppressMessage("ApiDesign", "RS0027:API with optional parameter(s) should have the most parameters amongst its public overloads", Justification = "Validated")]
    [Experimental(RespRequest.ExperimentalDiagnosticID)]
    public static async Task<long> ReplayAsync(Stream source, Message message, CancellationToken cancellationToken = default)
    {
        await using var resp = RespSource.Create(source);
        long count = 0;
        while (true)
        {
            LeasedSequence<byte> payload = await resp.ReadNextAsync(cancellationToken).ForAwait();
            if (payload.IsEmpty) break; // natural EOF
            message(new(payload));
            count++;
        }
        return count;
    }

    /// <summary>
    /// Replay the RESP messages for a pair of streams, invoking a callback per operation
    /// </summary>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "Necessary overload")]
    [Experimental(RespRequest.ExperimentalDiagnosticID)]
    public static async Task<long> ReplayAsync(Stream @out, Stream @in, MessagePair pair, CancellationToken cancellationToken = default)
    {
        await using var outSource = RespSource.Create(@out);
        await using var inSource = RespSource.Create(@in);

        long count = 0;
        while (true)
        {
            LeasedSequence<byte> sent = await outSource.ReadNextAsync(cancellationToken).ForAwait(), received;
            try
            {
                bool isOutOfBand;
                do
                {
                    received = await inSource.ReadNextAsync(cancellationToken).ForAwait();
                    isOutOfBand = IsOutOfBand(received);
                    if (isOutOfBand)
                    {
                        // spoof an empty request for OOB messages
                        pair(default, new(received));
                        received.Dispose();
                        count++;
                    }
                } while (isOutOfBand);
            }
            catch (Exception ex)
            {
                // if we got an exception following a command, spoof that as a pair,
                // so we see the message that had a corrupted reply
                if (!sent.IsEmpty)
                {
                    var spoofed = Encoding.ASCII.GetBytes("-ERR (managed) " + ex.Message);
                    pair(new(sent), new(spoofed));
                }
                throw; // still surface the original exception
            }

            bool invalid = sent.IsEmpty || received.IsEmpty; // no more paired messages
            if (!invalid)
            {
                pair(new(sent), new(received));
            }
            sent.Dispose();
            received.Dispose();
            if (invalid) break;
            count++;
        }
        return count;

        static bool IsOutOfBand(in LeasedSequence<byte> received)
        {
            var reader = new RespReader(received);
            if (reader.ReadNext())
            {
                if (reader.Prefix == RespPrefix.Push) return true;
                if (reader.Prefix == RespPrefix.Array)
                {
                    var length = reader.Length;
                    if (length >= 3 && reader.ReadNext() && reader.Prefix == RespPrefix.BulkString && reader.Length is 7 or 8)
                    {
                        return reader.Is("message"u8) || reader.Is("smessage"u8)
                            || (length >= 4 && reader.Is("pmessage"u8));
                    }
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Replay the RESP messages all the streams in a folder, invoking a callback per operation
    /// </summary>
    /// <param name="path">The directory of captured files to replay.</param>
    /// <param name="pair">Operation to perform per replayed message pair.</param>
    /// <param name="cancellationToken">Optional cancellation</param>
    [SuppressMessage("ApiDesign", "RS0027:API with optional parameter(s) should have the most parameters amongst its public overloads", Justification = "Necessary overload")]
    [Experimental(RespRequest.ExperimentalDiagnosticID)]
    public static async Task<long> ReplayAsync(string path, MessagePair pair, CancellationToken cancellationToken = default)
    {
        long total = 0;
        foreach (var outPath in Directory.EnumerateFiles(path, "*.out"))
        {
            var inPath = Path.ChangeExtension(outPath, "in");
            if (!File.Exists(outPath)) continue;

            using var outFile = File.OpenRead(outPath);
            using var inFile = File.OpenRead(inPath);
            total += await ReplayAsync(outFile, inFile, pair, cancellationToken).ForAwait();
        }
        return total;
    }

    private static async ValueTask<ContextualRedisResult> ReadOneAsync(PipeReader input, Arena<RawResult> arena, bool isInbound)
    {
        while (true)
        {
            var readResult = await input.ReadAsync().ForAwait();
            var buffer = readResult.Buffer;
            int handled = 0;
            var result = buffer.IsEmpty ? default : ProcessBuffer(arena, ref buffer, isInbound);
            input.AdvanceTo(buffer.Start, buffer.End);

            if (result.Result is not null) return result;

            if (handled == 0 && readResult.IsCompleted)
            {
                break; // no more data, or trailing incomplete messages
            }
        }
        return default;
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
        using var arena = new Arena<RawResult>();
        var input = StreamConnection.GetReader(stream);
        long total = 0, position = 0;
        while (true)
        {
            var readResult = await input.ReadAsync().ForAwait();
            var buffer = readResult.Buffer;
            int handled = 0;
            if (!buffer.IsEmpty)
            {
                try
                {
                    ProcessBuffer(arena, ref buffer, ref position, ref handled); // updates buffer.Start
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Invalid fragment starting at {position} (fragment {total + handled})", ex);
                }
                total += handled;
            }

            input.AdvanceTo(buffer.Start, buffer.End);

            if (handled == 0 && readResult.IsCompleted)
            {
                break; // no more data, or trailing incomplete messages
            }
        }
        return total;
    }
    private static void ProcessBuffer(Arena<RawResult> arena, ref ReadOnlySequence<byte> buffer, ref long position, ref int messageCount)
    {
        while (!buffer.IsEmpty)
        {
            var reader = new BufferReader(buffer);
            try
            {
                var result = TryParseResult(true, arena, in buffer, ref reader, true, null);
                if (result.HasValue)
                {
                    buffer = reader.SliceFromCurrent();
                    position += reader.TotalConsumed;
                    messageCount++;
                }
                else
                {
                    break; // remaining buffer isn't enough; give up
                }
            }
            finally
            {
                arena.Reset();
            }
        }
    }

    private readonly struct ContextualRedisResult
    {
        public readonly RedisResult? Result;
        public readonly bool IsOutOfBand;
        public ContextualRedisResult(RedisResult? result, bool isOutOfBand)
        {
            Result = result;
            IsOutOfBand = isOutOfBand;
        }
    }

    private static ContextualRedisResult ProcessBuffer(Arena<RawResult> arena, ref ReadOnlySequence<byte> buffer, bool isInbound)
    {
        if (!buffer.IsEmpty)
        {
            var reader = new BufferReader(buffer);
            try
            {
                var result = TryParseResult(true, arena, in buffer, ref reader, true, null);
                bool isOutOfBand = result.Resp3Type == ResultType.Push
                    || (isInbound && result.Resp2TypeArray == ResultType.Array && IsArrayOutOfBand(result));
                if (result.HasValue)
                {
                    buffer = reader.SliceFromCurrent();
                    if (!RedisResult.TryCreate(null, result, out var parsed))
                    {
                        throw new InvalidOperationException("Unable to parse raw result to RedisResult");
                    }
                    return new(parsed, isOutOfBand);
                }
            }
            finally
            {
                arena.Reset();
            }
        }
        return default;

        static bool IsArrayOutOfBand(in RawResult result)
        {
            var items = result.GetItems();
            return (items.Length >= 3 && (items[0].IsEqual(message) || items[0].IsEqual(smessage)))
                || (items.Length >= 4 && items[0].IsEqual(pmessage));

        }
    }

    private static readonly CommandBytes message = "message", pmessage = "pmessage", smessage = "smessage";

    /// <summary>
    /// Create a new instance of a <see cref="LoggingTunnel"/>
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

    private class DirectoryLoggingTunnel : LoggingTunnel
    {
        private readonly string path;
        private int _nextIndex = -1;

        internal DirectoryLoggingTunnel(string path, ConfigurationOptions? options = null, Tunnel? tail = null)
            : base(options, tail)
        {
            this.path = path;
            if (!Directory.Exists(path)) throw new InvalidOperationException("Directly does not exist: " + path);
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
    /// Perform logging on the provided stream
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

        var ssl = new SslStream(stream, false,
            _options.CertificateValidationCallback ?? PhysicalConnection.GetAmbientIssuerCertificateCallback(),
            _options.CertificateSelectionCallback ?? PhysicalConnection.GetAmbientClientCertificateCallback(),
            EncryptionPolicy.RequireEncryption);

#if NETCOREAPP3_1_OR_GREATER
        var configOptions = _options.SslClientAuthenticationOptions?.Invoke(host);
        if (configOptions is not null)
        {
            await ssl.AuthenticateAsClientAsync(configOptions).ForAwait();
        }
        else
        {
            ssl.AuthenticateAsClient(host, _options.SslProtocols, _options.CheckCertificateRevocation);
        }
#else
        ssl.AuthenticateAsClient(host, _options.SslProtocols, _options.CheckCertificateRevocation);
#endif
        return ssl;
    }

    /// <summary>
    /// Get a typical text representation of a redis command
    /// </summary>
    [Experimental(RespRequest.ExperimentalDiagnosticID)]
    public static string? DefaultFormatRequest(ref RespReader value)
    {
        if (!value.ReadNext()) return null;
        if (value.Prefix == RespPrefix.Array && !value.IsNull())
        {
            var count = value.ChildCount;
            var sb = new StringBuilder();
            for (int i = 0; i < count && value.ReadNext(); i++)
            {
                if (i != 0) sb.Append(' ');
                sb.Append(GetSimpleString(ref value));
            }
            return sb.ToString();
        }
        return GetSimpleString(ref value);
    }

    /// <summary>
    /// Get a typical text representation of a redis response
    /// </summary>
    public static string? DefaultFormatResponse(ref RespReader value)
        => value.ReadNext() ? GetSimpleString(ref value) : null;

    private static string GetSimpleString(ref RespReader value)
    {
        if (value.IsNull()) return "(null)";
        if (value.IsAggregate)
        {
            var s = $"[{value.Prefix} {value.ChildCount}]";
            value.SkipChildren();
            return s;
        }

        const int MAX_DISPLAY_BYTES = 50;
        if (value.Length <= MAX_DISPLAY_BYTES)
        {
            var blob = value.TryGetValueSpan(out var tmp) ? tmp : value.CopyTo(stackalloc byte[MAX_DISPLAY_BYTES]);
            int i;
            for (i = 0; i < blob.Length; i++)
            {
                char c = (char)blob[i];
                if (c < ' ' || c > '~') break;
            }
            if (i == blob.Length) // did we check it all?
            {
                string content;
#if NETCOREAPP3_1_OR_GREATER
                content = Resp2Writer.UTF8.GetString(blob);
#else
                unsafe
                {
                    fixed(byte* ptr = blob)
                    {
                        content = Resp2Writer.UTF8.GetString(ptr, blob.Length);
                    }
                }
#endif

                return value.Prefix switch
                {
                    RespPrefix.SimpleError or RespPrefix.BulkError => "-" + content,
                    _ => content,
                };
            }
        }
        return $"({value.Length} bytes)";
    }

    /// <summary>
    /// Get a typical text representation of a redis command
    /// </summary>
    public static string DefaultFormatRequest(RedisResult value)
    {
        try
        {
            if (value.IsNull) return "(null)";
            if (value.Resp2Type == ResultType.Array)
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
        catch {}
        return value.Resp3Type.ToString();

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
    /// Get a typical text representation of a redis response
    /// </summary>
    public static string DefaultFormatResponse(RedisResult value)
    {
        try
        {
            if (value.IsNull) return "(null)";
            switch (value.Resp2Type)
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
        return value.Resp3Type.ToString();
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
            await _writes.FlushAsync().ForAwait();
            await _inner.FlushAsync().ForAwait();
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
            await _writes.WriteAsync(buffer, offset, count, cancellationToken).ForAwait();
            await _inner.WriteAsync(buffer, offset, count, cancellationToken).ForAwait();
        }
#if NETCOREAPP3_0_OR_GREATER
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _writes.Write(buffer);
            _inner.Write(buffer);
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            await _writes.WriteAsync(buffer, cancellationToken).ForAwait();
            await _inner.WriteAsync(buffer, cancellationToken).ForAwait();
        }
#endif
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
