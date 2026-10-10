namespace StackExchange.Redis.Protocol;

/// <summary>A value written only when it is not null; omitted entirely otherwise.</summary>
/// <remarks>
/// <para>
/// <b>Omitted, not empty - and the difference is a real one.</b> <c>AppendFormatted(RedisValue)</c> writes
/// <c>$0</c> for a null, and an empty argument is not the same as no argument: for <c>XPENDING</c>'s
/// trailing consumer it means "the consumer whose name is the empty string" rather than "all consumers",
/// and for <c>SSCAN</c>'s <c>MATCH</c> it means "match the empty pattern" rather than "no filter".
/// </para>
/// <para>
/// Shared rather than copied per group, because it was written twice within a day and got it wrong the
/// first time in both places - a parity test caught one, a scan test the other.
/// </para>
/// </remarks>
internal readonly struct OptionalValue(RedisValue value) : IRespArgument
{
    public void WriteTo(scoped ref RespRequestBuilder handler)
    {
        if (!value.IsNull) handler.AppendFormatted(value);
    }
}
