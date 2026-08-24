; Unshipped analyzer release
; Tracks the diagnostics reported by the analyzers/generators shipped inside the StackExchange.Redis package.
; This is the analyzer equivalent of PublicAPI.Unshipped.txt: a diagnostic ID is a public contract once
; released, because consumers put them in NoWarn and .editorconfig. See Diagnostics.cs for the SER3xx map.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; The initial set is recorded directly in AnalyzerReleases.Shipped.md under 3.1. New rules added after that
; release go here first, and move across when they ship.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SER305  | Usage    | Error    | QueuedResultAnalyzer: waiting for a command queued on a transaction or batch, before Execute[Async]() sends it, never completes
SER306  | Usage    | Warning  | QueuedResultAnalyzer: waiting for a fire-and-forget result reads the default value rather than the server's answer
SER307  | Usage    | Warning  | QueuedResultAnalyzer: blocking on a redis call instead of awaiting it, which ties up a thread-pool thread while the reply needs one of its own
SER308  | Usage    | Warning  | QueuedResultAnalyzer: calling the library's own Wait/WaitAll/TryWait helpers, which block the calling thread
