; Shipped analyzer releases; see AnalyzerReleases.Unshipped.md for the convention.
; Recorded as shipped from the release that first carries the analyzer, rather than being staged in Unshipped
; first: these IDs go out with 3.1 as the initial set, so there is no window in which they are unshipped, and
; nothing is gained by tracking them in two places on the way. Later additions do go through Unshipped.

## Release 3.1

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SER300  | Usage    | Warning  | TransactionAnalyzer: transaction may be replaceable by a conditional argument (any server)
SER301  | Usage    | Warning  | TransactionAnalyzer: transaction may be replaceable by a single atomic operation (newer server)
SER302  | Usage    | Warning  | TransactionAnalyzer: condition may be redundant; the queued command already reports whether it acted
SER303  | Usage    | Warning  | TransactionAnalyzer: two queued operations may be a single compound command
SER304  | Usage    | Warning  | TransactionAnalyzer: repeated queued operations may suit the variadic overload
SER350  | Build    | Warning  | AsciiHashGenerator: generated code requires a newer C# language version, so nothing was generated
