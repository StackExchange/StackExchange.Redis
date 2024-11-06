using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Net;
using StackExchange.Redis;

Option<string> hostOption = new(
    aliases: new[] { "--host", "-h" },
    description: "The host of the RESP server.");
hostOption.SetDefaultValue("127.0.0.1");

Option<int> portOption = new(
    aliases: new[] { "--port", "-p" },
    description: "The port of the RESP server.");
portOption.SetDefaultValue(6379);

RootCommand rootCommand = new(description: "Connects to a RESP server to issue ad-hoc commands.")
{
    hostOption,
    portOption,
};

rootCommand.SetHandler(
    (string host, int port) => RespClient.RunClient(host, port),
    hostOption,
    portOption);
return await rootCommand.InvokeAsync(args);

file static class RespClient
{
    public static async Task RunClient(string host, int port)
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
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(" ");

            object[] args = new string[parts.Length - 1];
            Array.Copy(parts, 1, args, 0, parts.Length - 1);
            try
            {
                var result = await db.ExecuteAsync(parts[0], args);
                WriteValue(result, 0);
            }
            catch (RedisServerException ex)
            {
                WriteString(RedisResult.Create(ex.Message, ResultType.SimpleString), "-", 0, ConsoleColor.Red, ConsoleColor.Gray);
            }
        }

        static void WriteValue(RedisResult value, int indent)
        {
            switch (value.Resp3Type)
            {
                case ResultType.BulkString:
                    WriteString(value, "$", indent);
                    break;
                case ResultType.SimpleString:
                    WriteString(value, "+", indent);
                    break;
                case ResultType.Integer:
                    WriteString(value, ":", indent);
                    break;
                case ResultType.Double:
                    WriteString(value, ",", indent);
                    break;
                case ResultType.Null:
                    WriteString(value, "_", indent);
                    break;
                case ResultType.Boolean:
                    WriteString(value, "#", indent);
                    break;
                case ResultType.BigInteger:
                    WriteString(value, "(", indent);
                    break;
                case ResultType.Error:
                    WriteString(value, "-", indent, ConsoleColor.Red, ConsoleColor.Gray);
                    break;
                case ResultType.BlobError:
                    WriteString(value, "!", indent, ConsoleColor.Red, ConsoleColor.Gray);
                    break;
                // case ResultType.Array:
                //    WriteArray
                default:
                    WriteString(value, "?", indent);
                    break;
            }
        }

        static void Indent(int indent)
        {
            while (indent-- > 0) Write(" ", null, null);
        }

        static void WriteString(RedisResult value, string token, int indent, ConsoleColor foreground = ConsoleColor.DarkGray, ConsoleColor background = ConsoleColor.Yellow)
        {
            Indent(indent);
            Write(token, null, null);
            Write(" ", null, null);
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

        static void Write(string message, ConsoleColor? foreground, ConsoleColor? background)
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

        static void WriteLine(string message, ConsoleColor? foreground, ConsoleColor? background)
        {
            var fg = Console.ForegroundColor;
            var bg = Console.BackgroundColor;
            try
            {
                if (foreground != null) Console.ForegroundColor = foreground.Value;
                if (background != null) Console.BackgroundColor = background.Value;
                Console.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = fg;
                Console.BackgroundColor = bg;
            }
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
