using System;

namespace StackExchange.Redis.Transports
{
    internal interface IAllocator<T>
    {
        Memory<T> Allocate(int count);
    }
}
