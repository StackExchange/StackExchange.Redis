namespace StackExchange.Redis
{
    internal enum CommandResult
    {
        ConnectionFailed,
        InternalFailure,
        MultiplexerDisposed,
        NetworkWriteFailure,
        NoConnectionAvailable,
        NotQueued,
        ProtocolFailure,
        Success,
        TimeoutAwaitingResponse,
        TimeoutBeforeConnectionAvailable,
        TimeoutBeforeWrite,
        TransactionFailed,
    }
}
