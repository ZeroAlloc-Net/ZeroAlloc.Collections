# ZeroAlloc.Collections

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Collections.svg)](https://www.nuget.org/packages/ZeroAlloc.Collections)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

ZeroAlloc.Collections is a high-performance, zero-allocation collections library for .NET. It provides six collection types — each available as a `ref struct` for stack-only scenarios and as a heap-allocated class for use in async code, fields, and DI containers. The pooled collections rent their backing storage from `ArrayPool<T>.Shared` and return it on `Dispose()`; `FixedSizeList<T>` uses a buffer you supply, such as `stackalloc`, and `HeapFixedSizeList<T>` a plain array. The list types expose `Span<T>` accessors for tight inner loops. Source generators are included for emitting specialized, type-specific collection implementations at compile time.

## Install

```bash
dotnet add package ZeroAlloc.Collections
```

## Example

### PooledList

```csharp
using ZeroAlloc.Collections;

using var list = new PooledList<int>(capacity: 64);

list.Add(1);
list.Add(2);
list.Add(3);

foreach (var item in list)
{
    Console.WriteLine(item);
}
```

The list rents from `ArrayPool<T>.Shared` on construction and returns the buffer when `Dispose()` runs — zero heap allocation for the backing array.

### RingBuffer

```csharp
using ZeroAlloc.Collections;

using var ring = new RingBuffer<string>(capacity: 4);

ring.TryWrite("alpha");
ring.TryWrite("beta");
ring.TryWrite("gamma");

while (ring.TryRead(out var item))
{
    Console.WriteLine(item); // alpha, beta, gamma
}
```

A fixed-capacity circular buffer suitable for producer/consumer queues, telemetry windows, and bounded logging.

## Features

- **Zero Allocation on ref struct paths** — the pooled collections rent from `ArrayPool<T>.Shared` and return buffers on disposal; the `ref struct` variants add, look up and enumerate with 0 B allocated. The `Heap*` variants allocate one enumerator object per `foreach`, 32-56 B, until struct enumerators land in the next major ([#152](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/issues/152))
- **Ref Struct Variants** — stack-only types with compile-time lifetime enforcement and `ref T` indexers
- **Heap Variants** — class-based counterparts for use in async methods, fields and DI. `HeapPooledList<T>` and `HeapFixedSizeList<T>` implement `IList<T>` and `IReadOnlyList<T>`; `HeapPooledStack<T>`, `HeapPooledQueue<T>` and `HeapRingBuffer<T>` implement `IReadOnlyCollection<T>`; `HeapSpanDictionary<TKey,TValue>` implements `IDictionary<TKey,TValue>` and `IReadOnlyDictionary<TKey,TValue>`. Every pooled heap variant implements `IDisposable`; `HeapFixedSizeList<T>` owns a plain array and needs no disposal
- **Span Accessors** — `AsSpan()` and `AsReadOnlySpan()` on `PooledList<T>`, `FixedSizeList<T>` and `HeapPooledList<T>`, and `AsSpan()` on `PooledStack<T>`, for zero-copy interop
- **Source Generators** — emit type-specific collections, pooled wrappers, and `ref struct` enumerators at compile time
- **Analyzer Diagnostics** — ZAC001 warns when a pooled collection is not disposed; ZAC010–ZAC013 flag source-generator misconfiguration such as ambiguous or missing fields
- **Multi-TFM** — targets `netstandard2.1`, `net8.0`, and `net9.0`
- **Native AOT Compatible** — no reflection, no dynamic code generation

## Collections

| Type | Ref Struct | Heap Variant | Description |
|------|-----------|--------------|-------------|
| `PooledList<T>` | Yes | `HeapPooledList<T>` | Pooled-backed growable list |
| `RingBuffer<T>` | Yes | `HeapRingBuffer<T>` | Fixed-capacity circular buffer |
| `SpanDictionary<TKey,TValue>` | Yes | `HeapSpanDictionary<TKey,TValue>` | Open-addressing hash map |
| `PooledStack<T>` | Yes | `HeapPooledStack<T>` | Pooled-backed LIFO stack |
| `PooledQueue<T>` | Yes | `HeapPooledQueue<T>` | Pooled-backed FIFO queue |
| `FixedSizeList<T>` | Yes | `HeapFixedSizeList<T>` | Stack-allocated fixed-capacity list |
| `ConcurrentHeapSpanDictionary<TKey,TValue>` | No | — | Thread-safe, coarse-locked, pooled hash map |

## Documentation

| Page | Description |
|------|-------------|
| [Getting Started](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/getting-started.md) | Install and use your first collection in five minutes |
| [PooledList](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/pooled-list.md) | Growable list backed by `ArrayPool<T>` |
| [RingBuffer](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/ring-buffer.md) | Fixed-capacity circular buffer |
| [SpanDictionary](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/span-dictionary.md) | Open-addressing hash map with `Span` accessors |
| [PooledStack & PooledQueue](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/stack-and-queue.md) | LIFO stack and FIFO queue with pooled storage |
| [FixedSizeList](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/fixed-size-list.md) | Stack-allocated fixed-capacity list |
| [ConcurrentHeapSpanDictionary](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/concurrent-heap-span-dictionary.md) | Thread-safe pooled dictionary, a drop-in for `ConcurrentDictionary` |
| [Source Generators](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/source-generators.md) | Emit type-specific collections at compile time |
| [Diagnostics](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/diagnostics.md) | Analyzer warnings and error reference |
| [Performance](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/performance.md) | Benchmark results and zero-alloc design internals |
| [Testing](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/blob/main/docs/testing.md) | Unit-test collections with xUnit |

## License

MIT
