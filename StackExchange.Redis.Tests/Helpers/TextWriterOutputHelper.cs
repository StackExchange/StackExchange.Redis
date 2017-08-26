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
            if (value == '\n')
            {
                var text = Buffer.ToString();
                Output.WriteLine(text);
                Buffer.Clear();
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
                var text = Buffer.ToString();
                Output.WriteLine(text);
                Buffer.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
