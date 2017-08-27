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
        public TextWriterOutputHelper(ITestOutputHelper outputHelper) => Output = outputHelper;

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
            Buffer.Clear();
        }
    }
}
