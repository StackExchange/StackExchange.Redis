namespace RESPite.Messages;

public interface IRespFormatter<TRequest>
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
{
    void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in TRequest request);
}

/*
public interface IRespSizeEstimator<TRequest> : IRespFormatter<TRequest>
#if NET9_0_OR_GREATER
    where TRequest : allows ref struct
#endif
{
    int EstimateSize(scoped ReadOnlySpan<byte> command, in TRequest request);
}
*/
