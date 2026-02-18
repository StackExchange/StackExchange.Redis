using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using RESPite.Messages;
using Xunit;

namespace StackExchange.Redis.Tests;

public class ResultProcessorUnitTests(ITestOutputHelper log)
{
    private const string ATTRIB_FOO_BAR = "|1\r\n+foo\r\n+bar\r\n";
    private static readonly ResultProcessor.Int64DefaultValueProcessor Int64DefaultValue999 = new(999);

    [Theory]
    [InlineData(":1\r\n", 1)]
    [InlineData("+1\r\n", 1)]
    [InlineData("$1\r\n1\r\n", 1)]
    [InlineData(",1\r\n", 1)]
    [InlineData(ATTRIB_FOO_BAR + ":1\r\n", 1)]
    [InlineData(":-42\r\n", -42)]
    [InlineData("+-42\r\n", -42)]
    [InlineData("$3\r\n-42\r\n", -42)]
    [InlineData(",-42\r\n", -42)]
    public void Int32(string resp, int value) => Assert.Equal(value, Execute(resp, ResultProcessor.Int32));

    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData("$4\r\nPONG\r\n")]
    public void FailingInt32(string resp) => ExecuteUnexpected(resp, ResultProcessor.Int32);

    [Theory]
    [InlineData(":1\r\n", 1)]
    [InlineData("+1\r\n", 1)]
    [InlineData("$1\r\n1\r\n", 1)]
    [InlineData(",1\r\n", 1)]
    [InlineData(ATTRIB_FOO_BAR + ":1\r\n", 1)]
    [InlineData(":-42\r\n", -42)]
    [InlineData("+-42\r\n", -42)]
    [InlineData("$3\r\n-42\r\n", -42)]
    [InlineData(",-42\r\n", -42)]
    public void Int64(string resp, long value) => Assert.Equal(value, Execute(resp, ResultProcessor.Int64));

    [Theory]
    [InlineData("+OK\r\n")]
    [InlineData("$4\r\nPONG\r\n")]
    public void FailingInt64(string resp) => ExecuteUnexpected(resp, ResultProcessor.Int64);

    [Theory]
    [InlineData(":42\r\n", 42.0)]
    [InlineData("+3.14\r\n", 3.14)]
    [InlineData("$4\r\n3.14\r\n", 3.14)]
    [InlineData(",3.14\r\n", 3.14)]
    [InlineData(ATTRIB_FOO_BAR + ",3.14\r\n", 3.14)]
    [InlineData(":-1\r\n", -1.0)]
    [InlineData("+inf\r\n", double.PositiveInfinity)]
    [InlineData(",inf\r\n", double.PositiveInfinity)]
    [InlineData("$4\r\n-inf\r\n", double.NegativeInfinity)]
    [InlineData(",-inf\r\n", double.NegativeInfinity)]
    [InlineData(",nan\r\n", double.NaN)]
    public void Double(string resp, double value) => Assert.Equal(value, Execute(resp, ResultProcessor.Double));

    [Theory]
    [InlineData("_\r\n", null)]
    [InlineData("$-1\r\n", null)]
    [InlineData(":42\r\n", 42L)]
    [InlineData("+42\r\n", 42L)]
    [InlineData("$2\r\n42\r\n", 42L)]
    [InlineData(",42\r\n", 42L)]
    [InlineData(ATTRIB_FOO_BAR + ":42\r\n", 42L)]
    public void NullableInt64(string resp, long? value) => Assert.Equal(value, Execute(resp, ResultProcessor.NullableInt64));

    [Theory]
    [InlineData("*1\r\n:99\r\n", 99L)]
    [InlineData("*1\r\n$-1\r\n", null)] // unit array with RESP2 null bulk string
    [InlineData("*1\r\n_\r\n", null)] // unit array with RESP3 null
    [InlineData(ATTRIB_FOO_BAR + "*1\r\n:99\r\n", 99L)]
    public void NullableInt64ArrayOfOne(string resp, long? value) => Assert.Equal(value, Execute(resp, ResultProcessor.NullableInt64));

    [Theory]
    [InlineData("*-1\r\n")] // null array
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n:1\r\n:2\r\n")] // two elements
    public void FailingNullableInt64ArrayOfNonOne(string resp) => ExecuteUnexpected(resp, ResultProcessor.NullableInt64);

    [Theory]
    [InlineData("_\r\n", null)]
    [InlineData("$-1\r\n", null)]
    [InlineData(":42\r\n", 42.0)]
    [InlineData("+3.14\r\n", 3.14)]
    [InlineData("$4\r\n3.14\r\n", 3.14)]
    [InlineData(",3.14\r\n", 3.14)]
    [InlineData(ATTRIB_FOO_BAR + ",3.14\r\n", 3.14)]
    public void NullableDouble(string resp, double? value) => Assert.Equal(value, Execute(resp, ResultProcessor.NullableDouble));

    [Theory]
    [InlineData("_\r\n", false)] // null = false
    [InlineData(":0\r\n", false)]
    [InlineData(":1\r\n", true)]
    [InlineData("#f\r\n", false)]
    [InlineData("#t\r\n", true)]
    [InlineData("+OK\r\n", true)]
    [InlineData(ATTRIB_FOO_BAR + ":1\r\n", true)]
    public void Boolean(string resp, bool value) => Assert.Equal(value, Execute(resp, ResultProcessor.Boolean));

    [Theory]
    [InlineData("*1\r\n:1\r\n", true)] // SCRIPT EXISTS returns array
    [InlineData("*1\r\n:0\r\n", false)]
    [InlineData(ATTRIB_FOO_BAR + "*1\r\n:1\r\n", true)]
    public void BooleanArrayOfOne(string resp, bool value) => Assert.Equal(value, Execute(resp, ResultProcessor.Boolean));

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n:1\r\n:0\r\n")] // two elements
    [InlineData("*1\r\n*1\r\n:1\r\n")] // nested array (not scalar)
    public void FailingBooleanArrayOfNonOne(string resp) => ExecuteUnexpected(resp, ResultProcessor.Boolean);

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("+world\r\n", "world")]
    [InlineData(":42\r\n", "42")]
    [InlineData("$-1\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "$3\r\nfoo\r\n", "foo")]
    public void String(string resp, string? value) => Assert.Equal(value, Execute(resp, ResultProcessor.String));

    [Theory]
    [InlineData("*1\r\n$3\r\nbar\r\n", "bar")]
    [InlineData(ATTRIB_FOO_BAR + "*1\r\n$3\r\nbar\r\n", "bar")]
    public void StringArrayOfOne(string resp, string? value) => Assert.Equal(value, Execute(resp, ResultProcessor.String));

    [Theory]
    [InlineData("*-1\r\n")] // null array
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // two elements
    [InlineData("*1\r\n*1\r\n$3\r\nfoo\r\n")] // nested array (not scalar)
    public void FailingStringArrayOfNonOne(string resp) => ExecuteUnexpected(resp, ResultProcessor.String);

    [Theory]
    [InlineData("+string\r\n", Redis.RedisType.String)]
    [InlineData("+hash\r\n", Redis.RedisType.Hash)]
    [InlineData("+zset\r\n", Redis.RedisType.SortedSet)]
    [InlineData("+set\r\n", Redis.RedisType.Set)]
    [InlineData("+list\r\n", Redis.RedisType.List)]
    [InlineData("+stream\r\n", Redis.RedisType.Stream)]
    [InlineData("+vectorset\r\n", Redis.RedisType.VectorSet)]
    [InlineData("+blah\r\n", Redis.RedisType.Unknown)]
    [InlineData("$-1\r\n", Redis.RedisType.None)]
    [InlineData("_\r\n", Redis.RedisType.None)]
    [InlineData("$0\r\n\r\n", Redis.RedisType.None)]
    [InlineData(ATTRIB_FOO_BAR + "$6\r\nstring\r\n", Redis.RedisType.String)]
    public void RedisType(string resp, RedisType value) => Assert.Equal(value, Execute(resp, ResultProcessor.RedisType));

    [Theory]
    [InlineData("*3\r\n:1\r\n:2\r\n:3\r\n", "1,2,3")]
    [InlineData("*2\r\n,42\r\n,-99\r\n", "42,-99")]
    [InlineData("*0\r\n", "")]
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n:10\r\n:20\r\n", "10,20")]
    public void Int64Array(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.Int64Array)));

    [Theory]
    [InlineData("*3\r\n$3\r\nfoo\r\n$3\r\nbar\r\n$3\r\nbaz\r\n", "foo,bar,baz")]
    [InlineData("*2\r\n+hello\r\n+world\r\n", "hello,world")]
    [InlineData("*0\r\n", "")]
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a,b")]
    public void StringArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.StringArray)));

    [Theory]
    [InlineData("*3\r\n:1\r\n:0\r\n:1\r\n", "True,False,True")]
    [InlineData("*2\r\n#t\r\n#f\r\n", "True,False")]
    [InlineData("*0\r\n", "")]
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n:1\r\n:0\r\n", "True,False")]
    public void BooleanArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.BooleanArray)));

    [Theory]
    [InlineData("*3\r\n$3\r\nfoo\r\n$3\r\nbar\r\n$3\r\nbaz\r\n", "foo,bar,baz")]
    [InlineData("*3\r\n$3\r\nfoo\r\n$-1\r\n$3\r\nbaz\r\n", "foo,,baz")] // null element in middle (RESP2)
    [InlineData("*3\r\n$3\r\nfoo\r\n_\r\n$3\r\nbaz\r\n", "foo,,baz")] // null element in middle (RESP3)
    [InlineData("*2\r\n:42\r\n:-99\r\n", "42,-99")]
    [InlineData("*0\r\n", "")]
    [InlineData("$3\r\nfoo\r\n", "foo")] // single bulk string treated as array
    [InlineData("$-1\r\n", "")] // null bulk string treated as empty array
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$1\r\na\r\n:1\r\n", "a,1")]
    public void RedisValueArray(string resp, string expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.RedisValueArray)));

    [Theory]
    [InlineData("*3\r\n$3\r\nfoo\r\n$-1\r\n$3\r\nbaz\r\n", "foo,,baz")] // null element in middle (RESP2)
    [InlineData("*3\r\n$3\r\nfoo\r\n_\r\n$3\r\nbaz\r\n", "foo,,baz")] // null element in middle (RESP3)
    [InlineData("*2\r\n$5\r\nhello\r\n$5\r\nworld\r\n", "hello,world")]
    [InlineData("*0\r\n", "")]
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a,b")]
    public void NullableStringArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.NullableStringArray)));

    [Theory]
    [InlineData("*3\r\n$3\r\nkey\r\n$4\r\nkey2\r\n$4\r\nkey3\r\n", "key,key2,key3")]
    [InlineData("*3\r\n$3\r\nkey\r\n$-1\r\n$4\r\nkey3\r\n", "key,(null),key3")] // null element in middle (RESP2)
    [InlineData("*3\r\n$3\r\nkey\r\n_\r\n$4\r\nkey3\r\n", "key,(null),key3")] // null element in middle (RESP3)
    [InlineData("*2\r\n$1\r\na\r\n$1\r\nb\r\n", "a,b")]
    [InlineData("*0\r\n", "")]
    [InlineData("*-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n", "foo,bar")]
    public void RedisKeyArray(string resp, string? expected) => Assert.Equal(expected, Join(Execute(resp, ResultProcessor.RedisKeyArray)));

    [Theory]
    [InlineData("*2\r\n+foo\r\n:42\r\n", 42)]
    [InlineData($"{ATTRIB_FOO_BAR}*2\r\n+foo\r\n:42\r\n", 42)]
    [InlineData($"*2\r\n{ATTRIB_FOO_BAR}+foo\r\n:42\r\n", 42)]
    [InlineData($"*2\r\n+foo\r\n{ATTRIB_FOO_BAR}:42\r\n", 42)]
    public void PubSubNumSub(string resp, long expected) => Assert.Equal(expected, Execute(resp, ResultProcessor.PubSubNumSub));

    [Theory]
    [InlineData("*-1\r\n")]
    [InlineData("_\r\n")]
    [InlineData(":42\r\n")]
    [InlineData("$-1\r\n")]
    [InlineData("*3\r\n+foo\r\n:42\r\n+bar\r\n")]
    [InlineData("*4\r\n+foo\r\n:42\r\n+bar\r\n:6\r\n")]
    public void FailingPubSubNumSub(string resp) => ExecuteUnexpected(resp, ResultProcessor.PubSubNumSub);

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("+world\r\n", "world")]
    [InlineData(":42\r\n", "42")]
    [InlineData("$-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "$3\r\nfoo\r\n", "foo")]
    public void ByteArray(string resp, string? expected)
    {
        var result = Execute(resp, ResultProcessor.ByteArray);
        if (expected is null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.NotNull(result);
            Assert.Equal(expected, Encoding.UTF8.GetString(result));
        }
    }

    [Theory]
    [InlineData("*-1\r\n")] // null array
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // array
    public void FailingByteArray(string resp) => ExecuteUnexpected(resp, ResultProcessor.ByteArray);

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("+world\r\n", "world")]
    [InlineData("$-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "$11\r\nclusterinfo\r\n", "clusterinfo")]
    // note that this test does not include a valid cluster nodes response
    public void ClusterNodesRaw(string resp, string? expected) => Assert.Equal(expected, Execute(resp, ResultProcessor.ClusterNodesRaw));

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // array
    public void FailingClusterNodesRaw(string resp) => ExecuteUnexpected(resp, ResultProcessor.ClusterNodesRaw);

    [Theory]
    [InlineData(":42\r\n", 42L)]
    [InlineData("+99\r\n", 99L)]
    [InlineData("$2\r\n10\r\n", 10L)]
    [InlineData(",123\r\n", 123L)]
    [InlineData(ATTRIB_FOO_BAR + ":42\r\n", 42L)]
    public void Int64DefaultValue(string resp, long expected) => Assert.Equal(expected, Execute(resp, Int64DefaultValue999));

    [Theory]
    [InlineData("_\r\n", 999L)] // null returns default
    [InlineData("$-1\r\n", 999L)] // null returns default
    public void Int64DefaultValueNull(string resp, long expected) => Assert.Equal(expected, Execute(resp, Int64DefaultValue999));

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n:1\r\n:2\r\n")] // array
    [InlineData("+notanumber\r\n")] // invalid number
    public void FailingInt64DefaultValue(string resp) => ExecuteUnexpected(resp, Int64DefaultValue999);

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("+world\r\n", "world")]
    [InlineData(":42\r\n", "42")]
    [InlineData("$-1\r\n", "(null)")]
    [InlineData("_\r\n", "(null)")]
    [InlineData(ATTRIB_FOO_BAR + "$3\r\nfoo\r\n", "foo")]
    public void RedisKey(string resp, string expected)
    {
        var result = Execute(resp, ResultProcessor.RedisKey);
        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // array
    public void FailingRedisKey(string resp) => ExecuteUnexpected(resp, ResultProcessor.RedisKey);

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("+world\r\n", "world")]
    [InlineData(":42\r\n", "42")]
    [InlineData("$-1\r\n", "")]
    [InlineData("_\r\n", "")]
    [InlineData(",3.14\r\n", "3.14")]
    [InlineData(ATTRIB_FOO_BAR + "$3\r\nfoo\r\n", "foo")]
    public void RedisValue(string resp, string expected)
    {
        var result = Execute(resp, ResultProcessor.RedisValue);
        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // array
    public void FailingRedisValue(string resp) => ExecuteUnexpected(resp, ResultProcessor.RedisValue);

    [Theory]
    [InlineData("$5\r\nhello\r\n", "hello")]
    [InlineData("+world\r\n", "world")]
    [InlineData("$-1\r\n", null)]
    [InlineData("_\r\n", null)]
    [InlineData(ATTRIB_FOO_BAR + "$10\r\ntiebreaker\r\n", "tiebreaker")]
    public void TieBreaker(string resp, string? expected) => Assert.Equal(expected, Execute(resp, ResultProcessor.TieBreaker));

    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n")] // array
    public void FailingTieBreaker(string resp) => ExecuteUnexpected(resp, ResultProcessor.TieBreaker);

    [return: NotNullIfNotNull(nameof(array))]
    protected static string? Join<T>(T[]? array, string separator = ",")
    {
        if (array is null) return null;
        return string.Join(separator, array);
    }

    public void Log(string message) => log?.WriteLine(message);

    private protected static Message DummyMessage<T>()
        => Message.Create(0, default, RedisCommand.UNKNOWN);

    private protected void ExecuteUnexpected<T>(
        string resp,
        ResultProcessor<T> processor,
        ConnectionType connectionType = ConnectionType.Interactive,
        RedisProtocol protocol = RedisProtocol.Resp2,
        [CallerMemberName] string caller = "")
    {
        Assert.False(TryExecute(resp, processor, out _, out var ex));
        if (ex is not null) Log(ex.Message);
        Assert.StartsWith("Unexpected response to UNKNOWN:", Assert.IsType<RedisConnectionException>(ex).Message);
    }
    private protected static T? Execute<T>(
        string resp,
        ResultProcessor<T> processor,
        ConnectionType connectionType = ConnectionType.Interactive,
        RedisProtocol protocol = RedisProtocol.Resp2,
        [CallerMemberName] string caller = "")
    {
        Assert.True(TryExecute<T>(resp, processor, out var value, out var ex));
        Assert.Null(ex);
        return value;
    }

    private protected static bool TryExecute<T>(
        string resp,
        ResultProcessor<T> processor,
        out T? value,
        out Exception? exception,
        ConnectionType connectionType = ConnectionType.Interactive,
        RedisProtocol protocol = RedisProtocol.Resp2,
        [CallerMemberName] string caller = "")
    {
        byte[]? lease = null;
        try
        {
            var maxLen = Encoding.UTF8.GetMaxByteCount(resp.Length);
            const int MAX_STACK = 128;
            Span<byte> oversized = maxLen <= MAX_STACK
                ? stackalloc byte[MAX_STACK]
                : (lease = ArrayPool<byte>.Shared.Rent(maxLen));

            var msg = DummyMessage<T>();
            var box = SimpleResultBox<T>.Get();
            msg.SetSource(processor, box);

            var reader = new RespReader(oversized.Slice(0, Encoding.UTF8.GetBytes(resp, oversized)));
            PhysicalConnection connection = new(connectionType, protocol, caller);
            Assert.True(processor.SetResult(connection, msg, ref reader));
            value = box.GetResult(out exception, canRecycle: true);
            return exception is null;
        }
        finally
        {
            if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
        }
    }
}
