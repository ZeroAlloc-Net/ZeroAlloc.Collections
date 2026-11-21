---
id: performance
title: Performance
sidebar_position: 9
---

# Performance

ZeroAlloc.Collections is built for hot paths where collection overhead would otherwise be measurable. This page explains why the BCL collections allocate, how ZeroAlloc.Collections eliminates those allocations, and when zero-allocation actually matters in practice.

## Why BCL Collections Allocate

The standard .NET collection types perform heap operations on common code paths:

- **`List<T>`** allocates a new `T[]` and copies every time it grows. The old array becomes garbage.
- **`Dictionary<TKey, TValue>`** allocates `Entry` arrays and bucket arrays. Resizing copies everything into a new, larger set of arrays.
- **`Queue<T>` and `Stack<T>`** use the same grow-and-copy pattern as `List<T>`.
- **`IEnumerator<T>`** — the BCL collections return struct enumerators, but enumerating one through `IEnumerable<T>` boxes the enumerator onto the heap.

Each of these is a heap allocation. Under high throughput, GC pressure builds up. The minor GC pauses that result may be invisible at low load but become measurable — and sometimes dominant — in latency-sensitive workloads.

## How ZeroAlloc.Collections Eliminates Allocation

Four design decisions make zero or near-zero allocation possible.

### 1. ArrayPool Buffer Reuse

`PooledList<T>`, `PooledStack<T>`, `PooledQueue<T>`, `RingBuffer<T>`, and `SpanDictionary<TKey, TValue>` rent their backing arrays from `ArrayPool<T>.Shared` (or a custom pool). When you call `Dispose()`, the buffer is returned to the pool for reuse. No garbage is created.

```csharp
using var list = new PooledList<int>(capacity: 128);
for (int i = 0; i < 100; i++)
    list.Add(i);
// Dispose returns the buffer — no GC pressure
```

Growth still rents a new buffer and returns the old one, but both operations go through the pool. The only allocation is the initial `Rent` call, and even that is typically served from a cached buffer.

### 2. ref struct Lifetime Enforcement

The primary variants (`PooledList<T>`, `RingBuffer<T>`, etc.) are `ref struct` types. The compiler enforces that they cannot escape the stack: they cannot be boxed, stored in class fields, or captured by async state machines. This means the collection itself is never heap-allocated, and the compiler guarantees disposal at scope exit.

```csharp
// Compiler enforced: ref struct cannot escape
using var list = new PooledList<int>();
// Cannot assign list to a field, return it, or pass to async
```

### 3. Zero-Allocation Enumerators

Every `ref struct` collection's `GetEnumerator()` returns a `ref struct` enumerator. Because `ref struct` types cannot be boxed, `foreach` over a `ref struct` collection never allocates. The enumerator lives on the stack for the duration of the loop.

```csharp
using var list = new PooledList<int>();
list.Add(1);
list.Add(2);

// This foreach allocates 0 bytes — the enumerator is a ref struct on the stack
foreach (ref readonly var item in list)
{
    Console.WriteLine(item);
}
```

The `ref readonly` return also means no defensive copy for large value types.

The `Heap*` classes cannot use `ref struct` enumerators, because they implement `IEnumerable<T>`. Since 2.0.0 their `GetEnumerator()` returns a public struct `Enumerator` instead, as `List<T>` does, so a `foreach` over `HeapPooledList<T>`, `HeapPooledQueue<T>`, `HeapPooledStack<T>`, `HeapFixedSizeList<T>`, `HeapRingBuffer<T>`, `HeapSpanDictionary<TKey, TValue>` or `ConcurrentHeapSpanDictionary<TKey, TValue>` allocates nothing. The same holds for a `foreach` over `HeapSpanDictionary.Keys` and `.Values`, which return `KeyCollection` and `ValueCollection` with struct enumerators of their own. `ConcurrentHeapSpanDictionary` copies its snapshot into a buffer rented from `ArrayPool<T>.Shared` that the enumerator returns on `Dispose`; when the pool has no buffer of that size yet, as on the first enumeration, the rent allocates one, and later enumerations reuse it. Enumerating any of them through `IEnumerable<T>`, for example with LINQ, boxes the enumerator. In 1.x each `foreach` allocated a 32 to 56 B enumerator object.

