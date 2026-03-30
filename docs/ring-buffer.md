---
id: ring-buffer
title: RingBuffer
slug: /docs/ring-buffer
description: A fixed-capacity circular buffer backed by ArrayPool — use it for producer-consumer queues, sliding windows, and bounded channels.
sidebar_position: 3
---

# RingBuffer

`RingBuffer<T>` is a fixed-capacity circular buffer. Once full, writes are rejected rather than silently overwriting old data. The buffer rents its backing array from `ArrayPool<T>` and returns it on disposal.

| Variant | Kind | Use when |
|---|---|---|
| `RingBuffer<T>` | `ref struct` | Synchronous, stack-scoped scenarios where zero GC pressure matters most. |
| `HeapRingBuffer<T>` | `sealed class` | You need to store the buffer in a field, pass it across `await` boundaries, or consume `IReadOnlyCollection<T>`. |

## Circular Buffer Concept

A ring buffer uses a fixed-size array with two cursors -- `head` (read position) and `tail` (write position). Both advance forward and wrap around to the beginning when they reach the end of the array:

```
Capacity = 5, Count = 3

  head          tail
   v              v
 [ A ][ B ][ C ][   ][   ]

After reading A:

        head     tail
         v        v
 [   ][ B ][ C ][   ][   ]

After writing D and E:

        head               tail (wraps to 0)
         v                  v
 [   ][ B ][ C ][ D ][ E ]

After writing F (wraps):

  tail  head
   v     v
 [ F ][ B ][ C ][ D ][ E ]   <- buffer is now full
```

The key invariant: `Count` never exceeds the capacity passed to the constructor. `TryWrite` returns `false` when the buffer is full; `TryRead` returns `false` when it is empty. No exceptions are thrown for normal flow control.

## Construction

```csharp
// Uses ArrayPool<T>.Shared
var ring = new RingBuffer<int>(capacity: 64);

// Custom pool
var pool = ArrayPool<int>.Create(maxArrayLength: 256, maxArraysPerBucket: 4);
var ring = new RingBuffer<int>(capacity: 64, pool);
```

`HeapRingBuffer<T>` offers the same two constructors:

```csharp
var heap = new HeapRingBuffer<string>(capacity: 128);
var heap = new HeapRingBuffer<string>(capacity: 128, customPool);
```

The capacity must be greater than zero; otherwise an `ArgumentOutOfRangeException` is thrown.

## TryWrite / TryRead / TryPeek

All three methods follow the `Try` pattern -- return a `bool` and never throw for normal empty/full conditions.

```csharp
using var ring = new RingBuffer<int>(4);

ring.TryWrite(10); // true
ring.TryWrite(20); // true
ring.TryWrite(30); // true
ring.TryWrite(40); // true
ring.TryWrite(50); // false — buffer is full

ring.TryRead(out int first);  // true, first == 10
ring.TryRead(out int second); // true, second == 20

ring.TryPeek(out int next);   // true, next == 30 (not removed)
ring.TryRead(out int third);  // true, third == 30
```

## IsFull / IsEmpty

```csharp
using var ring = new RingBuffer<int>(2);

ring.IsEmpty; // true
ring.IsFull;  // false

ring.TryWrite(1);
ring.TryWrite(2);

ring.IsEmpty; // false
ring.IsFull;  // true
```

## Enumeration

Both variants enumerate in FIFO order (head to tail). The ref struct version uses a zero-allocation `ref struct Enumerator`; the heap version returns `IEnumerator<T>`.

```csharp
using var ring = new RingBuffer<int>(8);
ring.TryWrite(10);
ring.TryWrite(20);
ring.TryWrite(30);

foreach (int item in ring)
    Console.Write($"{item} "); // 10 20 30
```

## When to Use a RingBuffer

- **Producer-consumer patterns** -- a single producer writes items; a single consumer reads them. The fixed capacity provides natural back-pressure.
- **Sliding windows** -- keep the last N measurements. When the buffer is full, read the oldest before writing a new one.
- **Bounded channels** -- use a ring buffer as a lightweight in-process channel when you do not need the full `System.Threading.Channels` API.

## Usage Example -- Rolling Average

```csharp
public double RollingAverage(ReadOnlySpan<double> samples, int windowSize)
{
    using var window = new RingBuffer<double>(windowSize);
    double sum = 0;

    foreach (double s in samples)
    {
        if (window.IsFull)
        {
            window.TryRead(out double oldest);
            sum -= oldest;
        }
        window.TryWrite(s);
        sum += s;
    }

    return window.Count > 0 ? sum / window.Count : 0;
}
```

## Clear and Dispose

`Clear()` resets head, tail, and count to zero and clears references for reference types. The rented buffer is retained.

`Dispose()` returns the rented buffer to the pool.

## API Reference

### RingBuffer&lt;T&gt; (ref struct)

| Member | Signature | Description |
|---|---|---|
| Constructor | `RingBuffer(int capacity)` | Fixed capacity, uses `ArrayPool<T>.Shared`. |
| Constructor | `RingBuffer(int capacity, ArrayPool<T> pool)` | Fixed capacity with a custom pool. |
| Property | `int Count` | Number of elements currently in the buffer. |
| Property | `bool IsEmpty` | `true` when `Count` is zero. |
| Property | `bool IsFull` | `true` when `Count` equals capacity. |
| Method | `bool TryWrite(T item)` | Writes to the tail; returns `false` if full. |
| Method | `bool TryRead(out T item)` | Reads and removes from the head; returns `false` if empty. |
| Method | `bool TryPeek(out T item)` | Reads the head without removing; returns `false` if empty. |
| Method | `void Clear()` | Resets head, tail, and count; retains buffer. |
| Method | `Enumerator GetEnumerator()` | Returns a zero-alloc ref struct enumerator (FIFO order). |
| Method | `void Dispose()` | Returns the rented buffer to the pool. |

### HeapRingBuffer&lt;T&gt; (sealed class : IReadOnlyCollection&lt;T&gt;, IDisposable)

| Member | Signature | Description |
|---|---|---|
| Constructor | `HeapRingBuffer(int capacity)` | Fixed capacity, uses `ArrayPool<T>.Shared`. |
| Constructor | `HeapRingBuffer(int capacity, ArrayPool<T> pool)` | Fixed capacity with a custom pool. |
| Property | `int Count` | Number of elements currently in the buffer. |
| Property | `bool IsEmpty` | `true` when `Count` is zero. |
| Property | `bool IsFull` | `true` when `Count` equals capacity. |
| Method | `bool TryWrite(T item)` | Writes to the tail; returns `false` if full. |
| Method | `bool TryRead(out T item)` | Reads and removes from the head; returns `false` if empty. |
| Method | `bool TryPeek(out T item)` | Reads the head without removing; returns `false` if empty. |
| Method | `void Clear()` | Resets head, tail, and count; retains buffer. |
| Method | `T[] ToArray()` | Copies elements to a new array in FIFO order. |
| Method | `IEnumerator<T> GetEnumerator()` | Returns an `IEnumerator<T>` (FIFO order). |
| Method | `void Dispose()` | Returns the rented buffer to the pool. |
