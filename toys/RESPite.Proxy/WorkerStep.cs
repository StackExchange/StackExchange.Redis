namespace RESPite.Proxy;

internal enum WorkerStep
{
    None,
    InitClient,
    SocketPumpAwait,
    SocketProxyClientWrite,
    SocketProxyClientWriteCallback,
}
