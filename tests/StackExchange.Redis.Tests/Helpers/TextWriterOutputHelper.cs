using System;
using System.IO;
using System.Text;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Helpers
{
    public class TextWriterOutputHelper : TextWriter
    {
        private StringBuilder Buffer { get; } = new StringBuilder(2048);
        public override Encoding Encoding => Encoding.UTF8;
        private readonly ITestOutputHelper Output;
        private readonly bool ToConsole;
        public TextWriterOutputHelper(ITestOutputHelper outputHelper, bool echoToConsole)
        {
            Output = outputHelper;
            ToConsole = echoToConsole;
        }

        public override void WriteLine(string value)
        {
            try
            {
                base.Write(TestBase.Time());
                base.Write(": ");
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
            Output.WriteLine(text);
            if (ToConsole)
            {
                Console.WriteLine(text);
            }
            Buffer.Clear();
        }
    }
}
