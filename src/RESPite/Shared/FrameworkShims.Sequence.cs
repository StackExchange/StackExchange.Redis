#if !NET
// ReSharper disable once CheckNamespace
namespace System.Buffers
{
    internal static class FrameworkSequenceShims
    {
        // by-value receiver (not 'in'): the returned span references the underlying heap segment, not the
        // sequence struct, so it must not be scoped to the receiver - matching the BCL FirstSpan semantics
        extension<T>(ReadOnlySequence<T> sequence)
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
