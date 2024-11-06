using System.Globalization;
using System.Net;
using StackExchange.Redis;

internal static class RespClient
{
    public static string ParseCommand(string line, out object[] args)
    {
        try
        {
            return CommandParser.Parse(line, out args);
        }
        catch (Exception ex)
        {
            // log only (treat as blank input (i.e. repeat input loop)
            WriteLine(ex.Message, ConsoleColor.Red, ConsoleColor.White);
            args = Array.Empty<object>();
            return "";
        }
    }

    private static void Write(string message, ConsoleColor? foreground, ConsoleColor? background)
    {
        var fg = Console.ForegroundColor;
        var bg = Console.BackgroundColor;
        try
        {
            if (foreground != null) Console.ForegroundColor = foreground.Value;
            if (background != null) Console.BackgroundColor = background.Value;
            Console.Write(message);
        }
        finally
        {
            Console.ForegroundColor = fg;
            Console.BackgroundColor = bg;
        }
    }

    private static void WriteLine(string message, ConsoleColor? foreground, ConsoleColor? background)
    {
        var fg = Console.ForegroundColor;
        var bg = Console.BackgroundColor;
        try
        {
            if (foreground != null) Console.ForegroundColor = foreground.Value;
            if (background != null) Console.BackgroundColor = background.Value;
            Console.Write(message);
        }
        finally
        {
            Console.ForegroundColor = fg;
            Console.BackgroundColor = bg;
        }
        Console.WriteLine();
    }

    internal static async Task RunClient(string host, int port)
    {
        try
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
            };
            Console.WriteLine($"Connecting to {config}...");
            config.AllowAdmin = true;
            using var muxer = await ConnectionMultiplexer.ConnectAsync(config);

            var (connected, dbCount) = await VerifyConnectedAsync(muxer);
            if (!connected) return;

            var db = muxer.GetDatabase();
            while (true)
            {
                var line = ReadLine(db.Database);
                if (line is null) break;

                var cmd = ParseCommand(line, out var args);
                if (string.IsNullOrWhiteSpace(cmd)) continue; // no input

                try
                {
                    var result = await db.ExecuteAsync(cmd, args);
                    WriteValue(result, 0, -1);
                }
                catch (RedisServerException ex)
                {
                    WriteString(RedisResult.Create(ex.Message, ResultType.SimpleString), "-", 0, -1, ConsoleColor.Red, ConsoleColor.Gray);
                }
            }
        }
        catch (Exception ex)
        {
            WriteLine(ex.Message, ConsoleColor.Red, ConsoleColor.White);
            // and exit, no idea what happened
        }

        static void WriteValue(RedisResult value, int indent, int index)
        {
            switch (value.Resp3Type)
            {
                case ResultType.BulkString:
                    WriteString(value, "$", indent, index);
                    break;
                case ResultType.SimpleString:
                    WriteString(value, "+", indent, index);
                    break;
                case ResultType.VerbatimString:
                    WriteString(value, "=", indent, index);
                    break;
                case ResultType.Integer:
                    WriteString(value, ":", indent, index);
                    break;
                case ResultType.Double:
                    WriteString(value, ",", indent, index);
                    break;
                case ResultType.Null:
                    WriteString(value, "_", indent, index);
                    break;
                case ResultType.Boolean:
                    WriteString(value, "#", indent, index);
                    break;
                case ResultType.BigInteger:
                    WriteString(value, "(", indent, index);
                    break;
                case ResultType.Error:
                    WriteString(value, "-", indent, index, ConsoleColor.Red, ConsoleColor.Gray);
                    break;
                case ResultType.BlobError:
                    WriteString(value, "!", indent, index, ConsoleColor.Red, ConsoleColor.Gray);
                    break;
                case ResultType.Array:
                    WriteArray(value, "*", indent, index);
                    break;
                case ResultType.Map:
                    WriteArray(value, "%", indent, index);
                    break;
                case ResultType.Set:
                    WriteArray(value, "~", indent, index);
                    break;
                default:
                    WriteString(value, "?", indent, index);
                    break;
            }
        }

        static void WriteArray(RedisResult value, string token, int indent, int index)
        {
            WriteHeader(token, indent, index);
            if (value.IsNull)
            {
                WriteNull();
            }
            else if (value.Length == 0)
            {
                WriteLine("(empty)", ConsoleColor.Green, ConsoleColor.DarkGray);
            }
            else
            {
                WriteLine($"{value.Length}", ConsoleColor.Green, ConsoleColor.DarkGray);
                indent++;
                var arr = (RedisResult[])value!;
                for (int i = 0; i < arr.Length; i++)
                {
                    WriteValue(arr[i], indent, i);
                }
            }
        }

        static void Indent(int indent)
        {
            while (indent-- > 0) Write(" ", null, null);
        }

        static void WriteHeader(string token, int indent, int index)
        {
            Indent(indent);
            if (index >= 0)
            {
                Write($"[{index}]", ConsoleColor.White, ConsoleColor.DarkBlue);
            }
            Write(token, ConsoleColor.White, ConsoleColor.DarkBlue);
            Write(" ", null, null);
        }
        static void WriteString(RedisResult value, string token, int indent, int index, ConsoleColor? foreground = null, ConsoleColor? background = null)
        {
            WriteHeader(token, indent, index);
            if (value.IsNull)
            {
                WriteNull();
            }
            else
            {
                WriteLine((string)value!, foreground, background);
            }
        }

        static void WriteNull()
        {
            WriteLine("(nil)", ConsoleColor.Blue, ConsoleColor.Yellow);
        }

        static async Task<(bool Connected, int Databases)> VerifyConnectedAsync(ConnectionMultiplexer muxer)
        {
            foreach (var server in muxer.GetServers())
            {
                // show some details about the first server we connect to
                if (server.IsConnected)
                {
                    foreach (var grp in await server.InfoAsync("server"))
                    {
                        Console.WriteLine($"# {grp.Key}");
                        foreach (var pair in grp)
                        {
                            if (pair.Key.EndsWith("version") || pair.Key == "redis_mode")
                            {
                                Console.WriteLine($"{pair.Key}:\t{pair.Value}");
                            }
                        }
                    }
                    int dbCount = -1;
                    foreach (var token in await server.ConfigGetAsync("databases"))
                    {
                        Console.WriteLine($"{token.Key}:\t{token.Value}");
                        if (int.TryParse(token.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmp))
                        {
                            dbCount = tmp;
                        }
                    }
                    return (true, dbCount);
                }
            }
            return (false, -1);
        }

        static string? ReadLine(int db)
        {
            if (db > 1) Console.Write(db);
            Console.Write("> ");
            return Console.ReadLine();
        }
    }
}
