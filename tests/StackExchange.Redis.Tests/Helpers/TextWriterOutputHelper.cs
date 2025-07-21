﻿using System;
using System.IO;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests.Helpers;

public class TextWriterOutputHelper(ITestOutputHelper outputHelper) : TextWriter
{
    private StringBuilder Buffer { get; } = new StringBuilder(2048);
    private StringBuilder? Echo { get; set; }
    public override Encoding Encoding => Encoding.UTF8;
    private readonly ITestOutputHelper Output = outputHelper;

    public void EchoTo(StringBuilder sb) => Echo = sb;

    public void WriteLineNoTime(string? value)
    {
        try
        {
            base.WriteLine(value);
        }
        catch (Exception ex)
        {
            Console.Write("Attempted to write: ");
            Console.WriteLine(value);
            Console.WriteLine(ex);
        }
    }

    public override void WriteLine(string? value)
    {
        if (value is null)
        {
            return;
        }

        try
        {
            base.WriteLine(value);
        }
        catch (Exception ex)
        {
            Console.Write("Attempted to write: ");
            Console.WriteLine(value);
            Console.WriteLine(ex);
        }
    }

    public override void Write(char value)
    {
        if (value == '\n' || value == '\r')
        {
            // Ignore empty lines
            if (Buffer.Length > 0)
            {
                FlushBuffer();
            }
        }
        else
        {
            Buffer.Append(value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (Buffer.Length > 0)
        {
            FlushBuffer();
        }
        base.Dispose(disposing);
    }

    private void FlushBuffer()
    {
        var text = Buffer.ToString();
        try
        {
            Output.WriteLine(text);
        }
        catch (InvalidOperationException)
        {
            // Thrown when writing from a handler after a test has ended - just bail in this case
        }
        Echo?.AppendLine(text);
        Buffer.Clear();
    }
}