Reading `HeapSpanDictionary.Keys` or `.Values`, and calling `Count` or `Contains` on them, allocates nothing: the views are created once and cached.

### 4. Open-Addressing Hash Map

`SpanDictionary<TKey, TValue>` uses open addressing (linear probing) instead of separate chaining. This means there are no `Node` objects, no linked lists, and no per-entry heap allocations. All entries live in a single flat array rented from the pool.

## Benchmark Results

Measured 2026-10-17 with BenchmarkDotNet v0.15.8 on an otherwise idle machine. Full benchmark
source is in `tests/ZeroAlloc.Collections.Benchmarks/`.

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host] : .NET 9.0.21 (9.0.21, 9.0.2126.50404), X64 RyuJIT x86-64-v3
```

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

> The concurrent fill, fixed-capacity and `Heap*` `foreach` tables were measured again for 2.0.0,
> on .NET 10 on a GitHub-hosted runner, as each of them says. The other tables are from the
> 2026-10-17 run described here, on .NET 9.

Each benchmark ran out of process in three launches of 6 warmup and 15 measured iterations, pinned
to two performance cores. `Ratio` is each row's mean time relative to the BCL baseline in the same
run; `Launch range` is the lowest and highest ratio of the three launches. To reproduce:

```bash
dotnet run --project tests/ZeroAlloc.Collections.Benchmarks -c Release --   --filter "*" --launchCount 3 --warmupCount 6 --iterationCount 15
```

The benchmark classes carry `[ShortRunJob]` so the manual CI run stays short; the counts on the
command line override it.

### Add N items

| N | `List<int>` | `PooledList<int>` | Launch range | BCL alloc | ZA alloc |
|------:|---:|---:|---:|---:|---:|
| 100 | 1.00 | 0.50 | 0.47–0.56 | 1,184 B | 0 B |
| 1,000 | 1.00 | 0.63 | 0.63–0.64 | 8,424 B | 0 B |
| 10,000 | 1.00 | 0.58 | 0.57–0.58 | 131,400 B | 0 B |

### Enumerate N items (foreach)

Each row builds a pre-sized list of N items, then enumerates it with `foreach`. Ratios against the
`List<int>` row in the same run.

| N | `List<int>` | `PooledList<int>` | BCL alloc | ZA alloc |
|------:|---:|---:|---:|---:|
| 100 | 1.00 | 1.11 | 456 B | 0 B |
| 1,000 | 1.00 | 1.41 | 4,056 B | 0 B |
| 10,000 | 1.00 | 1.39 | 40,056 B | 0 B |

> The BCL allocation is the list itself; `foreach` over a `List<int>` uses its struct enumerator and allocates nothing. `PooledList<int>` rents its buffer, so the whole build-and-enumerate allocates nothing, at some cost in speed.

### Push/pop N items

| N | `Stack<int>` | `PooledStack<int>` | Launch range | BCL alloc | ZA alloc |
|------:|---:|---:|---:|---:|---:|
| 100 | 1.00 | 0.60 | 0.58–0.63 | 1,184 B | 0 B |
| 1,000 | 1.00 | 0.62 | 0.62–0.63 | 8,424 B | 0 B |
| 10,000 | 1.00 | 0.60 | 0.59–0.63 | 131,400 B | 0 B |

### Enqueue/dequeue N items

| N | `Queue<int>` | `PooledQueue<int>` | Launch range | BCL alloc | ZA alloc |
|------:|---:|---:|---:|---:|---:|
| 100 | 1.00 | 0.51 | 0.50–0.53 | 1,192 B | 0 B |
| 1,000 | 1.00 | 0.79 | 0.78–0.81 | 8,432 B | 0 B |
| 10,000 | 1.00 | 0.70 | 0.69–0.73 | 131,408 B | 0 B |

### Add + lookup N entries

Ratios against `Dictionary<string,int>` add-then-lookup in the same run.

| N | `Dictionary<string,int>` | `SpanDictionary<string,int>` | BCL alloc | ZA alloc |
|------:|---:|---:|---:|---:|
| 100 | 1.00 | 0.64 | 7,392 B | 0 B |
| 1,000 | 1.00 | 0.60 | 73,168 B | 0 B |

> `SpanDictionary` allocates **0 bytes** because its entry array is rented from `ArrayPool<T>` and returned on `Dispose`. The open-addressing layout (no `Node` objects, no bucket chains) also eliminates the per-entry allocation overhead found in `Dictionary`.

### Concurrent fill, N entries

Measured 2026-11-21 on .NET 10.0.15 with the `ShortRun` job on a GitHub-hosted runner, AMD EPYC 9V45,
after the 2.0.0 changes; one launch, so no launch range.

| N | `ConcurrentDictionary` | `Dictionary` + `Lock` | `ConcurrentHeapSpanDictionary` | BCL alloc | `Dictionary` + `Lock` alloc | ZA alloc |
|------:|---:|---:|---:|---:|---:|---:|
| 100 | 1.00 | 0.63 | 0.61 | 12,744 B | 7,352 B | 96 B |
| 1,000 | 1.00 | 0.58 | 0.51 | 141,032 B | 73,128 B | 96 B |

> The 96 B is the `ConcurrentHeapSpanDictionary` object and its `System.Threading.Lock`; its storage is pooled, and the `foreach` over it reuses a pooled snapshot buffer, so it allocates nothing once the pool holds one.

### Write/read N items, capacity known up front

| N | `Queue<int>` | `RingBuffer<int>` | Launch range | `HeapRingBuffer<int>` | Launch range | BCL alloc | ZA alloc |
|------:|---:|---:|---:|---:|---:|---:|---:|
| 100 | 1.00 | 0.41 | 0.39–0.43 | 0.78 | 0.77–0.78 | 464 B | 0 B |
| 1,000 | 1.00 | 0.47 | 0.45–0.49 | 0.70 | 0.68–0.73 | 4,064 B | 0 B |

> `RingBuffer` is about twice as fast as a pre-sized `Queue<int>` in this single-threaded write-then-read loop, and allocates nothing.

### Add + enumerate N items, fixed capacity

Measured 2026-11-21 on .NET 10.0.15 with the `ShortRun` job on a GitHub-hosted runner, AMD EPYC 9V74,
after the 2.0.0 changes; one launch.

| N | `List<int>`, pre-sized | `FixedSizeList<int>` (stackalloc) | `HeapFixedSizeList<int>` | BCL alloc | `FixedSizeList` alloc | `HeapFixedSizeList` alloc |
|------:|---:|---:|---:|---:|---:|---:|
| 100 | 1.00 | 0.75 | 0.81 | 456 B | 0 B | 424 B |
| 1,000 | 1.00 | 0.75 | 0.93 | 4,056 B | 0 B | 4,024 B |

> `HeapFixedSizeList` allocates a plain `T[]`, not a pooled buffer; its `foreach` allocates nothing. In this run .NET 10 also kept the short-lived list object itself off the heap, so only the array shows.

### `foreach` over the `Heap*` classes, N = 100

Ratios against `foreach` over a `List<int>` in the same run. Measured 2026-11-21 on .NET 10.0.15 with
the `ShortRun` job on a GitHub-hosted runner, AMD EPYC 9V45, after the 2.0.0 changes; one launch. Short
runs on shared runners are noisy: in an earlier run of the same job, `HeapRingBuffer<int>` read 1.36
instead of 2.42. The allocations are exact.

| Collection | Ratio | Allocated |
|---|---:|---:|
| `List<int>` | 1.00 | 0 B |
| `HeapPooledList<int>` | 0.98 | 0 B |
| `HeapPooledQueue<int>` | 1.32 | 0 B |
| `HeapPooledStack<int>` | 1.11 | 0 B |
| `HeapRingBuffer<int>` | 2.42 | 0 B |
| `HeapFixedSizeList<int>` | 0.98 | 0 B |
| `HeapSpanDictionary<int, int>` | 4.84 | 0 B |
| `HeapSpanDictionary<int, int>.Keys` | 6.27 | 0 B |
| `HeapSpanDictionary<int, int>.Keys.Count` | 0.02 | 0 B |
| `ConcurrentHeapSpanDictionary<int, int>` | 8.38 | 0 B |

> In 1.x, measured on .NET 9, these allocated 32 to 56 B per `foreach` and the lists ran at about 2.2 times `List<int>`. The dictionaries scan every slot of their open-addressing table, and `ConcurrentHeapSpanDictionary` also copies a snapshot under its lock. The 0 B in its row holds once the pool supplies the snapshot buffer.

The key metric is the **ZA alloc** column. The `ref struct` collections target 0 B allocated on all paths except initial pool rent and `ToArray()`. The `Heap*` collections additionally allocate the collection object itself; their `foreach` allocates nothing, as described in [Zero-Allocation Enumerators](#3-zero-allocation-enumerators), except that `ConcurrentHeapSpanDictionary` allocates its snapshot buffer when the pool has none to lend.

## When Zero Allocation Matters

**It matters for:**

- Game loops running at 60+ FPS where any GC pause causes a visible frame drop
- Low-latency APIs where p99 latency is a hard requirement
- High-throughput message processing (>10k messages/second)
- Memory-constrained environments (containers with <256 MB, IoT, embedded .NET)
- Real-time audio/video pipelines where GC pauses cause audible/visible glitches

**It probably doesn't matter for:**

- Standard web APIs handling <1k req/sec — the collection cost is a rounding error compared to database round-trips
- Batch jobs that run once a day
- CLI tools that process a file and exit
- Applications where developer productivity and code clarity outweigh microsecond-level performance

If your profiler or allocation tracker does not show collection operations as a hotspot, use BCL collections. Switch to ZeroAlloc collections when measurements show they are needed.

## Tips for Maximum Performance

1. **Pre-size your collections** — pass a `capacity` parameter to the constructor. This avoids grow operations entirely when you know the approximate size.

2. **Use `ref struct` variants on hot paths** — they enforce stack-only lifetime and produce zero-alloc enumerators. Reserve `Heap*` variants for async code and long-lived storage; their struct enumerators make `foreach` allocation-free too, as long as you do not enumerate them through `IEnumerable<T>`. For `ConcurrentHeapSpanDictionary` that also needs the array pool to supply the snapshot buffer.

3. **Use `AsSpan()` instead of enumeration for bulk processing** — span-based iteration is faster than enumerator-based iteration because there is no `MoveNext()` / `Current` overhead.

4. **Pass a custom `ArrayPool<T>` for isolated workloads** — this prevents contention on `ArrayPool<T>.Shared` in high-concurrency scenarios and gives you control over buffer sizes.

5. **Use source generators for known types** — `[ZeroAllocList(typeof(int))]` generates specialized code that eliminates generic virtual dispatch, enabling the JIT to inline operations that would otherwise remain virtual calls.

6. **Dispose promptly** — the sooner you return a buffer to the pool, the sooner it can be reused. Use `using` declarations to ensure disposal at scope exit.

## Native AOT Compatibility

All ZeroAlloc.Collections types are fully compatible with Native AOT (`PublishAot`). There is no reflection, no `Type.GetType()`, no `Activator.CreateInstance()`, and no dynamic code generation at runtime. The source generators run at compile time and emit plain C#.

```xml
<PropertyGroup>
    <PublishAot>true</PublishAot>
</PropertyGroup>
```

```bash
dotnet publish -r linux-x64 -c Release
```

The published binary contains only direct method calls with no runtime type inspection.
