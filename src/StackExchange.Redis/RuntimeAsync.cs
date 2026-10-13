#if NET11_0_OR_GREATER
// DISCOVERY ONLY - never present in a shipped package; see IncludePreviewTargets in the csproj.
//
// Opts the whole assembly into .NET 11 runtime-native async, which replaces the compiler's async state
// machines with runtime-native suspension. TWO knobs are needed and neither works alone: this attribute,
// and <Features>runtime-async=on</Features>. Verified on CLEAN builds, because an incremental one will
// report success from a stale obj/ and did exactly that while this was being worked out.
//
// Assembly-wide is the only granularity offered, so this changes codegen for EVERY async method here at
// once; the compiler reports ERR_UnsupportedFeatureInRuntimeAsync for anything it cannot convert, and
// such a method opts out individually with [RuntimeAsyncMethodGeneration(false)].
//
// Measured on the shape that matters - a count command, ValueTask<long>, nothing materialised - the
// synchronously-completing path went 40.4ns -> 19.6ns, zero allocation either way, which is the case a
// cache hit and a buffered pipelined reply both take. The suspending path measured WORSE in a synthetic
// harness (160B -> 198B, 74ns -> 103ns), but that harness resumed inline from OnCompleted rather than
// from real I/O, so it is not yet evidence of anything. See the queue.
[assembly: System.Runtime.CompilerServices.RuntimeAsyncMethodGeneration(true)]

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Declared here because it is absent from the .NET 11 RC1 <b>reference</b> pack, though present in
    /// CoreLib - the same arrangement <c>IsExternalInit</c> has always needed.
    /// </summary>
    /// <param name="runtimeAsync">Whether methods in this scope should use runtime-native async.</param>
    [AttributeUsage(
        AttributeTargets.Assembly | AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Method,
        AllowMultiple = false,
        Inherited = false)]
    internal sealed class RuntimeAsyncMethodGenerationAttribute(bool runtimeAsync) : Attribute
    {
        /// <summary>Whether runtime-native async is requested.</summary>
        public bool RuntimeAsync { get; } = runtimeAsync;
    }
}
#endif
