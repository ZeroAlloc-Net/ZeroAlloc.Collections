# ZeroAlloc.Collections

Zero-allocation, high-performance collection types for .NET.

## Collections

| Type | Ref Struct | Heap Variant | Description |
|------|-----------|--------------|-------------|
| `PooledList<T>` | Yes | `HeapPooledList<T>` | Pooled-backed growable list |
| `RingBuffer<T>` | Yes | `HeapRingBuffer<T>` | Fixed-capacity circular buffer |
| `SpanDictionary<TKey,TValue>` | Yes | `HeapSpanDictionary<TKey,TValue>` | Open-addressing hash map |
| `PooledStack<T>` | Yes | `HeapPooledStack<T>` | Pooled-backed LIFO stack |
| `PooledQueue<T>` | Yes | `HeapPooledQueue<T>` | Pooled-backed FIFO queue |
| `FixedSizeList<T>` | Yes | `HeapFixedSizeList<T>` | Stack-allocated fixed-capacity list |

## Installation

```bash
dotnet add package ZeroAlloc.Collections
```

## License

MIT
