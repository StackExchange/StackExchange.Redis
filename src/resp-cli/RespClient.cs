using System;
using System.Globalization;
using System.Net;
using System.Reflection;
using RESPite.Resp;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;
using RESPite.Transports;

namespace StackExchange.Redis;

internal static class RespClient
{
    public static string ParseCommand(string line, out object[] args)
    {
        try
        {
            return Utils.Parse(line, out args);
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

    internal static async Task RunClient(IRequestResponseTransport transport, string? command)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                WriteLine("Authenticating...", null, null);
            }
            do
            {
                if (command is null) break; // EOF

                LeasedStrings.Builder builder = default;
                foreach (var item in Utils.Tokenize(command))
                {
                    builder.Add(item);
                }
                using var cmd = builder.Create();
                if (!cmd.IsEmpty)
                {
                    WriteResult(await transport.SendAsync(cmd, CommandWriter.AdHoc, LeasedRespResult.Reader));
                }

                command = ReadLine();
            }
            while (true);
        }
        catch (Exception ex)
        {
            WriteLine(ex.Message, ConsoleColor.Red, ConsoleColor.White);
            // and exit, no idea what happened
        }

        static void WriteResult(LeasedRespResult result)
        {
            var reader = new RespReader(result.Span);
            if (reader.TryReadNext())
            {
                WriteValue(ref reader, 0, -1);
            }
        }

        static void WriteValue(ref RespReader reader, int indent, int index)
        {
            if (reader.IsScalar)
            {
                WriteString(
                    ref reader,
                    indent,
                    index,
                    reader.IsError ? ConsoleColor.Red : null,
                    reader.IsError ? ConsoleColor.Gray : null);
            }
            else if (reader.IsAggregate)
            {
                WriteArray(ref reader, indent, index);
            }
        }

        static void WriteArray(ref RespReader reader, int indent, int index)
        {
            WriteHeader(reader.Prefix, indent, index);
            if (reader.IsNull)
            {
                WriteNull();
            }
            else if (reader.ChildCount == 0)
            {
                WriteLine("(empty)", ConsoleColor.Green, ConsoleColor.DarkGray);
            }
            else
            {
                var count = reader.ChildCount;
                WriteLine($"{count}", ConsoleColor.Green, ConsoleColor.DarkGray);
                indent++;
                for (int i = 0; i < count; i++)
                {
                    if (reader.TryReadNext())
                    {
                        WriteValue(ref reader, indent, i);
                    }
                }
            }
        }

        static void Indent(int indent)
        {
            while (indent-- > 0) Write(" ", null, null);
        }

        static void WriteHeader(RespPrefix prefix, int indent, int index)
        {
            Indent(indent);
            if (index >= 0)
            {
                Write($"[{index}]", ConsoleColor.White, ConsoleColor.DarkBlue);
            }
            Write(((char)prefix).ToString(), ConsoleColor.White, ConsoleColor.DarkBlue);
            Write(" ", null, null);
        }
        static void WriteString(ref RespReader reader, int indent, int index, ConsoleColor? foreground = null, ConsoleColor? background = null)
        {
            WriteHeader(reader.Prefix, indent, index);
            if (reader.IsNull)
            {
                WriteNull();
            }
            else
            {
                WriteLine(reader.ReadString() ?? "", foreground, background);
            }
        }

        static void WriteNull()
        {
            WriteLine("(nil)", ConsoleColor.Blue, ConsoleColor.Yellow);
        }

        static string? ReadLine()
        {
            Console.Write("> ");
            return Console.ReadLine();
        }
    }
}
