---
id: fixed-size-list
title: FixedSizeList
sidebar_position: 6
---

# FixedSizeList

`FixedSizeList<T>` is a `ref struct` list with a fixed capacity determined at construction time. It wraps a caller-provided `Span<T>` — typically stack-allocated via `stackalloc` — so it performs zero heap allocations. `HeapFixedSizeList<T>` is the heap-based counterpart that implements `IList<T>` and `IReadOnlyList<T>`, making it usable in async code, as a class field, and anywhere interfaces are required.

## When to Use

- You know the maximum number of elements at compile time or call time
- You need a temporary buffer scoped to a single method
- You want to avoid `ArrayPool` overhead for small, short-lived lists
- You need stack allocation for latency-critical hot paths (game loops, parsers, serializers)

If the maximum size is unknown or the list needs to grow, use `PooledList<T>` instead.

## FixedSizeList&lt;T&gt; (ref struct)

### Construction

`FixedSizeList<T>` takes a `Span<T>` as its backing storage. The most common pattern is `stackalloc`:

```csharp
var list = new FixedSizeList<int>(stackalloc int[16]);

list.Add(1);
list.Add(2);
list.Add(3);

foreach (ref readonly var item in list)
{
    Console.WriteLine(item);
}
```

You can also pass a span over an existing array or inline buffer:

```csharp
int[] buffer = new int[64];
var list = new FixedSizeList<int>(buffer.AsSpan());
```

### Add vs TryAdd

`Add` throws `InvalidOperationException` when the list is full. `TryAdd` returns `false` instead, which is useful when you want to fill a buffer without exception handling:

```csharp
var list = new FixedSizeList<int>(stackalloc int[4]);

list.Add(1);       // OK
list.Add(2);       // OK
list.Add(3);       // OK
list.Add(4);       // OK
// list.Add(5);    // throws InvalidOperationException

bool added = list.TryAdd(5); // returns false — list is full
```

### No Dispose Needed

`FixedSizeList<T>` does not own its memory. The caller provides the span, so there is nothing to return or free. This is different from `PooledList<T>`, which rents from `ArrayPool` and must be disposed.

### Span Access

`AsSpan()` and `AsReadOnlySpan()` return slices over only the active elements (not the full buffer):

```csharp
var list = new FixedSizeList<byte>(stackalloc byte[128]);
list.Add(0x01);
list.Add(0x02);

Span<byte> active = list.AsSpan();           // length = 2
ReadOnlySpan<byte> ro = list.AsReadOnlySpan(); // length = 2
```

### API Reference

| Member | Type | Description |
|--------|------|-------------|
| `FixedSizeList(Span<T> buffer)` | Constructor | Wraps the provided span as backing storage |
| `Count` | `int` | Number of elements currently in the list |
| `Capacity` | `int` | Total capacity of the backing span |
| `IsFull` | `bool` | `true` when `Count == Capacity` |
| `this[int index]` | `ref T` | Returns a reference to the element at the specified index; throws `ArgumentOutOfRangeException` if out of range |
| `Add(T item)` | `void` | Appends an item; throws `InvalidOperationException` if full |
| `TryAdd(T item)` | `bool` | Appends an item if space is available; returns `false` if full |
| `AsSpan()` | `Span<T>` | Returns a span over the active elements |
| `AsReadOnlySpan()` | `ReadOnlySpan<T>` | Returns a read-only span over the active elements |
| `Clear()` | `void` | Resets `Count` to zero (does not clear memory) |
| `GetEnumerator()` | `Enumerator` | Returns a `ref struct` enumerator with `ref readonly T Current` |

## HeapFixedSizeList&lt;T&gt; (sealed class)

`HeapFixedSizeList<T>` allocates a regular `T[]` array of the specified capacity. It implements `IList<T>` and `IReadOnlyList<T>`, so it works everywhere the ref struct variant cannot: async methods, class fields, LINQ, and interface-based APIs.

```csharp
var list = new HeapFixedSizeList<string>(capacity: 32);
list.Add("hello");
list.Add("world");

// Implements IList<T> — works with any API expecting a list
IList<string> ilist = list;
Console.WriteLine(ilist[0]); // "hello"
```

### Insert, Remove, and Search

Unlike the ref struct variant, `HeapFixedSizeList<T>` supports full `IList<T>` operations:

```csharp
var list = new HeapFixedSizeList<int>(capacity: 8);
list.Add(10);
list.Add(30);

list.Insert(1, 20);      // [10, 20, 30]
list.Remove(20);          // [10, 30]
list.RemoveAt(0);         // [30]
bool has = list.Contains(30); // true
int idx = list.IndexOf(30);   // 0
```

### API Reference

| Member | Type | Description |
|--------|------|-------------|
| `HeapFixedSizeList(int capacity)` | Constructor | Allocates a `T[]` of the specified capacity; throws `ArgumentOutOfRangeException` if negative |
| `Count` | `int` | Number of elements currently in the list |
| `Capacity` | `int` | Total capacity of the backing array |
| `IsFull` | `bool` | `true` when `Count == Capacity` |
| `IsReadOnly` | `bool` | Always `false` |
| `this[int index]` | `T` (get/set) | Gets or sets the element at the specified index |
| `Add(T item)` | `void` | Appends an item; throws `InvalidOperationException` if full |
| `TryAdd(T item)` | `bool` | Appends an item if space is available; returns `false` if full |
| `Insert(int index, T item)` | `void` | Inserts an item at the specified index; throws if full or index out of range |
| `RemoveAt(int index)` | `void` | Removes the item at the specified index |
| `Remove(T item)` | `bool` | Removes the first occurrence of the item; returns `false` if not found |
| `Contains(T item)` | `bool` | Returns `true` if the item is in the list |
| `IndexOf(T item)` | `int` | Returns the index of the first occurrence, or `-1` |
| `Clear()` | `void` | Resets `Count` to zero; clears references for GC if `T` is a reference type |
| `CopyTo(T[] array, int arrayIndex)` | `void` | Copies active elements to the target array |
| `ToArray()` | `T[]` | Returns a new array containing the active elements |
| `GetEnumerator()` | `IEnumerator<T>` | Returns an enumerator (implements `IEnumerable<T>`) |

## Choosing Between the Two

| Consideration | `FixedSizeList<T>` | `HeapFixedSizeList<T>` |
|---------------|---------------------|------------------------|
| Allocation | Zero (stack or caller-provided) | One `T[]` on the heap |
| Usable in async methods | No (ref struct) | Yes |
| Usable as class/struct field | No (ref struct) | Yes |
| Implements `IList<T>` | No | Yes |
| Insert/Remove support | No | Yes |
| `Dispose()` needed | No | No |
| Best for | Hot-path temp buffers | Longer-lived fixed-capacity lists |
