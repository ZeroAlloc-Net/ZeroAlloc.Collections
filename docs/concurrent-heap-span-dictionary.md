---
id: concurrent-heap-span-dictionary
title: ConcurrentHeapSpanDictionary
slug: /docs/concurrent-heap-span-dictionary
description: A thread-safe, coarse-locked, ArrayPool-backed dictionary — drop-in for ConcurrentDictionary when pooled bucket storage is wanted.
sidebar_position: 6
---

# ConcurrentHeapSpanDictionary

`ConcurrentHeapSpanDictionary<TKey, TValue>` is a thread-safe, `ArrayPool`-backed, disposable hash map. Drop-in replacement for `System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>` when pooled bucket storage is wanted.

## When to use

- You hold a **short-lived** concurrent dictionary — per-request scope, per-session accumulator, per-time-window bucket — where construction allocation pressure matters.
- Profiling shows the bucket-array allocation as a hot item.
- You don't need throughput beyond what a coarse `lock` gives you — see [Concurrency model](#concurrency-model) below.

If the dictionary is **long-lived** (a singleton that lives for the application's lifetime and grows slowly), `System.Collections.Concurrent.ConcurrentDictionary` is the better choice — it scales better under read-heavy contention and the one-time bucket allocation doesn't matter.

## API

Mirrors `ConcurrentDictionary<TKey, TValue>`:

```csharp
public sealed class ConcurrentHeapSpanDictionary<TKey, TValue>
    : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable
    where TKey : notnull
{
    // Construction
    public ConcurrentHeapSpanDictionary();
    public ConcurrentHeapSpanDictionary(int capacity);
    public ConcurrentHeapSpanDictionary(int capacity, IEqualityComparer<TKey>? comparer);

    // State
    public int  Count { get; }
    public bool IsEmpty { get; }

    // Lookups / mutations
    public TValue this[TKey key] { get; set; }
    public bool    TryAdd(TKey key, TValue value);
    public bool    TryGetValue(TKey key, out TValue value);
    public bool    TryRemove(TKey key, out TValue value);
    public bool    TryUpdate(TKey key, TValue newValue, TValue comparisonValue);
    public TValue  GetOrAdd(TKey key, TValue value);
    public TValue  GetOrAdd(TKey key, Func<TKey, TValue> valueFactory);
    public TValue  AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateFactory);
    public bool    ContainsKey(TKey key);
    public void    Clear();

    // Snapshots
    public TKey[]                     ToKeysArray();
    public TValue[]                   ToValuesArray();
    public KeyValuePair<TKey,TValue>[] ToArray();
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator();

    public void Dispose();
}
```

`Keys` / `Values` shortcut properties are intentionally omitted — the explicit `ToKeysArray()` / `ToValuesArray()` make snapshot allocation visible at the call site.

## Concurrency model

A single `lock(_syncRoot)` guards every public method. Not striped, not lock-free.

Why: the value proposition is pooled bucket allocation, not beating `ConcurrentDictionary` on throughput. A coarse lock is provably correct, reuses the single-threaded `HeapSpanDictionary` logic 1:1, and keeps the implementation simple. If a future consumer profiles contention as the bottleneck, a separate `StripedConcurrentHeapSpanDictionary` (or lock-free variant) can ship alongside.

### Factory atomicity — stronger than `ConcurrentDictionary`

`GetOrAdd(key, valueFactory)` and `AddOrUpdate(key, addValue, updateFactory)` invoke their factory/update delegate **exactly once** per successful add/update.

`ConcurrentDictionary` documents that its factory *may* be called more than once under contention. The coarse lock here gives the stronger exactly-once guarantee for free. If you rely on this — e.g. because the factory has side effects such as opening a DB connection or logging — prefer this type.

### Enumeration semantics

`GetEnumerator()` / `foreach` iterates a **snapshot** taken under the lock. Iteration itself does not hold the lock and is safe under concurrent writes — but items added after you started iterating will not appear.

## Disposal

`Dispose()` returns the bucket array to `ArrayPool.Shared`. Required for the zero-alloc claim to hold across instances.

**Caller contract:** no concurrent operations may be in flight when `Dispose()` is called. Concurrent `Dispose` vs. operation is undefined behaviour.

Idempotent — calling `Dispose()` twice is a no-op.

## Example

```csharp
using ZeroAlloc.Collections;

// Populate concurrently
using var cache = new ConcurrentHeapSpanDictionary<int, string>(capacity: 16);
Parallel.For(0, 100, i => cache.TryAdd(i, $"item-{i}"));

// Exactly-once factory
var product = cache.GetOrAdd(id, k => LoadFromDb(k));

// Custom comparer
using var caseInsensitive = new ConcurrentHeapSpanDictionary<string, int>(
    capacity: 8,
    comparer: StringComparer.OrdinalIgnoreCase);
```

## See also

- [SpanDictionary](span-dictionary.md) — ref-struct variant, single-threaded
- [HeapSpanDictionary](span-dictionary.md#heapspandictionary) — class variant, single-threaded
