; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ZAC001 | ZeroAlloc.Collections.Generators | Warning | UndisposedPooledCollectionAnalyzer
ZAC010 | ZeroAlloc.Collections.Generators | Warning | ZeroAllocEnumerableGenerator — ambiguous array field
ZAC011 | ZeroAlloc.Collections.Generators | Warning | ZeroAllocEnumerableGenerator — ambiguous count field
ZAC012 | ZeroAlloc.Collections.Generators | Error | ZeroAllocEnumerableGenerator — field not found
