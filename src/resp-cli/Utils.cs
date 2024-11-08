using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using RESPite.Resp;
using static StackExchange.Redis.RespDesktop;

namespace StackExchange.Redis;

public static class Utils
{
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

    public static string GetSimpleText(RedisResult value, int includeItems, out bool isAggregate)
    {
        var type = value.Resp3Type;
        switch (type)
        {
            case ResultType.Map:
            case ResultType.Array:
            case ResultType.Set:
                isAggregate = true;
                if (value.Length > includeItems && value.Length != 0)
                {
                    return $"{GetPrefix(type)} {value.Length}";
                }
                var sb = new StringBuilder();
                sb.Append(GetPrefix(type)).Append(" ").Append(value.Length).Append(" [");
                for (int i = 0; i < value.Length; i++)
                {
                    if (i != 0) sb.Append(",");
                    sb.Append(GetSimpleText(value[i], 0, out _));
                }
                return sb.Append("]").ToString();
            default:
                isAggregate = false;
                return $"{GetPrefix(type)} {value.ToString()}";
        }

        static string GetPrefix(ResultType type)
            => type switch
            {
                ResultType.SimpleString => "+",
                ResultType.Error => "-",
                ResultType.Integer => ":",
                ResultType.BulkString => "$",
                ResultType.Array => "*",
                ResultType.Null => "_",
                ResultType.Boolean => "#",
                ResultType.Double => ",",
                ResultType.BigInteger => "(",
                ResultType.BlobError => "!",
                ResultType.VerbatimString => "=",
                ResultType.Map => "%",
                ResultType.Set => "~",
                ResultType.Attribute => "|",
                ResultType.Push => ">",
                _ => "???",
            };
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

    internal static bool CertificateValidation(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is X509Certificate2 cert2)
        {
            Console.WriteLine($"TLS: {certificate.Subject} ({cert2.Thumbprint})");
        }
        else
        {
            Console.WriteLine($"TLS: {certificate?.Subject}");
        }
        if (sslPolicyErrors != SslPolicyErrors.None)
        {
            Console.WriteLine($"Ignoring certificate policy failure (ignoring): {sslPolicyErrors}");
        }
        return true;
    }
}
