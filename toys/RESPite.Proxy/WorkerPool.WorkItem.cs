using System.Net.Sockets;

namespace RESPite.Proxy;

internal partial class WorkerPool
{
    private partial struct WorkItem
    {
        public partial void Execute()
        {
            switch (step)
            {
                case WorkerStep.None:
                    GC.KeepAlive(target);
                    break;
                case WorkerStep.InitClient:
                    ((SocketServer)target).InitClient((Socket)arg!);
                    break;
                case WorkerStep.SocketPumpAwait:
                    ((WorkerSocketAsyncEventArgs)target).PumpAwait((Action<object?>)arg!);
                    break;
                case WorkerStep.SocketProxyClientWrite:
                    ((SocketProxyClient)target).WorkerWrite();
                    break;
                case WorkerStep.SocketProxyClientWriteCallback:
                    ((SocketProxyClient)target).WorkerWriteCallback();
                    break;
                case WorkerStep.SocketProxyClientRead:
                    ((SocketProxyClient)target).WorkerRead();
                    break;
                case WorkerStep.SocketProxyClientReadCallback:
                    ((SocketProxyClient)target).WorkerReadCallback();
                    break;
                default:
                    Throw();
                    break;
            }
        }

        private void Throw() => throw new NotImplementedException($"No implementation for {step}");
    }
}
