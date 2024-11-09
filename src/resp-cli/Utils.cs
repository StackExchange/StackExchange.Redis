using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using RESPite.Resp;
using RESPite.Transports;

namespace StackExchange.Redis;

public static class Utils
{
    internal static async Task<IRequestResponseTransport?> ConnectAsync(string host, int port, bool tls, Action<string>? log)
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

            return conn.CreateTransport().RequestResponse(RespFrameScanner.Default);
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

    internal static string GetSimpleText(LeasedRespResult value, int includeItems)
    {
        var reader = new RespReader(value.Span);
        if (TryGetSimpleText(ref reader, includeItems, out _, out var s) && !reader.TryReadNext())
        {
            return s;
        }
        return value.ToString(); // fallback
    }

    internal static bool TryGetSimpleText(ref RespReader reader, int includeItems, out bool isAggregate, [NotNullWhen(true)] out string? value, bool iterateChildren = true)
    {
        value = null;
        if (!reader.TryReadNext())
        {
            isAggregate = false;
            return false;
        }
        isAggregate = reader.IsAggregate;
        char prefix = (char)reader.Prefix;
        if (reader.IsScalar)
        {
            value = $"{prefix} {reader.ReadString()}";
            return true;
        }
        if (reader.IsAggregate)
        {
            var count = reader.ChildCount;
            if (!iterateChildren)
            {
                value = $"{prefix} {count}";
                return true;
            }

            if (count > includeItems && count != 0)
            {
                value = $"{prefix} {count}";
                reader.SkipChildren();
                return true;
            }

            var sb = new StringBuilder();
            sb.Append(prefix).Append(" ").Append(count).Append(" [");
            for (int i = 0; i < count; i++)
            {
                if (i != 0) sb.Append(",");
                if (!(reader.TryReadNext() && TryGetSimpleText(ref reader, 0, out _, out var s)))
                {
                    value = null;
                    return false;
                }
                sb.Append(s);
            }
            value = sb.Append("]").ToString();
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
