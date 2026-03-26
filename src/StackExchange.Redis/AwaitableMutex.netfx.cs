#if !NET
namespace StackExchange.Redis;

// compensating for the fact that netfx SemaphoreSlim is kinda janky
// (https://blog.marcgravell.com/2019/02/fun-with-spiral-of-death.html)
internal partial struct AwaitableMutex
{
}
#endif
