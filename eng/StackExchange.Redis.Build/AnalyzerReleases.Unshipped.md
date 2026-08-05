; Unshipped analyzer release
; Tracks the diagnostics reported by the analyzers/generators shipped inside the StackExchange.Redis package.
; This is the analyzer equivalent of PublicAPI.Unshipped.txt: a diagnostic ID is a public contract once
; released, because consumers put them in NoWarn and .editorconfig. See Diagnostics.cs for the SER3xx map.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SER300  | Usage    | Info     | TransactionAnalyzer: transaction can be replaced by a conditional argument (any server)
SER301  | Usage    | Info     | TransactionAnalyzer: transaction can be replaced by a single atomic operation (newer server)
SER350  | Build    | Warning  | AsciiHashGenerator: generated code requires a newer C# language version, so nothing was generated
