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
- **`IEnumerator<T>`** — calling `GetEnumerator()` on any BCL collection allocates a class instance on the heap (or boxes a struct enumerator when accessed through the interface).

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

Every collection's `GetEnumerator()` returns a `ref struct` enumerator. Because `ref struct` types cannot be boxed, `foreach` over a ZeroAlloc collection never allocates. The enumerator lives on the stack for the duration of the loop.

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

### 4. Open-Addressing Hash Map

`SpanDictionary<TKey, TValue>` uses open addressing (linear probing) instead of separate chaining. This means there are no `Node` objects, no linked lists, and no per-entry heap allocations. All entries live in a single flat array rented from the pool.

## Benchmark Results

Benchmarks are run with BenchmarkDotNet on .NET 9.0, Windows 11, x64 RyuJIT AVX2. Full benchmark source is in `tests/ZeroAlloc.Collections.Benchmarks/`. Run `dotnet run -c Release` in that project to reproduce results on your hardware.

### Add N items

| N | BCL (`List<int>`) | `PooledList<int>` | Speedup | BCL Alloc | ZA Alloc |
|------:|------------------:|------------------:|--------:|----------:|---------:|
| 100 | 263 ns | 111 ns | 2.4× | 1,184 B | 0 B |
| 1,000 | 1,330 ns | 681 ns | 2.0× | 8,424 B | 0 B |
| 10,000 | 15,104 ns | 7,040 ns | 2.1× | 131,400 B | ~1 B |

### Enumerate N items (foreach)

| N | BCL (`List<int>`) | `PooledList<int>` | BCL Alloc | ZA Alloc |
|------:|------------------:|------------------:|----------:|---------:|
| 100 | 149 ns | 145 ns | 456 B | 0 B |
| 1,000 | 1,216 ns | 1,592 ns | 4,056 B | 0 B |
| 10,000 | 11,980 ns | 11,706 ns | 40,056 B | ~1 B |

> `List<int>` enumerator allocation comes from boxing the `List<T>.Enumerator` struct when accessed through `IEnumerable<T>`. The ZeroAlloc `ref struct` enumerator is never boxed.

### Push/pop N items

| N | BCL (`Stack<int>`) | `PooledStack<int>` | Speedup | BCL Alloc | ZA Alloc |
|------:|-------------------:|-------------------:|--------:|----------:|---------:|
| 100 | 357 ns | 197 ns | 1.8× | 1,184 B | 0 B |
| 1,000 | 2,590 ns | 1,429 ns | 1.8× | 8,424 B | 0 B |
| 10,000 | 23,021 ns | 14,853 ns | 1.5× | 131,400 B | 2 B |

### Enqueue/dequeue N items

| N | BCL (`Queue<int>`) | `PooledQueue<int>` | BCL Alloc | ZA Alloc |
|------:|-------------------:|-------------------:|----------:|---------:|
| 100 | 439 ns | 744 ns | 1,192 B | 0 B |
| 1,000 | 3,172 ns | 8,967 ns | 8,432 B | 0 B |
| 10,000 | 37,196 ns | 126,427 ns | 131,408 B | 16 B |

> `PooledQueue` is slower than BCL `Queue` for large N due to the grow-and-unwrap operation required to maintain circular-buffer invariants in a pooled array. The allocation saving is 100% for N ≤ 1,000. Use `RingBuffer<T>` instead of `PooledQueue<T>` when capacity is known in advance — it has no grow overhead.

### Add + lookup N entries

| N | BCL (`Dictionary<string,int>`) | `SpanDictionary<string,int>` | Speedup | BCL Alloc | ZA Alloc |
|------:|-------------------------------:|-----------------------------:|--------:|----------:|---------:|
| 100 | 1,270 ns | 719 ns | 1.8× | 7.4 KB | 0 B |
| 1,000 | 11,821 ns | 6,368 ns | 1.9× | 71.5 KB | 0 B |

> `SpanDictionary` allocates **0 bytes** because its entry array is rented from `ArrayPool<T>` and returned on `Dispose`. The open-addressing layout (no `Node` objects, no bucket chains) also eliminates the per-entry allocation overhead found in `Dictionary`.

The key metric is the **ZA Alloc** column. ZeroAlloc collections target 0 B allocated on all paths except initial pool rent and `ToArray()`.

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

2. **Use `ref struct` variants on hot paths** — they enforce stack-only lifetime and produce zero-alloc enumerators. Reserve `Heap*` variants for async code and long-lived storage.

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
