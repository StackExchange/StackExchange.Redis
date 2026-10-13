using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using StackExchange.Redis.Protocol;
using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

/// <summary>
/// Base class for ResultProcessor unit tests.
/// Tests are organized into subclass files by category.
/// </summary>
public abstract class ResultProcessorUnitTest(ITestOutputHelper log)
{
    private protected const string ATTRIB_FOO_BAR = "|1\r\n+foo\r\n+bar\r\n";

    [return: NotNullIfNotNull(nameof(array))]
    protected static string? Join<T>(T[]? array, string separator = ",")
    {
        if (array is null) return null;
        return string.Join(separator, array);
    }

    public void Log(string message) => log?.WriteLine(message);

    /// <summary>Parse a reply that the handler should reject.</summary>
    /// <remarks>
    /// The shipped processors reported a shape they did not expect as a <see cref="RedisConnectionException"/>
    /// ("Unexpected response to ..."); a handler simply throws, and the operation turns that into the
    /// caller's fault. What is pinned here is that it does not quietly produce a value.
    /// </remarks>
    private protected Exception ExecuteUnexpected<T>(
        string resp,
        IRespHandler<T> handler,
        [CallerMemberName] string caller = "")
    {
        Assert.False(TryExecute(resp, handler, out _, out var ex), caller);
        Assert.NotNull(ex);
        Log(ex.Message);
        return ex;
    }

    private protected static T Execute<T>(string resp, IRespHandler<T> handler, [CallerMemberName] string caller = "")
    {
        var ok = TryExecute(resp, handler, out var value, out var ex);
        Assert.True(ok, $"{caller}: {ex?.GetType().Name}: {ex?.Message}");
        return value!;
    }

    /// <summary>Parse <paramref name="resp"/> exactly as the core does - through <c>RespExecutor.ParseFromSpan</c>.</summary>
    private protected static bool TryExecute<T>(string resp, IRespHandler<T> handler, out T? value, out Exception? exception)
    {
        try
        {
            value = RespExecutor.ParseFromSpan(handler, Encoding.UTF8.GetBytes(resp));
            exception = null;
            return true;
        }
        catch (Exception ex)
        {
            value = default;
            exception = ex;
            return false;
        }
    }
}
