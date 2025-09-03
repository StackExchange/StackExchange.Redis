using System.Runtime.CompilerServices;
using RESPite;
using RESPite.Redis.Alt;
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

    public void Log(string message) => log?.WriteLine(message);

    protected string Me([CallerMemberName] string caller = "") => caller;
}
