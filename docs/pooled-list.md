---
id: pooled-list
title: PooledList
slug: /docs/pooled-list
description: A growable list backed by ArrayPool — available as a ref struct for hot paths and as a class for heap storage.
sidebar_position: 2
---

# PooledList

`PooledList<T>` is a growable list that rents its backing array from `ArrayPool<T>` instead of allocating one directly. When you are finished, call `Dispose()` to return the array to the pool. The library ships two variants:

| Variant | Kind | Use when |
|---|---|---|
| `PooledList<T>` | `ref struct` | Synchronous, stack-scoped hot paths where zero GC pressure matters most. |
| `HeapPooledList<T>` | `sealed class` | You need to store the list in a field, pass it across `await` boundaries, or consume `IList<T>` / `IReadOnlyList<T>`. |

Both variants share the same pooling and growth logic. The ref struct version adds a `ref return` indexer and a zero-allocation `ref struct` enumerator; the class version implements the standard collection interfaces instead.

## Construction

```csharp
// Default — empty, uses ArrayPool<T>.Shared
var list = new PooledList<int>();

// Pre-sized — rents at least 64 slots up front
var list = new PooledList<int>(64);

// Custom pool
var pool = ArrayPool<int>.Create(maxArrayLength: 1024, maxArraysPerBucket: 4);
var list = new PooledList<int>(64, pool);
```

`HeapPooledList<T>` offers the same three constructors:

```csharp
var heap = new HeapPooledList<string>();
var heap = new HeapPooledList<string>(128);
var heap = new HeapPooledList<string>(128, customPool);
```

## Adding Items and Auto-Grow

`Add` appends to the end. When the internal array is full the list rents a new array of double the size, copies elements over, and returns the old array to the pool. The default initial capacity is 4.

```csharp
using var list = new PooledList<int>();
list.Add(10);
list.Add(20);
list.Add(30);
// list.Count == 3
```

`Insert` places an element at a specific index, shifting subsequent elements to the right. It grows the buffer if necessary.

```csharp
list.Insert(1, 15); // [10, 15, 20, 30]
```

## Indexer

The ref struct variant returns a **reference** to the element, allowing in-place mutation without copying:

```csharp
using var list = new PooledList<int>();
list.Add(42);
ref int slot = ref list[0];
slot = 99; // modifies the element in place
```

`HeapPooledList<T>` returns a value copy through the standard `T this[int index]` property (get/set).

## Span Access

Both variants expose the active region of the backing array as a span. This is the fastest way to pass data to APIs that accept `Span<T>` or `ReadOnlySpan<T>`.

```csharp
using var list = new PooledList<int>(16);
for (int i = 0; i < 10; i++)
    list.Add(i);

ReadOnlySpan<int> span = list.AsReadOnlySpan();
int sum = 0;
foreach (int v in span)
    sum += v;
```

## Search and Removal

```csharp
using var list = new PooledList<string>(8);
list.Add("alpha");
list.Add("beta");
list.Add("gamma");

bool found = list.Contains("beta");   // true
int idx    = list.IndexOf("gamma");    // 2

list.RemoveAt(1); // removes "beta", shifts "gamma" left
// list is now ["alpha", "gamma"]
```

`HeapPooledList<T>` additionally exposes `Remove(T)`, which finds and removes the first occurrence and returns `true` if the item was present.

## Clear vs Dispose

`Clear()` resets the count to zero and clears references (for reference types) but **keeps** the rented buffer. Use it to reuse the list without returning the array to the pool.

`Dispose()` returns the rented buffer to the pool and sets the internal state to empty. It is safe to call multiple times.

```csharp
using var list = new PooledList<int>(64);
list.Add(1);
list.Add(2);

list.Clear();
// Count == 0, buffer still rented — ready for reuse

list.Add(3);
// Count == 1, no new rent needed
```

```csharp
// Dispose returns the buffer
list.Dispose();
// After dispose the list is empty and holds no array
```

## Zero-Alloc Foreach

The ref struct variant provides a `ref struct Enumerator` whose `Current` property returns `ref readonly T`. This means iterating does not box, does not allocate an enumerator object, and gives read-only access by reference.

```csharp
using var list = new PooledList<LargeStruct>();
list.Add(new LargeStruct(1));
list.Add(new LargeStruct(2));

foreach (ref readonly LargeStruct item in list)
{
    // No copy — item is a direct reference into the backing array
    Console.WriteLine(item.Id);
}
```

`HeapPooledList<T>` uses `IEnumerator<T>` via `yield return`, which does allocate. If you need allocation-free iteration on the heap variant, call `AsSpan()` and iterate the span instead.

## ToArray and CopyTo

```csharp
using var list = new PooledList<int>();
list.Add(10);
list.Add(20);

int[] snapshot = list.ToArray(); // new array [10, 20]
```

`HeapPooledList<T>` additionally provides `CopyTo(T[] array, int arrayIndex)` for the `ICollection<T>` contract.

## Usage Example — Building a Temporary Result Set

