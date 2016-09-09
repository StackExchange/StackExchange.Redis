using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Channels;

namespace RedisCore
{
    public class SocketClientChannelFactory : ClientChannelFactory
    {
        public override string ToString() => typeof(Socket).FullName;
        private ChannelFactory channelFactory = new ChannelFactory();
        protected override void Dispose(bool disposing)
        {
            if (disposing) channelFactory?.Dispose();
        }
        class SocketClientChannel : IClientChannel
        {
            private Socket socket;
            private NetworkStream ns;
            private IReadableChannel input;
            private IWritableChannel output;
            public SocketClientChannel(ChannelFactory channelFactory, Socket socket)
            {
                this.socket = socket;
                this.ns = new NetworkStream(socket);
                this.input = channelFactory.MakeReadableChannel(ns);
                this.output = channelFactory.MakeWriteableChannel(ns);
            }

            IReadableChannel IClientChannel.Input => input;

            IWritableChannel IClientChannel.Output => output;

            void IDisposable.Dispose()
            {
                input?.CompleteReading();
                input = null;
                output?.CompleteWriting();
                output = null;
                ns?.Dispose();
                ns = null;
                socket?.Dispose();
                socket = null;
            }
        }
        class ConnectState
        {
            public readonly TaskCompletionSource<IClientChannel> TaskSource;
            public readonly ChannelFactory ChannelFactory;
            public ConnectState(TaskCompletionSource<IClientChannel> taskSource, ChannelFactory channelFactory)
            {
                this.TaskSource = taskSource;
                this.ChannelFactory = channelFactory;
            }
        }
        static readonly EventHandler<SocketAsyncEventArgs> onConnect = (sender, args) =>
        {
            var state = (ConnectState)args.UserToken;
            var tcs = state.TaskSource;
            try
            {
                if (args.SocketError != SocketError.Success)
                {
                    tcs.TrySetException(new SocketException((int)args.SocketError));
                    return;
                }
                var socket = args.ConnectSocket;
                socket.NoDelay = true;
                var channel = new SocketClientChannel(state.ChannelFactory, socket);
                tcs.SetResult(channel);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        };
        public override Task<IClientChannel> ConnectAsync(string location)
        {
            var tcs = new TaskCompletionSource<IClientChannel>();
            var ep = ParseIPEndPoint(location);
            var args = new SocketAsyncEventArgs();
            args.UserToken = new ConnectState(tcs, channelFactory);
            args.RemoteEndPoint = ep;
            args.Completed += onConnect;
            if (!Socket.ConnectAsync(SocketType.Stream, ProtocolType.Tcp, args)) onConnect(typeof(Socket), args);
            return tcs.Task;
        }
    }
}
