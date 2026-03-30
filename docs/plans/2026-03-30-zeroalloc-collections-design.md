# ZeroAlloc.Collections Design

## Overview

A high-performance, zero/minimal-allocation collections library for .NET. Provides specialized collection types not in the BCL, optimized for hot-path game/real-time systems, general-purpose development, and high-throughput server scenarios.

## Targets

- `netstandard2.1` (Span available everywhere)
- `net8.0` (LTS, full ref struct support)
- `net9.0` (`allows ref struct` generic constraint)

## Package & Project Structure

Single NuGet package: `ZeroAlloc.Collections` — source generators bundled internally.

Two `.csproj` files in `src/`:

- `ZeroAlloc.Collections/` — core collection types (`netstandard2.1;net8.0;net9.0`), packs the NuGet
- `ZeroAlloc.Collections.Generators/` — source generators (`netstandard2.0`, Roslyn requirement), packed into `analyzers/dotnet/cs/` in the NuGet

```
ZeroAlloc.Collections/
├── .github/workflows/
├── .config/
├── assets/icon.png
├── docs/plans/
├── src/
│   ├── ZeroAlloc.Collections/
│   └── ZeroAlloc.Collections.Generators/
├── tests/
│   ├── ZeroAlloc.Collections.Tests/
│   └── ZeroAlloc.Collections.Benchmarks/
├── Directory.Build.props
├── ZeroAlloc.Collections.slnx
├── .commitlintrc.yml
├── GitVersion.yml
├── renovate.json
├── LICENSE (MIT)
└── README.md
```

Repo conventions match the ZeroAlloc ecosystem: `.slnx`, `Directory.Build.props`, `commitlint`, `release-please`, `renovate`, `Meziantou.Analyzer`, `ErrorProne.NET.Structs`, GitHub workflows.

## Collection Variants

Each collection ships in two variants:

- **`ref struct`** — zero heap allocation, stack-only, compile-time lifetime enforcement. Cannot implement interfaces.
- **`Heap*` class** — implements `IDisposable`, `IList<T>` / `IReadOnlyList<T>` etc. Usable in async code and as fields.

Naming: `PooledList<T>` (ref struct) / `HeapPooledList<T>` (class).

## Pool Strategy

`ArrayPool<T>.Shared` by default, with an overload to pass a custom `ArrayPool<T>` instance. Zero-config for most users, tunable for power users.

## Collection Types

### PooledList\<T\> / HeapPooledList\<T\>

- Backed by `ArrayPool<T>.Shared` (overload for custom pool)
- Grows like `List<T>` (double capacity)
- Returns buffer on `Dispose()`
- `Span<T>` / `ReadOnlySpan<T>` accessors
- `ref T this[int index]` indexer on ref struct variant

### RingBuffer\<T\> / HeapRingBuffer\<T\>

- Fixed-capacity circular buffer
- `TryWrite(T)` / `TryRead(out T)` — no exceptions
- `IsFull` / `IsEmpty` / `Count`
- Span-based bulk read/write
- Backed by `ArrayPool` (pooled) or caller-provided array

### SpanDictionary\<TKey, TValue\> / HeapSpanDictionary\<TKey, TValue\>

- Open-addressing hash map (no `Node` allocations like `Dictionary<,>`)
- `Span`-friendly lookup
- Pooled backing array
- `ref TValue` value access on ref struct variant

### PooledStack\<T\> / HeapPooledStack\<T\>

- Pooled-backed LIFO stack
- `TryPop(out T)` / `TryPeek(out T)`
- Returns buffer on `Dispose()`

### PooledQueue\<T\> / HeapPooledQueue\<T\>

- Pooled-backed FIFO queue (circular array internally)
- `TryDequeue(out T)` / `TryPeek(out T)`
- Returns buffer on `Dispose()`

### FixedSizeList\<T\> / HeapFixedSizeList\<T\>

- Compile-time capacity via const generic or source-generated
- Stack-allocated in ref struct variant (`Span<T>` over `stackalloc` or inline buffer)
- Throws on overflow (or `TryAdd` pattern)

### Shared Patterns

- Zero-alloc `GetEnumerator()` returning a `ref struct` enumerator
- `ToArray()`, `AsSpan()`, `AsReadOnlySpan()`
- `Clear()` resets without returning buffers, `Dispose()` returns them
- TFM-conditional `allows ref struct` on `net9.0`

## Source Generators

### 1. Specialized Type-Specific Collections

Generates concrete implementations optimized for a specific `T`, eliminating interface dispatch and enabling inlining.

```csharp
[ZeroAllocList<int>]
public partial struct IntList;
```

### 2. Strongly-Typed Pooled Wrappers

Generates wrappers with automatic return-to-pool logic and domain-specific naming.

```csharp
[PooledCollection<Order>]
public partial struct OrderBuffer;
```

### 3. Custom Zero-Alloc Enumerators

Generates `ref struct` enumerators for any type that exposes a `Span<T>` or backing array.

```csharp
[ZeroAllocEnumerable]
public partial struct MyCollection<T>
{
    private T[] _items;
    private int _count;
}
```

### Generator Implementation Details

- Incremental generators (`IIncrementalGenerator`) for fast IDE experience
- Emits `#nullable enable` and `[MethodImpl(AggressiveInlining)]` where appropriate
- TFM-aware: emits `allows ref struct` constraints on `net9.0`, omits on lower targets
- Diagnostic analyzers bundled alongside: warn on undisposed pooled collections, warn on copying ref struct variants

## Testing & Benchmarks

### Unit Tests (ZeroAlloc.Collections.Tests)

- xUnit
- Per-collection test classes
- Test categories: functional, lifecycle, edge cases, enumerator, source generators
- Source generator tests use `Microsoft.CodeAnalysis.Testing`
- Multi-TFM test runs

### Benchmarks (ZeroAlloc.Collections.Benchmarks)

- BenchmarkDotNet
- Compare against BCL equivalents (`List<T>`, `Dictionary<,>`, `Queue<T>`, `Stack<T>`)
- Metrics: allocation (bytes), throughput (ops/sec), GC collections
- Scenarios: add N items, enumerate, lookup, mixed read/write