```csharp
public ReadOnlySpan<int> GetEvenNumbers(ReadOnlySpan<int> source)
{
    using var results = new PooledList<int>(source.Length);
    foreach (int n in source)
    {
        if (n % 2 == 0)
            results.Add(n);
    }
    return results.AsReadOnlySpan();
}
```

## Common Pitfalls

**Pitfall 1 -- Forgetting to dispose**

If you never call `Dispose()`, the rented array is never returned to the pool. Over time this exhausts the pool and forces new allocations -- defeating the purpose.

```csharp
// BAD — array leaks
var list = new PooledList<int>();
list.Add(1);
// no Dispose()

// GOOD — using statement handles it
using var list = new PooledList<int>();
list.Add(1);
```

**Pitfall 2 -- Copying a ref struct**

`PooledList<T>` is a ref struct. Assigning it to another variable copies the struct, and now two copies share the same rented array. Disposing one invalidates the other.

```csharp
// BAD
var a = new PooledList<int>();
a.Add(1);
var b = a;      // b is a shallow copy
b.Dispose();    // returns the array
a.Add(2);       // undefined behavior — array was returned
```

Always use `using var` and avoid passing the struct by value. If you need to pass it, use `ref` or `in`.

**Pitfall 3 -- Returning a span from a disposed list**

The span returned by `AsSpan()` points into the rented array. If the list is disposed, the span is dangling.

```csharp
// BAD
Span<int> GetItems()
{
    using var list = new PooledList<int>();
    list.Add(1);
    return list.AsSpan(); // dangling — list disposes at end of scope
}
```

Copy to an array with `ToArray()` if the data must outlive the list.

## API Reference

### PooledList&lt;T&gt; (ref struct)

| Member | Signature | Description |
|---|---|---|
| Constructor | `PooledList()` | Empty list, uses `ArrayPool<T>.Shared`. |
| Constructor | `PooledList(int capacity)` | Pre-sized, uses `ArrayPool<T>.Shared`. |
| Constructor | `PooledList(int capacity, ArrayPool<T> pool)` | Pre-sized with a custom pool. |
| Property | `int Count` | Number of active elements. |
| Indexer | `ref T this[int index]` | Returns a reference to the element at `index`. |
| Method | `void Add(T item)` | Appends an item, growing if necessary. |
| Method | `Span<T> AsSpan()` | Span over active elements. |
| Method | `ReadOnlySpan<T> AsReadOnlySpan()` | Read-only span over active elements. |
| Method | `void Clear()` | Resets count to zero; retains buffer. |
| Method | `T[] ToArray()` | Copies active elements to a new array. |
| Method | `void RemoveAt(int index)` | Removes element at `index`, shifting left. |
| Method | `void Insert(int index, T item)` | Inserts at `index`, shifting right. |
| Method | `bool Contains(T item)` | Returns `true` if `item` is in the list. |
| Method | `int IndexOf(T item)` | Index of first occurrence, or `-1`. |
| Method | `Enumerator GetEnumerator()` | Returns a zero-alloc ref struct enumerator. |
| Method | `void Dispose()` | Returns the rented buffer to the pool. |

**Enumerator** (ref struct):

| Member | Signature | Description |
|---|---|---|
| Property | `ref readonly T Current` | Read-only reference to the current element. |
| Method | `bool MoveNext()` | Advances to the next element. |

### HeapPooledList&lt;T&gt; (sealed class : IList&lt;T&gt;, IReadOnlyList&lt;T&gt;, IDisposable)

| Member | Signature | Description |
|---|---|---|
| Constructor | `HeapPooledList()` | Empty list, uses `ArrayPool<T>.Shared`. |
| Constructor | `HeapPooledList(int capacity)` | Pre-sized, uses `ArrayPool<T>.Shared`. |
| Constructor | `HeapPooledList(int capacity, ArrayPool<T> pool)` | Pre-sized with a custom pool. |
| Property | `int Count` | Number of active elements. |
| Property | `bool IsReadOnly` | Always `false`. |
| Indexer | `T this[int index]` | Gets or sets the element at `index`. |
| Method | `void Add(T item)` | Appends an item, growing if necessary. |
| Method | `Span<T> AsSpan()` | Span over active elements. |
| Method | `ReadOnlySpan<T> AsReadOnlySpan()` | Read-only span over active elements. |
| Method | `void Clear()` | Resets count to zero; retains buffer. |
| Method | `T[] ToArray()` | Copies active elements to a new array. |
| Method | `void RemoveAt(int index)` | Removes element at `index`, shifting left. |
| Method | `void Insert(int index, T item)` | Inserts at `index`, shifting right. |
| Method | `bool Contains(T item)` | Returns `true` if `item` is in the list. |
| Method | `int IndexOf(T item)` | Index of first occurrence, or `-1`. |
| Method | `bool Remove(T item)` | Removes first occurrence; returns `true` if found. |
| Method | `void CopyTo(T[] array, int arrayIndex)` | Copies elements to a target array. |
| Method | `IEnumerator<T> GetEnumerator()` | Returns an `IEnumerator<T>`. |
| Method | `void Dispose()` | Returns the rented buffer to the pool. |
