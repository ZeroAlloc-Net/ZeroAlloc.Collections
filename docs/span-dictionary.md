---
id: span-dictionary
title: SpanDictionary
slug: /docs/span-dictionary
description: An open-addressing dictionary with zero per-node allocations — available as a ref struct and a class.
sidebar_position: 4
---

# SpanDictionary

`SpanDictionary<TKey, TValue>` is a dictionary that uses **open addressing with linear probing** instead of the chained hashing used by `System.Collections.Generic.Dictionary<TKey, TValue>`. The result is zero per-node allocations -- every entry lives inside a single flat array of structs.

| Variant | Kind | Use when |
|---|---|---|
| `SpanDictionary<TKey, TValue>` | `ref struct` | Synchronous, stack-scoped lookups where allocation cost must be zero. |
| `HeapSpanDictionary<TKey, TValue>` | `sealed class` | You need to store the dictionary in a field, pass it across `await` boundaries, or consume `IDictionary<TKey, TValue>` / `IReadOnlyDictionary<TKey, TValue>`. |

## Why Open Addressing?

The BCL `Dictionary<TKey, TValue>` uses separate chaining: each bucket points to a linked list node, and every insertion allocates a node object. For short-lived dictionaries in hot paths this creates GC pressure that dominates the runtime cost.

`SpanDictionary` stores keys, values, and hash codes directly in a contiguous `Entry[]` array. Lookups walk the array linearly from the hash slot until they find the key or an empty slot. Deletions use tombstone markers (`Deleted` state) so that probe chains are not broken.

```
Open addressing (linear probing):

  hash("alpha") = 2

  Index:   0       1       2       3       4       5
  State: [Empty] [Empty] [Occupied] [Occupied] [Empty] [Empty]
  Key:                    "alpha"    "beta"
  Value:                   100        200

  Lookup for "alpha": hash -> index 2 -> match -> return 100
  Lookup for "beta":  hash -> index 2 -> no match -> probe 3 -> match -> return 200
```

## Construction

```csharp
// Minimum capacity of 4 (default)
var dict = new SpanDictionary<string, int>(capacity: 16);
```

`HeapSpanDictionary<TKey, TValue>` supports a default constructor as well:

```csharp
var heap = new HeapSpanDictionary<string, int>();       // default capacity
var heap = new HeapSpanDictionary<string, int>(64);     // explicit capacity
```

## Add, Indexer, and TryGetValue

```csharp
using var dict = new SpanDictionary<string, int>(8);

// Add throws if the key already exists
dict.Add("alpha", 1);
dict.Add("beta", 2);

// Indexer get — throws KeyNotFoundException if missing
int v = dict["alpha"]; // 1

// Indexer set — inserts or updates
dict["gamma"] = 3;
dict["alpha"] = 10; // updates existing key

// TryGetValue — safe lookup
if (dict.TryGetValue("beta", out int val))
    Console.WriteLine(val); // 2
```

## ContainsKey and Remove

```csharp
bool exists = dict.ContainsKey("gamma"); // true
bool removed = dict.Remove("gamma");     // true
bool again = dict.Remove("gamma");       // false — already removed
```

`Remove` marks the entry as `Deleted` (tombstone). The slot is reused by future insertions, and probe chains that pass through it continue correctly.

## Load Factor and Auto-Grow

The dictionary maintains a load factor threshold of 75%. When `(Count + 1) * 4 >= Capacity * 3`, it doubles the backing array and rehashes all occupied entries. You never need to resize manually, but choosing a reasonable initial capacity avoids unnecessary rehashes.

## Enumeration

Both variants enumerate `KeyValuePair<TKey, TValue>` over the occupied entries. Iteration order is not guaranteed to match insertion order.

```csharp
using var dict = new SpanDictionary<string, int>(8);
dict.Add("x", 1);
dict.Add("y", 2);

foreach (var kvp in dict)
    Console.WriteLine($"{kvp.Key} = {kvp.Value}");
```

The ref struct variant uses a `ref struct Enumerator` (zero allocation). The class variant returns `IEnumerator<KeyValuePair<TKey, TValue>>`.

## Comparison with BCL Dictionary

