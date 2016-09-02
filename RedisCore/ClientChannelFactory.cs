using Channels;
using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;

namespace RedisCore
{
    public abstract class ClientChannelFactory : IDisposable
    {
        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing) { }
        protected IPEndPoint ParseIPEndPoint(string location)
        {
            // todo: lots of love
            int i = location.IndexOf(':');
            var ip = IPAddress.Parse(location.Substring(0, i));
            var port = int.Parse(location.Substring(i + 1), CultureInfo.InvariantCulture);
            return new IPEndPoint(ip, port);
        }
        public abstract Task<IClientChannel> ConnectAsync(string location);
    }
    public interface IClientChannel : IDisposable
    {
        // TODO: provide a nul input/output for use with one-way transports
        IReadableChannel Input { get; }
        IWritableChannel Output { get; }
    }    
}
