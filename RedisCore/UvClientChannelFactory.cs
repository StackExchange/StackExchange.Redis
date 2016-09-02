using Channels;
using Channels.Networking.Libuv;
using System;
using System.Threading.Tasks;

namespace RedisCore
{
    public class UvClientChannelFactory : ClientChannelFactory
    {
        UvThread thread;
        readonly bool ownsThread;
        public override string ToString() => "libuv";
        public UvClientChannelFactory(UvThread thread = null)
        {
            if (thread == null)
            {
                thread = new UvThread();
                ownsThread = true;
            }
            this.thread = thread;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (ownsThread)
                {
                    thread?.Dispose();
                    thread = null;
                }
            }
        }
        private class UvClientChannel : IClientChannel
        {
            private UvTcpConnection connection;

            public UvClientChannel(UvTcpConnection connection)
            {
                this.connection = connection;
            }

            IReadableChannel IClientChannel.Input => connection.Input;

            IWritableChannel IClientChannel.Output => connection.Output;

            void IDisposable.Dispose()
            {
                connection?.Input?.CompleteReading();
                connection?.Output?.CompleteWriting();
                connection = null;
            }
        }
        public override async Task<IClientChannel> ConnectAsync(string location)
        {
            var endpoint = ParseIPEndPoint(location);
            var native = await new UvTcpClient(thread, endpoint).ConnectAsync();
            return new UvClientChannel(native);
        }
    }
}
