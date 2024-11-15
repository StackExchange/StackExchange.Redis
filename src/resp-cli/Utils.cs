using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using RESPite.Resp;
using RESPite.Resp.Readers;
using RESPite.Transports;

namespace StackExchange.Redis;

public static class Utils
{
    internal static string Truncate(string? value, int length)
    {
        value ??= "";
        if (value.Length > length)
        {
            if (length <= 1) return "\u2026";
            return value.Substring(0, length - 1) + "\u2026";
        }
        return value;
    }

    internal static async Task<IRequestResponseTransport?> ConnectAsync(
        string host,
        int port,
        bool tls,
        Action<string>? log,
        IFrameScanner<RespFrameScanner.RespFrameState>? frameScanner = null,
        FrameValidation validateOutbound = FrameValidation.Debug)
    {
        Socket? socket = null;
        Stream? conn = null;
        try
        {
            var ep = BuildEndPoint(host, port);
            socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            log?.Invoke($"Connecting to {host} on TCP port {port}...");
            await socket.ConnectAsync(ep);

            conn = new NetworkStream(socket);
            if (tls)
            {
                log?.Invoke("Establishing TLS...");
                var ssl = new SslStream(conn);
                conn = ssl;
                var options = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = Utils.CertificateValidation(log),
                    TargetHost = host,
                };
                await ssl.AuthenticateAsClientAsync(options);
            }

            return conn.CreateTransport().RequestResponse(frameScanner ?? RespFrameScanner.Default, validateOutbound);
        }
        catch (Exception ex)
        {
            conn?.Dispose();
            socket?.Dispose();

            log?.Invoke(ex.Message);
            return null;
        }
    }

    internal static string GetHandshake(string? user, string? pass, bool resp3)
    {
        if (resp3)
        {
            if (!string.IsNullOrWhiteSpace(pass))
            {
                if (string.IsNullOrWhiteSpace(user))
                {
                    return $"HELLO 3 AUTH default {pass}";
                }
                else
                {
                    return $"HELLO 3 AUTH {user} {pass}";
                }
            }
            else
            {
                return "HELLO 3";
            }
        }
        else if (!string.IsNullOrWhiteSpace(pass))
        {
            if (string.IsNullOrWhiteSpace(user))
            {
                return $"AUTH {user} {pass}";
            }
            else
            {
                return $"AUTH {pass}";
            }
        }
        return "";
    }

    internal static string GetSimpleText(in ReadOnlySequence<byte> content, AggregateMode childMode = AggregateMode.Full, int sizeHint = int.MaxValue)
    {
        try
        {
            var reader = new RespReader(content, throwOnErrorResponse: false);
            var sb = new StringBuilder();
            if (TryGetSimpleText(sb, ref reader, childMode, sizeHint: sizeHint) && !reader.TryReadNext())
            {
                return sb.ToString();
            }
        }
        catch
        {
            Debug.WriteLine(Encoding.UTF8.GetString(content));
            throw;
        }
        return Encoding.UTF8.GetString(content); // fallback
    }

    internal enum AggregateMode
    {
        Full,
        CountAndConsume,
        CountOnly,
    }

    public static string GetCommandText(in ReadOnlySequence<byte> payload, int sizeHint = int.MaxValue)
    {
        RespReader reader = new(payload);
        return GetCommandText(ref reader, sizeHint);
    }

    public static string GetCommandText(ref RespReader reader, int sizeHint = int.MaxValue)
    {
        reader.ReadNextAggregate();
        reader.Demand(RespPrefix.Array);

        var len = reader.ChildCount;
        reader.ReadNextScalar();
        reader.Demand(RespPrefix.BulkString);
        reader.DemandNotNull();
        string cmd = reader.ReadString()!;
        if (len != 1)
        {
            var sb = new StringBuilder(cmd);
            for (int i = 1; i < len; i++)
            {
                reader.ReadNextScalar();
                reader.Demand(RespPrefix.BulkString);
                reader.DemandNotNull();
                string orig = reader.ReadString()!;
                var s = Escape(reader.ReadString());
                if (orig.IndexOf(' ') >= 0 || orig.IndexOf('\"') >= 0) s = "\"" + s + "\"";
                sb.Append(' ').Append(s);
            }
            cmd = sb.ToString();
        }
        reader.ReadEnd();
        return cmd;
    }

    private static string? Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        value = value.Replace("'", "\\'");
        value = value.Replace("\"", "\\\"");
        return value;
    }

    internal static bool TryGetSimpleText(StringBuilder sb, ref RespReader reader, AggregateMode aggregateMode = AggregateMode.Full, int sizeHint = int.MaxValue)
    {
        if (!reader.TryReadNext())
        {
            return false;
        }

        char prefix = (char)reader.Prefix;

        if (reader.IsNull)
        {
            sb.Append(prefix).Append("(null)");
            return true;
        }
        if (reader.IsScalar)
        {
            sb.Append(prefix);
            switch (reader.Prefix)
            {
                case RespPrefix.SimpleString:
                    sb.Append(Escape(reader.ReadString()));
                    break;
                case RespPrefix.BulkString:
                    sb.Append("\"").Append(Escape(reader.ReadString())).Append("\"");
                    break;
                case RespPrefix.VerbatimString:
                    sb.Append("\"\"\"").Append(Escape(reader.ReadString())).Append("\"\"\"");
                    break;
                default:
                    sb.Append(reader.ReadString());
                    break;
            }
            return true;
        }
        if (reader.IsAggregate)
        {
            var count = reader.ChildCount;

            sb.Append(prefix).Append(count);
            switch (aggregateMode)
            {
                case AggregateMode.Full when count == 0:
                case AggregateMode.CountOnly:
                    return true;
                case AggregateMode.CountAndConsume:
                    reader.SkipChildren();
                    return true;
            }

            sb.Append(" [");
            for (int i = 0; i < count; i++)
            {
                if (i != 0 && sb.Length < sizeHint) sb.Append(",");
                if (sb.Length < sizeHint)
                {
                    if (!TryGetSimpleText(sb, ref reader, aggregateMode, sizeHint))
                    {
                        return false;
                    }
                }
                else
                {
                    // skip!
                    if (!reader.TryReadNext()) return false;
                    if (reader.IsAggregate) reader.SkipChildren();
                }
            }
            if (sb.Length < sizeHint) sb.Append("]");
            return true;
        }
        return false;
    }

    public static EndPoint BuildEndPoint(string host, int port)
    {
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return new IPEndPoint(ipAddress, port);
        }
        else
        {
            return new DnsEndPoint(host, port);
        }
    }

    public static string Parse(string value, out object[] args)
    {
        args = Array.Empty<object>();
        using var iter = Tokenize(value).GetEnumerator();
        if (iter.MoveNext())
        {
            var cmd = iter.Current;
            List<object>? list = null;
            while (iter.MoveNext())
            {
                (list ??= new()).Add(iter.Current);
            }
            if (list is not null) args = list.ToArray();
            return cmd;
        }
        return "";
    }

    public static IEnumerable<string> Tokenize(string value)
    {
        bool inQuote = false, prevWhitespace = true;
        int startIndex = -1;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '"' when inQuote: // end the current quoted string
                    yield return value.Substring(startIndex, i - startIndex);
                    startIndex = -1;
                    inQuote = false;
                    break;
                case '"' when startIndex < 0: // start a new quoted string
                    if (!prevWhitespace) UnableToParse();
                    inQuote = true;
                    startIndex = i + 1;
                    break;
                case '"':
                    UnableToParse();
                    break;
                default:
                    if (char.IsWhiteSpace(c))
                    {
                        if (startIndex >= 0 && !inQuote) // end non-quoted string
                        {
                            yield return value.Substring(startIndex, i - startIndex);
                            startIndex = -1;
                        }
                    }
                    else if (startIndex < 0) // start a new non-quoted token
                    {
                        if (!prevWhitespace) UnableToParse();

                        startIndex = i;
                    }
                    break;
            }
            prevWhitespace = !inQuote && char.IsWhiteSpace(c);
        }
        // anything left
        if (startIndex >= 0)
        {
            yield return value.Substring(startIndex, value.Length - startIndex);
        }

        static void UnableToParse() => throw new FormatException("Unable to parse input");
    }

    internal static RemoteCertificateValidationCallback CertificateValidation(Action<string>? log)
        => (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
        {
            if (certificate is X509Certificate2 cert2)
            {
                log?.Invoke($"Server certificate: {certificate.Subject} ({cert2.Thumbprint})");
            }
            else
            {
                log?.Invoke($"Server certificate: {certificate?.Subject}");
            }
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                log?.Invoke($"Ignoring certificate policy failure (ignoring): {sslPolicyErrors}");
            }
            return true;
        };
}
