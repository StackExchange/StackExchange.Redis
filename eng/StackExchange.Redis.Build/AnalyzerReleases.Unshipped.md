; Unshipped analyzer release
; Tracks the diagnostics reported by the analyzers/generators shipped inside the StackExchange.Redis package.
; This is the analyzer equivalent of PublicAPI.Unshipped.txt: a diagnostic ID is a public contract once
; released, because consumers put them in NoWarn and .editorconfig. See Diagnostics.cs for the SER3xx map.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; Empty: the initial set is recorded directly in AnalyzerReleases.Shipped.md under 3.1. New rules added after
; that release go here first, under a "### New Rules" table, and move across when they ship.
