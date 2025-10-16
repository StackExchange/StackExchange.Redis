using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using RESPite.Redis.Alt;
using RESPite.StackExchange.Redis;
using StackExchange.Redis;
using Xunit;

namespace RESPite.Tests;

public abstract class IntegrationTestBase(ConnectionFixture fixture, ITestOutputHelper log)
{
    public RespConnection GetConnection([CallerMemberName] string caller = "")
    {
         var conn = fixture.GetConnection(); // includes cancellation from the test
         // most of the time, they'll be using a key from Me(), so: pre-emptively nuke it
         conn.Context.AsKeys().Del(caller);
         return conn;
    }

    public async ValueTask<RespConnection> GetConnectionAsync([CallerMemberName] string caller = "")
    {
        var conn = fixture.GetConnection(); // includes cancellation from the test
        // most of the time, they'll be using a key from Me(), so: pre-emptively nuke it
        await conn.Context.AsKeys().DelAsync(caller).ConfigureAwait(false);
        return conn;
    }

    public IDatabase AsDatabase(RespConnection conn, int db = 0) => new RespContextDatabase(fixture.Multiplexer, conn, db);

    public void Log(string message) => log?.WriteLine(message);

    protected string Me([CallerMemberName] string caller = "") => caller;
}
