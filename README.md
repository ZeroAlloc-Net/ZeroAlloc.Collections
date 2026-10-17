# ZeroAlloc.Collections

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Collections.svg)](https://www.nuget.org/packages/ZeroAlloc.Collections)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Collections/graph/badge.svg)](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Collections)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Pooled and stack-only counterparts to `List<T>`, `Dictionary<TKey,TValue>`, `Stack<T>` and `Queue<T>`
whose `ref struct` forms add, look up and enumerate with 0 B allocated, NativeAOT-ready.

## Why ZeroAlloc.Collections

- **0 B on the hot path, asserted in CI.** The `ref struct` collections add, look up and enumerate with
  0 B allocated per call; `AllocationTests` fails CI if any of them allocates.
- **Faster than the BCL as well.** Adding 1,000 items to a `PooledList<int>` takes 0.63× the time of
  `List<int>`, which allocates 8,424 B in the same run. Stacks, queues and dictionaries take 0.6–0.8×,
  and a `RingBuffer<int>` takes 0.47× a pre-sized `Queue<int>`.
- **Works under NativeAOT and trimming.** No reflection and no runtime code generation; every PR
  publishes and runs an AOT sample.
- **Compile-time diagnostics instead of runtime surprises.** `ZAC001` warns when a pooled collection is
  never disposed, so a buffer that would never return to the pool is a build warning.

## Install

```bash
dotnet add package ZeroAlloc.Collections
```

## Quick start

```csharp
using ZeroAlloc.Collections;

string[] order = ["apple", "pear", "apple", "kiwi"];

// Rents its entries from ArrayPool<T>.Shared and returns them on Dispose.
using var prices = new SpanDictionary<string, decimal>(capacity: 16);
prices.Add("apple", 0.50m);
prices.Add("pear", 0.75m);

// Lives on the stack: no heap allocation at all.
var basket = new FixedSizeList<decimal>(stackalloc decimal[8]);
foreach (var item in order)
    if (prices.TryGetValue(item, out var price)) basket.Add(price);

decimal total = 0;
foreach (var p in basket) total += p;
Console.WriteLine($"{basket.Count} items, {total}"); // 3 items, 1.75
```

## What you get

| Type | Ref Struct | Heap Variant | Description |
|------|-----------|--------------|-------------|
| `PooledList<T>` | Yes | `HeapPooledList<T>` | Pooled-backed growable list |
| `RingBuffer<T>` | Yes | `HeapRingBuffer<T>` | Fixed-capacity circular buffer |
| `SpanDictionary<TKey,TValue>` | Yes | `HeapSpanDictionary<TKey,TValue>` | Open-addressing hash map |
| `PooledStack<T>` | Yes | `HeapPooledStack<T>` | Pooled-backed LIFO stack |
| `PooledQueue<T>` | Yes | `HeapPooledQueue<T>` | Pooled-backed FIFO queue |
| `FixedSizeList<T>` | Yes | `HeapFixedSizeList<T>` | Stack-allocated fixed-capacity list |
| `ConcurrentHeapSpanDictionary<TKey,TValue>` | No | — | Thread-safe, coarse-locked, pooled hash map |

- **Two forms of each:** a `ref struct` for synchronous hot paths, and a `Heap*` class for fields,
  `async` code and APIs that take `IList<T>`, `IReadOnlyCollection<T>` or `IDictionary<TKey,TValue>`.
- **`Span<T>` access** to the list and stack contents for zero-copy loops.
- **Source generators** that emit type-specific collections from `[ZeroAllocList(typeof(T))]`.
  See [source generators](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/source-generators.md).
- Targets `netstandard2.1`, `net8.0` and `net9.0`.

## Performance

BenchmarkDotNet, N = 1,000. Time is the same-run ratio against the BCL type; allocations are exact.

| Workload | BCL type | ZeroAlloc time | BCL alloc | ZeroAlloc alloc |
|---|---|---:|---:|---:|
| Add N items | `List<int>` | 0.63× | 8,424 B | 0 B |
| Push and pop N items | `Stack<int>` | 0.62× | 8,424 B | 0 B |
| Enqueue and dequeue N items | `Queue<int>` | 0.79× | 8,432 B | 0 B |
| Write and read N items, capacity known | `Queue<int>` | 0.47× | 4,064 B | 0 B |

The `Heap*` classes allocate one enumerator per `foreach`. Every workload, with the full numbers: [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/performance.md).

## Learn more

- Documentation: [collections.zeroalloc.net](https://collections.zeroalloc.net), including
  [getting started](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/getting-started.md), the [cookbook](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/tree/main/docs/cookbook) and
  [diagnostics](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/diagnostics.md).
- Related packages: [ZeroAlloc.Inject](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject) and
  [ZeroAlloc.Analyzers](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers).
- Contributing: issues and pull requests are welcome on [GitHub](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/issues).
- [CHANGELOG](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/CHANGELOG.md).

## License

MIT. See [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/LICENSE).