| Feature | `Dictionary<K,V>` | `SpanDictionary<K,V>` |
|---|---|---|
| Hashing strategy | Separate chaining | Open addressing (linear probing) |
| Per-node allocation | Yes (linked-list nodes) | No (flat `Entry[]` array) |
| Deletion | Frees node | Tombstone marker |
| Thread safety | None | None |
| Interface support | `IDictionary`, `IReadOnlyDictionary` | Ref struct: none. Heap variant: both. |
| Span / ref struct compatible | No | Yes (ref struct variant) |

Use `SpanDictionary` when you build and discard a dictionary within a single method or scope and want to avoid per-entry allocations. For long-lived dictionaries shared across your application, the BCL `Dictionary` is the right choice.

## Usage Example -- Counting Occurrences

```csharp
public void CountWords(ReadOnlySpan<string> words)
{
    using var counts = new SpanDictionary<string, int>(words.Length);

    foreach (string word in words)
    {
        if (counts.TryGetValue(word, out int n))
            counts[word] = n + 1;
        else
            counts.Add(word, 1);
    }

    foreach (var kvp in counts)
        Console.WriteLine($"{kvp.Key}: {kvp.Value}");
}
```

## Clear and Dispose

`Clear()` resets all entries to `Empty` and sets the count to zero. The backing array is retained.

`Dispose()` nulls out the internal array. Because `SpanDictionary` allocates the entry array directly (not via `ArrayPool`), there is no pool to return to -- `Dispose` simply releases the reference for GC.

## API Reference

### SpanDictionary&lt;TKey, TValue&gt; (ref struct)

| Member | Signature | Description |
|---|---|---|
| Constructor | `SpanDictionary(int capacity)` | Creates a dictionary with the given initial capacity. |
| Property | `int Count` | Number of occupied entries. |
| Indexer | `TValue this[TKey key]` | Gets or sets the value for `key`. Throws `KeyNotFoundException` on get if missing. |
| Method | `void Add(TKey key, TValue value)` | Inserts a new entry. Throws `ArgumentException` if the key exists. |
| Method | `bool TryGetValue(TKey key, out TValue value)` | Returns `true` and the value if the key exists. |
| Method | `bool ContainsKey(TKey key)` | Returns `true` if the key is present. |
| Method | `bool Remove(TKey key)` | Removes the key (tombstone); returns `true` if found. |
| Method | `void Clear()` | Resets all entries; retains the backing array. |
| Method | `Enumerator GetEnumerator()` | Returns a zero-alloc ref struct enumerator. |
| Method | `void Dispose()` | Releases internal state. |

**Enumerator** (ref struct):

| Member | Signature | Description |
|---|---|---|
| Property | `KeyValuePair<TKey, TValue> Current` | The current key/value pair. |
| Method | `bool MoveNext()` | Advances to the next occupied entry. |

### HeapSpanDictionary&lt;TKey, TValue&gt; (sealed class : IDictionary&lt;TKey, TValue&gt;, IReadOnlyDictionary&lt;TKey, TValue&gt;, IDisposable)

| Member | Signature | Description |
|---|---|---|
| Constructor | `HeapSpanDictionary()` | Default capacity (4). |
| Constructor | `HeapSpanDictionary(int capacity)` | Explicit initial capacity. |
| Property | `int Count` | Number of occupied entries. |
| Property | `bool IsReadOnly` | Always `false`. |
| Property | `ICollection<TKey> Keys` | Collection of all keys. |
| Property | `ICollection<TValue> Values` | Collection of all values. |
| Indexer | `TValue this[TKey key]` | Gets or sets the value for `key`. |
| Method | `void Add(TKey key, TValue value)` | Inserts a new entry. Throws if key exists. |
| Method | `void Add(KeyValuePair<TKey, TValue> item)` | Inserts via `KeyValuePair`. |
| Method | `bool TryGetValue(TKey key, out TValue value)` | Returns `true` and the value if found. |
| Method | `bool ContainsKey(TKey key)` | Returns `true` if the key is present. |
| Method | `bool Contains(KeyValuePair<TKey, TValue> item)` | Key exists and value matches. |
| Method | `bool Remove(TKey key)` | Removes by key (tombstone); returns `true` if found. |
| Method | `bool Remove(KeyValuePair<TKey, TValue> item)` | Removes if key and value both match. |
| Method | `void Clear()` | Resets all entries; retains array. |
| Method | `void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)` | Copies entries to a target array. |
| Method | `IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()` | Returns an `IEnumerator`. |
| Method | `void Dispose()` | Releases internal state. |
