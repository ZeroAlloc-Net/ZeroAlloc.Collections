# ZeroAlloc.Collections

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Collections.svg)](https://www.nuget.org/packages/ZeroAlloc.Collections)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

ZeroAlloc.Collections is a high-performance, zero-allocation collections library for .NET. It provides six collection types — each available as a `ref struct` for stack-only scenarios and as a heap-allocated class for use in async code, fields, and DI containers. All collections rent their backing storage from `ArrayPool<T>.Shared`, return it on `Dispose()`, and expose `Span<T>` accessors for tight inner loops. Source generators are included for emitting specialized, type-specific collection implementations at compile time.

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

- **Zero Allocation** — all pooled collections rent from `ArrayPool<T>.Shared` and return buffers on disposal
- **Ref Struct Variants** — stack-only types with compile-time lifetime enforcement and `ref T` indexers
- **Heap Variants** — class-based counterparts that implement `IDisposable`, `IList<T>`, and `IReadOnlyList<T>` for use in async methods and DI
- **Span Accessors** — `AsSpan()` and `AsReadOnlySpan()` on every collection for zero-copy interop
- **Source Generators** — emit type-specific collections, pooled wrappers, and `ref struct` enumerators at compile time
- **Analyzer Diagnostics** — build-time warnings for undisposed collections and accidental ref struct copies
- **Multi-TFM** — targets `netstandard2.1`, `net8.0`, and `net9.0` (`allows ref struct` on .NET 9)
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

## Documentation

| Page | Description |
|------|-------------|
| [Getting Started](docs/getting-started.md) | Install and use your first collection in five minutes |
| [PooledList](docs/pooled-list.md) | Growable list backed by `ArrayPool<T>` |
| [RingBuffer](docs/ring-buffer.md) | Fixed-capacity circular buffer |
| [SpanDictionary](docs/span-dictionary.md) | Open-addressing hash map with `Span` accessors |
| [PooledStack & PooledQueue](docs/stack-and-queue.md) | LIFO stack and FIFO queue with pooled storage |
| [FixedSizeList](docs/fixed-size-list.md) | Stack-allocated fixed-capacity list |
| [Source Generators](docs/source-generators.md) | Emit type-specific collections at compile time |
| [Diagnostics](docs/diagnostics.md) | Analyzer warnings and error reference |
| [Performance](docs/performance.md) | Benchmark results and zero-alloc design internals |
| [Testing](docs/testing.md) | Unit-test collections with xUnit |

## License

MIT
