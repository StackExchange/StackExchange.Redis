using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests.Booksleeve
{
    public class SocketModeTests : BookSleeveTestBase
    {
        public SocketModeTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void Equality()
        {
            Assert.True(SocketMode.Poll == SocketMode.Poll);
            Assert.True(SocketMode.Poll.Equals(SocketMode.Poll));
            Assert.True(SocketMode.Poll.Equals(SocketMode.Poll as object));
        }

        [Fact]
        public void Inequality()
        {
            Assert.True(SocketMode.Poll != SocketMode.Async);
            Assert.True(!SocketMode.Poll.Equals(SocketMode.Async));
            Assert.True(!SocketMode.Poll.Equals(SocketMode.Async as object));
            Assert.True(!SocketMode.Poll.Equals(""));
            Assert.True(!SocketMode.Poll.Equals(null));
        }
    }
}
