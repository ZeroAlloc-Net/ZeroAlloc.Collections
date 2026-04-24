# ConcurrentHeapSpanDictionary — Design

**Date:** 2026-04-24
**Status:** Approved (brainstorming → implementation plan next)

## Goal

Ship `ZeroAlloc.Collections.ConcurrentHeapSpanDictionary<TKey, TValue>` — a thread-safe, `ArrayPool`-backed, disposable hash map that drops in for `System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>` when the caller wants pooled bucket storage.

## Motivation

The ZeroAlloc ecosystem integration audit (see `../../../docs/INTEGRATION-MATRIX.md`) filed three P1 issues that want the ZeroAlloc pooled-dictionary pattern applied to existing consumers, all of which hold `ConcurrentDictionary` under concurrent access:

| Consumer | Field | Issue |
|---|---|---|
| `ZeroAlloc.Cache` | L1 hit-path store | [Cache#11](https://github.com/ZeroAlloc-Net/ZeroAlloc.Cache/issues/11) |
| `ZeroAlloc.Outbox.InMemory` | `_entries`, `_throughput` | [Outbox#16](https://github.com/ZeroAlloc-Net/ZeroAlloc.Outbox/issues/16) |
| `ZeroAlloc.Scheduling.InMemory` | `_entries` | [Scheduling#14](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/14) |

`ZeroAlloc.Collections` today ships `HeapSpanDictionary` (single-threaded). None of the three can drop that in without external locking — which would regress lock-free behaviour. This design adds the missing concurrent variant.

## Non-goals

- Beat `ConcurrentDictionary` on throughput. The value proposition is **pooled bucket allocation**, not throughput-per-thread.
- Lock-free or striped-locking implementation. Those are later, separate types if profiling ever justifies them.
- Change to `HeapSpanDictionary`'s API or behaviour.

## API shape

Mirrors `System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>` so the three consumer migrations are nearly mechanical. Not the `HeapSpanDictionary`-style `IDictionary<T,V>` shape — that lets per-method atomicity stay explicit (`GetOrAdd`, `AddOrUpdate`, `TryUpdate`) rather than buried behind an indexer.

```csharp
public sealed class ConcurrentHeapSpanDictionary<TKey, TValue> : IDisposable
    where TKey : notnull
{
    public ConcurrentHeapSpanDictionary();
    public ConcurrentHeapSpanDictionary(int capacity);
    public ConcurrentHeapSpanDictionary(int capacity, IEqualityComparer<TKey>? comparer);

    public int  Count { get; }
    public bool IsEmpty { get; }

    public TValue this[TKey key] { get; set; }

    public bool   TryAdd(TKey key, TValue value);
    public bool   TryGetValue(TKey key, out TValue value);
    public bool   TryRemove(TKey key, out TValue value);
    public bool   TryUpdate(TKey key, TValue newValue, TValue comparisonValue);
    public TValue GetOrAdd(TKey key, TValue value);
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory);
    public TValue AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateFactory);

    public void Clear();
    public bool ContainsKey(TKey key);

    public TKey[]                    ToKeysArray();      // snapshot
    public TValue[]                  ToValuesArray();    // snapshot
    public KeyValuePair<TKey,TValue>[] ToArray();        // snapshot

    public void Dispose();
}
```

Enumeration via `GetEnumerator()` returns a **snapshot** — matches `ConcurrentDictionary`. `Keys` / `Values` shortcut properties omitted in favour of explicit `ToKeysArray()` / `ToValuesArray()` so snapshot allocation is visible at the call site.

## Concurrency primitive

**Single `readonly object _syncRoot = new();` plus `lock(_syncRoot)` around every mutating and reading operation.** Not lock-free, not striped.

Rationale:
- Value prop is allocation, not throughput. `ConcurrentDictionary` already wins on throughput for contended workloads; users keep using it there.
- Correctness proof reduces to "coarse lock excludes all races" — no ABA, no torn reads, no resize-mid-lookup hazards.
- Implementation reuses the entire `HeapSpanDictionary` probing/resize logic wholesale — wrap each public method body in `lock`, done.
- If a future consumer profiles contention as the bottleneck, a separate `StripedConcurrentHeapSpanDictionary` can ship alongside. YAGNI today.

### Factory atomicity

`GetOrAdd(key, factory)` and `AddOrUpdate(key, addValue, updateFactory)` — the factory/updateFactory must be called **exactly once** per successful add/update under the lock. This differs from `ConcurrentDictionary`'s stated behaviour, which explicitly documents the factory *may* be called more than once under contention; our coarse lock gives us a stronger guarantee for free and it's a user-friendly one. Document the divergence in XML remarks.

## Resize semantics

Under the lock: rent new array from `ArrayPool<Entry>.Shared`, rehash, return old array. No reader coordination required because the lock excludes them.

Load factor and growth thresholds copied verbatim from `HeapSpanDictionary` — no tuning in this PR.

## Disposal contract

Caller must ensure no concurrent operations are in flight before calling `Dispose()`. Concurrent `Dispose` vs operations is undefined behaviour — same contract as `HeapSpanDictionary`. Document in XML remarks.

Idempotent disposal: a second call is a no-op.

## Scope

| Artefact | What | Size |
|---|---|---|
| `src/ZeroAlloc.Collections/ConcurrentHeapSpanDictionary.cs` | New type. Copy `HeapSpanDictionary` body; add `_syncRoot` field; wrap each public method in `lock(_syncRoot) { ... }`; add the `GetOrAdd`/`AddOrUpdate`/`TryUpdate` variants the `ConcurrentDictionary` shape requires. | ~350 LOC |
| `tests/ZeroAlloc.Collections.Tests/ConcurrentHeapSpanDictionaryTests.cs` | xUnit. Reuse the full `HeapSpanDictionaryTests` case matrix (Add / TryGetValue / Indexer / Remove / Clear / resize) adjusted for the new API, plus four concurrency tests: (a) 1000 concurrent `TryAdd` of distinct keys all succeed, (b) `GetOrAdd` factory invoked exactly once under 100 concurrent callers for the same key, (c) `AddOrUpdate` serialises against concurrent `TryGetValue`, (d) enumerator returns a consistent snapshot. | ~400 LOC |
| `tests/ZeroAlloc.Collections.Benchmarks/DictionaryBenchmarks.cs` | Add 3-way comparison on Fill-10k and MixedReadWrite workloads: `ConcurrentDictionary` · `Dictionary + lock` · `ConcurrentHeapSpanDictionary`. `[MemoryDiagnoser]` on. | +~60 LOC |
| `samples/ZeroAlloc.Collections.AotSmoke/Program.cs` | Add a `Create → TryAdd → TryGetValue → Dispose` smoke call to prove AOT publish. | +~5 LOC |
| `src/ZeroAlloc.Collections.Generators/Diagnostics/UndisposedPooledCollectionAnalyzer.cs` | Add `"ConcurrentHeapSpanDictionary"` to the recognised type name set so the "forgot to dispose" diagnostic fires. | +~1 line |
| `docs/concurrent-heap-span-dictionary.md` | New reference page: API, thread-safety contract, benchmark headline numbers, when to prefer this over `ConcurrentDictionary`. | ~100 LOC |

## Open questions (resolved during design)

- **API shape (mirror `ConcurrentDictionary` vs. `HeapSpanDictionary`)** — resolved: `ConcurrentDictionary` shape so consumer migrations are drop-in.
- **Concurrency primitive** — resolved: coarse lock.
- **Striped / lock-free later?** — parked; separate type if ever justified.
- **Factory atomicity divergence from `ConcurrentDictionary`** — resolved: document the stronger guarantee in XML remarks; it's user-friendly and free under a coarse lock.

## Out of scope

- Updating the three downstream consumers (Cache / Outbox / Scheduling) — each is a separate already-filed integration issue; they consume this type in follow-up PRs.
- Perf optimisation beyond correctness + allocation parity with `HeapSpanDictionary`.
- Persistence, ordering, observation events. It's a hash map.
