#if !NET
// ReSharper disable once CheckNamespace
namespace System.Buffers
{
    internal static class FrameworkSequenceShims
    {
        extension<T>(scoped in ReadOnlySequence<T> sequence)
        {
            /// <summary>
            /// Gets the first segment of the sequence as a span. On modern runtimes this is a BCL instance
            /// property; this shim supplies it for older targets.
            /// </summary>
            public ReadOnlySpan<T> FirstSpan => sequence.First.Span;
        }
    }
}
#endif
