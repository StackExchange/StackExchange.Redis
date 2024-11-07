using System.Net;
using System.Text;
using System.Xml.Linq;
using Terminal.Gui;

namespace StackExchange.Redis;

public static class Utils
{
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

    public static ConfigurationOptions BuildOptions(string host, int port)
    {
        EndPoint ep;
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            ep = new IPEndPoint(ipAddress, port);
        }
        else
        {
            ep = new DnsEndPoint(host, port);
        }
        var config = new ConfigurationOptions
        {
            EndPoints = { ep },
            AllowAdmin = true,
        };
        return config;
    }

    public static string Parse(string value, out object[] args)
    {
        args = Array.Empty<object>();
        using var iter = Tokenize(value);
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

    private static IEnumerator<string> Tokenize(string value)
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
}
