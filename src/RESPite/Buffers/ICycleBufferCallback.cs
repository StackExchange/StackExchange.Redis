using System.Diagnostics.CodeAnalysis;

namespace RESPite.Buffers;

[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public interface ICycleBufferCallback
{
    /// <summary>
    /// Notify that a page is available; this means that a consumer that wants
    /// unflushed data can activate when pages are rotated, allowing large
    /// payloads to be written concurrent with write.
    /// </summary>
    void PageComplete();
}
