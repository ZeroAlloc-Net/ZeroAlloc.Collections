; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### Changed Rules

Rule ID | New Category                     | New Severity | Old Category                     | Old Severity | Notes
--------|----------------------------------|--------------|----------------------------------|--------------|----------------------------------------------------------
ZAC013  | ZeroAlloc.Collections.Generators | Error        | ZeroAlloc.Collections.Generators | Warning      | Nested type inside a containing type that is not partial
