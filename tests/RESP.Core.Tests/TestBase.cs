using Resp;
using Xunit;

namespace RESP.Core.Tests;

public abstract class TestBase(ConnectionFixture fixture, ITestOutputHelper log)
{
    public IRespConnection GetConnection() => fixture.GetConnection();
    public void Log(string message) => log?.WriteLine(message);
}
