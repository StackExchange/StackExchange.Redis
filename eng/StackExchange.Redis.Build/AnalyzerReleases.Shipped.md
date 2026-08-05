; Shipped analyzer releases; see AnalyzerReleases.Unshipped.md for the convention.
; Recorded as shipped from the release that first carries the analyzer, rather than being staged in Unshipped
; first: these IDs go out with 3.1 as the initial set, so there is no window in which they are unshipped, and
; nothing is gained by tracking them in two places on the way. Later additions do go through Unshipped.

## Release 3.1

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SER300  | Usage    | Info     | TransactionAnalyzer: transaction can be replaced by a conditional argument (any server)
SER301  | Usage    | Info     | TransactionAnalyzer: transaction can be replaced by a single atomic operation (newer server)
SER302  | Usage    | Info     | TransactionAnalyzer: condition is redundant; the queued command already reports whether it acted
SER303  | Usage    | Info     | TransactionAnalyzer: two queued operations are a single compound command
SER304  | Usage    | Info     | TransactionAnalyzer: repeated queued operations can use the variadic overload
SER350  | Build    | Warning  | AsciiHashGenerator: generated code requires a newer C# language version, so nothing was generated
