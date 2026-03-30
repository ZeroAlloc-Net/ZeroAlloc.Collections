---
id: docs-index
title: Documentation
slug: /docs
description: ZeroAlloc.Collections documentation index — navigate to all available pages.
sidebar_position: 0
---

# ZeroAlloc.Collections Documentation

Zero-allocation, high-performance collection types for .NET — pooled lists, ring buffers, span dictionaries, stacks, queues, and fixed-size lists, each in ref struct and heap variants.

## Reference

| # | Guide | Description |
|---|-------|-------------|
| 1 | [Getting Started](getting-started.md) | Install and use your first collection in 5 minutes |
| 2 | [PooledList](pooled-list.md) | Growable list backed by `ArrayPool<T>` |
| 3 | [RingBuffer](ring-buffer.md) | Fixed-capacity circular buffer for producer/consumer scenarios |
| 4 | [SpanDictionary](span-dictionary.md) | Open-addressing hash map with `Span` accessors |
| 5 | [PooledStack & PooledQueue](stack-and-queue.md) | LIFO stack and FIFO queue with pooled storage |
| 6 | [FixedSizeList](fixed-size-list.md) | Stack-allocated fixed-capacity list |
| 7 | [Source Generators](source-generators.md) | Emit type-specific collections and enumerators at compile time |
| 8 | [Diagnostics](diagnostics.md) | Analyzer warnings and error reference |
| 9 | [Performance](performance.md) | Benchmark results, zero-alloc design, Native AOT |
| 10 | [Testing](testing.md) | Unit-test collections with xUnit |

## Cookbook

Real-world recipes for common scenarios.

| # | Recipe | Scenario |
|---|--------|----------|
| 1 | [High-Throughput Message Processing](cookbook/01-message-processing.md) | Batch message ingestion with pooled lists and ring buffers |
| 2 | [Game Loop Object Pooling](cookbook/02-game-loop.md) | Frame-scoped allocation using ref struct collections |
| 3 | [Zero-Alloc Request Parsing](cookbook/03-request-parsing.md) | Parse HTTP headers and query strings without allocation |
| 4 | [Custom Collection with Source Generator](cookbook/04-custom-collection.md) | Emit a domain-specific collection using `[ZeroAllocList<T>]` |

## Quick Reference

```csharp
// PooledList — growable, pooled
using var list = new PooledList<int>(capacity: 128);
list.Add(42);
ReadOnlySpan<int> span = list.AsReadOnlySpan();

// RingBuffer — fixed-capacity circular buffer
using var ring = new RingBuffer<byte>(capacity: 1024);
ring.TryWrite(0xFF);
ring.TryRead(out var b);

// SpanDictionary — open-addressing hash map
using var dict = new SpanDictionary<string, int>(capacity: 16);
dict.TryAdd("key", 1);
dict.TryGetValue("key", out var value);

// PooledStack — LIFO
using var stack = new PooledStack<double>();
stack.Push(3.14);
stack.TryPop(out var top);

// PooledQueue — FIFO
using var queue = new PooledQueue<string>();
queue.Enqueue("first");
queue.TryDequeue(out var front);

// FixedSizeList — stack-allocated, fixed capacity
using var fixedList = new FixedSizeList<long>(capacity: 8);
fixedList.Add(100L);

// Heap variant — usable in async code and as class fields
using var heapList = new HeapPooledList<int>(capacity: 64);
heapList.Add(1);
await ProcessAsync(heapList);

// Source generator — emit a type-specific collection
[ZeroAllocList<int>]
public partial struct IntList;
```
