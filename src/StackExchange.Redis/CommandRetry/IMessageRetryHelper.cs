using System;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    internal interface IMessageRetryHelper
    {
        RedisTimeoutException GetTimeoutException(Message message);
        bool HasTimedOut(Message message);
        bool IsEndpointAvailable(Message message);
        void SetExceptionAndComplete(Message message, Exception ex = null);
        Task<bool> TryResendAsync(Message message);
    }
}