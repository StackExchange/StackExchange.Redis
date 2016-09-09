using Channels;

namespace RedisCore
{
    public interface IMessage
    {
        int MinimumSize { get; }
        void Write(ref WritableBuffer output);
    }
}
