using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.RoundTripUnitTests;

/// <summary>
/// Verifies the exact wire bytes of the individual <c>HIMPORT</c> messages. The field-set name is written as an
/// 8-byte bulk string (the token's opaque id); it is derived from the live token here rather than hard-coded, since
/// the id is a process-wide monotonic counter.
/// </summary>
/// <remarks>
/// On the new core a <c>HashImport</c> call is one <c>PREPARE</c>/<c>SET</c> pair: the <c>PREPARE</c> is a
/// gated preamble, and an executor that cannot write one alongside (as here) sends it first, then the
/// <c>SET</c>. So the two frames are asserted together, in order.
/// </remarks>
public class HashImport(ITestOutputHelper log)
{
    // RESP bulk-string encoding of an arbitrary byte payload (may contain non-printable bytes).
    private static string Bulk(byte[] bytes)
    {
        var sb = new StringBuilder().Append('$').Append(bytes.Length).Append("\r\n");
        foreach (var b in bytes) sb.Append((char)b);
        return sb.Append("\r\n").ToString();
    }

    [Fact(Timeout = 5000)]
    public async Task PrepareThenSet_RoundTrips()
    {
        var token = StackExchange.Redis.HashImport.Create("name", "email", "age");
        byte[] name = BitConverter.GetBytes(token.Id);
        var executor = new RoundTripExecutor("+OK\r\n");

        await RoundTrip.Database(executor).HashImportAsync("user:1", token, new RedisValue[] { "a", "b", "c" });

        RoundTrip.AssertSent(
            executor,
            log,
            // HIMPORT PREPARE <field-set> name email age
            "*6\r\n$7\r\nHIMPORT\r\n$7\r\nPREPARE\r\n" + Bulk(name) + "$4\r\nname\r\n$5\r\nemail\r\n$3\r\nage\r\n",
            // HIMPORT SET user:1 <field-set> a b c
            "*7\r\n$7\r\nHIMPORT\r\n$3\r\nSET\r\n$6\r\nuser:1\r\n" + Bulk(name) + "$1\r\na\r\n$1\r\nb\r\n$1\r\nc\r\n");
    }

    [Fact(Timeout = 5000)]
    public async Task Set_RoundTrips()
    {
        var token = StackExchange.Redis.HashImport.Create("f1", "f2");
        byte[] name = BitConverter.GetBytes(token.Id);
        var executor = new RoundTripExecutor("+OK\r\n");

        await RoundTrip.Database(executor).HashImportAsync("user:1", token, new RedisValue[] { "v1", "v2" });

        // HIMPORT SET user:1 <field-set> v1 v2
        RoundTrip.AssertSent(
            executor,
            log,
            "*5\r\n$7\r\nHIMPORT\r\n$7\r\nPREPARE\r\n" + Bulk(name) + "$2\r\nf1\r\n$2\r\nf2\r\n",
            "*6\r\n$7\r\nHIMPORT\r\n$3\r\nSET\r\n$6\r\nuser:1\r\n" + Bulk(name) + "$2\r\nv1\r\n$2\r\nv2\r\n");
    }

    [Fact(Timeout = 5000)]
    public async Task Set_SingleValue_RoundTrips()
    {
        var token = StackExchange.Redis.HashImport.Create("only");
        byte[] name = BitConverter.GetBytes(token.Id);
        var executor = new RoundTripExecutor("+OK\r\n");

        await RoundTrip.Database(executor).HashImportAsync("k", token, new RedisValue[] { "v" });

        RoundTrip.AssertSent(
            executor,
            log,
            "*4\r\n$7\r\nHIMPORT\r\n$7\r\nPREPARE\r\n" + Bulk(name) + "$4\r\nonly\r\n",
            "*5\r\n$7\r\nHIMPORT\r\n$3\r\nSET\r\n$1\r\nk\r\n" + Bulk(name) + "$1\r\nv\r\n");
    }

    [Fact(Timeout = 5000)]
    public async Task Discard_RoundTrips()
    {
        var token = StackExchange.Redis.HashImport.Create("f");
        byte[] name = BitConverter.GetBytes(token.Id);
        var executor = new RoundTripExecutor("+OK\r\n");

        // DISCARD is only ever sent by disposal, against the servers the token was prepared on, so there is
        // no public route to it without a live connection; reach the composer directly
        var sendDiscard = typeof(StackExchange.Redis.HashImport).GetMethod("SendDiscard", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(sendDiscard);
        var pending = (ValueTask<bool>)sendDiscard.Invoke(token, [new RespContext().WithExecutor(executor)])!;
        await pending;

        // HIMPORT DISCARD <field-set>
        RoundTrip.AssertSent(executor, log, "*3\r\n$7\r\nHIMPORT\r\n$7\r\nDISCARD\r\n" + Bulk(name));
    }
}
