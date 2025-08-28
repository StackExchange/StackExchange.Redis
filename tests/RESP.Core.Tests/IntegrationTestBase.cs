using System.Runtime.CompilerServices;
using Resp;
using RESPite.Redis;
using Xunit;

namespace RESP.Core.Tests;

public abstract class IntegrationTestBase(ConnectionFixture fixture, ITestOutputHelper log)
{
    public IRespConnection GetConnection(out RespContext context, [CallerMemberName] string caller = "")
    {
         // most of the time, they'll be using a key from Me(), so: pre-emptively nuke it
         var conn = fixture.GetConnection();
         context = new(conn, TestContext.Current.CancellationToken);
         context.Keys.Del(caller);
         return conn;
    }

    public void Log(string message) => log?.WriteLine(message);

    protected string Me([CallerMemberName] string caller = "") => caller;
}
