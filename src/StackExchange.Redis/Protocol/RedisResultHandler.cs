using RESPite.Messages;

namespace StackExchange.Redis.Protocol;

/// <summary>Reads any reply into the shipped <see cref="RedisResult"/> tree.</summary>
/// <remarks>
/// <para>
/// <b>The bridge from the context surface to the older result type</b>, for the <c>IDatabase</c> members
/// that promise one - <c>ScriptEvaluate</c> and the ad-hoc <c>Execute</c> family. It is not offered on the
/// new surface: <see cref="RedisResult"/> materialises the whole reply into a tree of objects and arrays,
/// which is precisely what <see cref="RespResult"/> exists to avoid.
/// </para>
/// <para>
/// <b>The parse is the shipped one</b>, reused rather than rewritten, so the two paths cannot disagree
/// about how a reply becomes a result. <c>RedisResult.TryCreate</c> takes a <c>PhysicalConnection</c>,
/// which turns out to be vestigial - it is only threaded through the recursion as state and never read,
/// and it is already nullable - so a handler with no connection to offer can pass null.
/// </para>
/// </remarks>
internal sealed class RedisResultHandler : IRespHandler<RedisResult>
{
    internal static readonly RedisResultHandler Instance = new();

    private RedisResultHandler() { }

    public RedisResult Parse(ref RespReader reader)
        => RedisResult.TryCreate(null, ref reader, out var value)
            ? value
            : throw new System.InvalidOperationException("Unable to read the reply as a RedisResult.");
}
