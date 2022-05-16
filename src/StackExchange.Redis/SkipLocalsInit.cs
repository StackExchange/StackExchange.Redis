// turn off ".locals init"; this gives a small perf boost, but is particularly relevant when stackalloc is used
// side-effects: locals don't have defined zero values; normally this doesn't matter, due to "definite assignment",
// but it *can* be observed when using unsafe code, any "out" method that cheats, or "stackalloc" - the last is
// the most relevant to us, so we have audited that no "stackalloc" use expects the buffers to be zero'd initially
[module:System.Runtime.CompilerServices.SkipLocalsInit]

#if !NET5_0_OR_GREATER
// when not available, we can spoof it in a private type
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Event | AttributeTargets.Interface, Inherited = false)]
    internal sealed class SkipLocalsInitAttribute : Attribute {}
}
#endif
