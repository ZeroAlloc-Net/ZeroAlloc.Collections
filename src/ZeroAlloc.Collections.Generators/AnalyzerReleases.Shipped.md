; Shipped analyzer releases.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.1

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------------------------------------
ZAC001  | Usage    | Warning  | UndisposedPooledCollectionAnalyzer

## Release 0.1.3

### New Rules

Rule ID | Category                         | Severity | Notes
--------|----------------------------------|----------|------------------------------------------------------
ZAC010  | ZeroAlloc.Collections.Generators | Warning  | ZeroAllocEnumerableGenerator — ambiguous array field
ZAC011  | ZeroAlloc.Collections.Generators | Warning  | ZeroAllocEnumerableGenerator — ambiguous count field
ZAC012  | ZeroAlloc.Collections.Generators | Error    | ZeroAllocEnumerableGenerator — field not found

## Release 1.1.4

### Changed Rules

Rule ID | New Category                     | New Severity | Old Category | Old Severity | Notes
--------|----------------------------------|--------------|--------------|--------------|------------------------------------
ZAC001  | ZeroAlloc.Collections.Generators | Warning      | Usage        | Warning      | UndisposedPooledCollectionAnalyzer
