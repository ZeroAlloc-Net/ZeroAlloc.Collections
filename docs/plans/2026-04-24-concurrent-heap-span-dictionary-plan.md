# ConcurrentHeapSpanDictionary Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (or superpowers:subagent-driven-development if running in-session) to implement this plan task-by-task.

**Goal:** Ship `ZeroAlloc.Collections.ConcurrentHeapSpanDictionary<TKey, TValue>` — a thread-safe, `ArrayPool`-backed, disposable hash map that drops in for `System.Collections.Concurrent.ConcurrentDictionary` when pooled bucket storage is wanted.

**Architecture:** New class next to `HeapSpanDictionary`. Internally reuses the same open-addressing / linear-probing logic with a single `object _syncRoot` and a `lock(_syncRoot)` around every public method. API surface mirrors `ConcurrentDictionary` (`TryAdd`, `GetOrAdd`, `AddOrUpdate`, …) — **not** `HeapSpanDictionary`'s `IDictionary` shape — so consumer migrations are mechanical. `ArrayPool<Entry>.Shared` rents bucket arrays; `Dispose()` returns them.

**Tech Stack:** C# 12 · `netstandard2.1;net8.0;net9.0` · xUnit · BenchmarkDotNet.

**Design:** [`./2026-04-24-concurrent-heap-span-dictionary-design.md`](2026-04-24-concurrent-heap-span-dictionary-design.md).

---

## Scope adjustments vs. design

- **Analyzer update dropped.** `UndisposedPooledCollectionAnalyzer.TrackedTypeNames` currently only lists ref-struct types (`PooledList`, `PooledStack`, `PooledQueue`, `RingBuffer`, `SpanDictionary`). None of the `Heap*` classes are tracked — including existing `HeapSpanDictionary`. Adding only the new type would break that implicit invariant. Expanding analyzer coverage to all heap classes is a separate change and out of scope for this PR.

All other deliverables from the design stand.

---

## Task 1: Worktree + baseline

**Goal:** isolated workspace; confirm the repo builds + tests pass before we start.

**Step 1:** Use superpowers:using-git-worktrees to set up a branch named `feature/concurrent-heap-span-dictionary` inside the worktree dir the repo uses (check `.worktrees/` or `worktrees/`; ask if neither exists).

**Step 2:** Baseline build and test on the worktree.

```bash
dotnet build ZeroAlloc.Collections.slnx -c Release
```
Expected: 0 warnings, 0 errors.

```bash
dotnet test ZeroAlloc.Collections.slnx -c Release --logger "console;verbosity=minimal"
```
Expected: all tests green.

**Step 3:** No commit yet.

---

## Task 2: Failing test — `TryAdd` + `TryGetValue`

**Files:**
- Create: `tests/ZeroAlloc.Collections.Tests/ConcurrentHeapSpanDictionaryTests.cs`

**Step 1:** Write the failing test file (minimal bootstrap).

```csharp
using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class ConcurrentHeapSpanDictionaryTests
{
    [Fact]
    public void TryAdd_NewKey_Succeeds_And_TryGetValue_Returns()
    {
        using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
        Assert.True(dict.TryAdd(1, "one"));
        Assert.True(dict.TryGetValue(1, out var value));
        Assert.Equal("one", value);
        Assert.Equal(1, dict.Count);
    }

    [Fact]
    public void TryAdd_DuplicateKey_ReturnsFalse()
    {
        using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
        Assert.True(dict.TryAdd(1, "one"));
        Assert.False(dict.TryAdd(1, "ONE"));
        Assert.True(dict.TryGetValue(1, out var value));
        Assert.Equal("one", value);
    }
}
```

**Step 2:** Verify it fails to compile.

```bash
dotnet test tests/ZeroAlloc.Collections.Tests --filter "FullyQualifiedName~ConcurrentHeapSpanDictionaryTests" -c Release 2>&1 | tail -5
```
Expected: `The type or namespace name 'ConcurrentHeapSpanDictionary' could not be found`.

**Step 3:** No commit (red).

---

## Task 3: Minimal type — constructor, `TryAdd`, `TryGetValue`, `Count`, `Dispose`

**Files:**
- Create: `src/ZeroAlloc.Collections/ConcurrentHeapSpanDictionary.cs`

**Step 1:** Scaffold the type. Start from `HeapSpanDictionary.cs` (copy file in place, rename class). The minimum for these two tests:

```csharp
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A thread-safe, <see cref="ArrayPool{T}"/>-backed hash map. Drop-in replacement for
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/> when pooled
/// bucket storage is wanted. Coarse-locked: every public method acquires a single
/// <c>lock(_syncRoot)</c> — no lock striping, no lock-free CAS.
/// </summary>
/// <remarks>
/// Disposal contract: the caller must ensure no concurrent operations are in flight
/// before calling <see cref="Dispose"/>. Concurrent <c>Dispose</c> vs. operations is
/// undefined behaviour.
/// </remarks>
public sealed class ConcurrentHeapSpanDictionary<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private enum EntryState : byte { Empty, Occupied, Deleted }

    private struct Entry
    {
        public TKey Key;
        public TValue Value;
        public int HashCode;
        public EntryState State;
    }

    private readonly object _syncRoot = new();
    private Entry[]? _entries;
    private int _count;
    private readonly EqualityComparer<TKey> _comparer;
    private readonly ArrayPool<Entry> _pool;

    private const int DefaultCapacity = 4;

    public ConcurrentHeapSpanDictionary() : this(DefaultCapacity) { }

    public ConcurrentHeapSpanDictionary(int capacity)
    {
        if (capacity < 1) capacity = DefaultCapacity;
        _pool = ArrayPool<Entry>.Shared;
        _entries = _pool.Rent(capacity);
        Array.Clear(_entries, 0, _entries.Length);
        _count = 0;
        _comparer = EqualityComparer<TKey>.Default;
    }

    public int Count { get { lock (_syncRoot) return _count; } }

    public bool TryAdd(TKey key, TValue value)
    {
        lock (_syncRoot) return TryInsertLocked(key, value, insertOnly: true);
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        lock (_syncRoot) return TryGetValueLocked(key, out value);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            var entries = _entries;
            if (entries is null) return;
            _entries = null;
            _count = 0;
            _pool.Return(entries, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<Entry>());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetHash(TKey key) => key is null ? 0 : key.GetHashCode() & 0x7FFFFFFF;

    // TryGetValueLocked + TryInsertLocked + GrowLocked: copy body from HeapSpanDictionary's
    // TryGetValue / TryInsert / Grow, dropping the `_entries!` null-forgiving noise (we know
    // it's non-null under the lock until Dispose).
    private bool TryGetValueLocked(TKey key, out TValue value) { /* copy from HeapSpanDictionary.TryGetValue */ }
    private bool TryInsertLocked(TKey key, TValue value, bool insertOnly) { /* copy from HeapSpanDictionary.TryInsert */ }
    private void GrowLocked() { /* copy from HeapSpanDictionary.Grow */ }
}
```

Paste the `TryGetValueLocked` / `TryInsertLocked` / `GrowLocked` bodies verbatim from `HeapSpanDictionary.TryGetValue` / `TryInsert` / `Grow`.

**Step 2:** Run Task 2's two tests.

```bash
dotnet test tests/ZeroAlloc.Collections.Tests --filter "FullyQualifiedName~ConcurrentHeapSpanDictionaryTests" -c Release 2>&1 | tail -5
```
Expected: `Passed! - Failed: 0, Passed: 2`.

**Step 3:** Commit.

```bash
git add src/ZeroAlloc.Collections/ConcurrentHeapSpanDictionary.cs tests/ZeroAlloc.Collections.Tests/ConcurrentHeapSpanDictionaryTests.cs
git commit -m "feat(collections): ConcurrentHeapSpanDictionary — TryAdd/TryGetValue"
```

---

## Task 4: `TryRemove`, `ContainsKey`, `Clear`

**Step 1:** Add failing tests to `ConcurrentHeapSpanDictionaryTests.cs`:

```csharp
[Fact]
public void TryRemove_ExistingKey_RemovesAndReturnsValue()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one");
    Assert.True(dict.TryRemove(1, out var value));
    Assert.Equal("one", value);
    Assert.False(dict.ContainsKey(1));
    Assert.Equal(0, dict.Count);
}

[Fact]
public void TryRemove_MissingKey_ReturnsFalse()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    Assert.False(dict.TryRemove(1, out _));
}

[Fact]
public void Clear_EmptiesDictionary()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one");
    dict.TryAdd(2, "two");
    dict.Clear();
    Assert.Equal(0, dict.Count);
    Assert.False(dict.ContainsKey(1));
}

[Fact]
public void ContainsKey_ReturnsAsExpected()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one");
    Assert.True(dict.ContainsKey(1));
    Assert.False(dict.ContainsKey(99));
}
```

**Step 2:** Run — expected: compile error on `TryRemove` / `ContainsKey` / `Clear`.

**Step 3:** Add the implementations:

```csharp
public bool TryRemove(TKey key, out TValue value)
{
    lock (_syncRoot)
    {
        if (TryGetValueLocked(key, out value))
        {
            RemoveLocked(key);
            return true;
        }
        return false;
    }
}

public bool ContainsKey(TKey key) { lock (_syncRoot) return TryGetValueLocked(key, out _); }

public void Clear()
{
    lock (_syncRoot)
    {
        if (_entries is not null) Array.Clear(_entries, 0, _entries.Length);
        _count = 0;
    }
}

// Copy HeapSpanDictionary.Remove body into RemoveLocked — drop the null-forgiving.
private bool RemoveLocked(TKey key) { /* copy from HeapSpanDictionary.Remove */ }
```

**Step 4:** Run all `ConcurrentHeapSpanDictionaryTests` — expected 6 passing.

**Step 5:** Commit: `feat(collections): ConcurrentHeapSpanDictionary — TryRemove/ContainsKey/Clear`.

---

## Task 5: Indexer + `IsEmpty` + `TryUpdate`

**Step 1:** Failing tests:

```csharp
[Fact]
public void Indexer_SetGet_And_OverwriteSemantics()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict[1] = "one";
    Assert.Equal("one", dict[1]);
    dict[1] = "ONE";
    Assert.Equal("ONE", dict[1]);
}

[Fact]
public void Indexer_Get_Missing_Throws()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    Assert.Throws<KeyNotFoundException>(() => dict[99]);
}

[Fact]
public void IsEmpty_ReflectsState()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    Assert.True(dict.IsEmpty);
    dict.TryAdd(1, "one");
    Assert.False(dict.IsEmpty);
}

[Fact]
public void TryUpdate_MatchingComparison_Succeeds()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one");
    Assert.True(dict.TryUpdate(1, "ONE", "one"));
    Assert.Equal("ONE", dict[1]);
}

[Fact]
public void TryUpdate_MismatchedComparison_Fails()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one");
    Assert.False(dict.TryUpdate(1, "ONE", "other"));
    Assert.Equal("one", dict[1]);
}
```

**Step 2:** Run — expect compile errors on the new surface.

**Step 3:** Implement:

```csharp
public bool IsEmpty { get { lock (_syncRoot) return _count == 0; } }

public TValue this[TKey key]
{
    get
    {
        lock (_syncRoot)
        {
            if (TryGetValueLocked(key, out var value)) return value;
            throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
        }
    }
    set { lock (_syncRoot) TryInsertLocked(key, value, insertOnly: false); }
}

public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
{
    lock (_syncRoot)
    {
        if (TryGetValueLocked(key, out var current) &&
            EqualityComparer<TValue>.Default.Equals(current, comparisonValue))
        {
            TryInsertLocked(key, newValue, insertOnly: false);
            return true;
        }
        return false;
    }
}
```

**Step 4:** Run all — 11 passing.

**Step 5:** Commit: `feat(collections): indexer, IsEmpty, TryUpdate`.

---

## Task 6: `GetOrAdd` (value + factory overloads)

**Step 1:** Failing tests:

```csharp
[Fact]
public void GetOrAdd_Value_NewKey_Adds()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    Assert.Equal("one", dict.GetOrAdd(1, "one"));
    Assert.Equal(1, dict.Count);
}

[Fact]
public void GetOrAdd_Value_ExistingKey_ReturnsExisting()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one");
    Assert.Equal("one", dict.GetOrAdd(1, "ONE"));
}

[Fact]
public void GetOrAdd_Factory_NewKey_InvokesFactoryOnce()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    int invocations = 0;
    var value = dict.GetOrAdd(1, k => { invocations++; return "one"; });
    Assert.Equal("one", value);
    Assert.Equal(1, invocations);
}

[Fact]
public void GetOrAdd_Factory_ExistingKey_DoesNotInvokeFactory()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one");
    int invocations = 0;
    var value = dict.GetOrAdd(1, k => { invocations++; return "NEW"; });
    Assert.Equal("one", value);
    Assert.Equal(0, invocations);
}
```

**Step 2:** Run — compile error.

**Step 3:** Implement:

```csharp
public TValue GetOrAdd(TKey key, TValue value)
{
    lock (_syncRoot)
    {
        if (TryGetValueLocked(key, out var existing)) return existing;
        TryInsertLocked(key, value, insertOnly: true);
        return value;
    }
}

public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
{
    if (valueFactory is null) throw new ArgumentNullException(nameof(valueFactory));
    lock (_syncRoot)
    {
        if (TryGetValueLocked(key, out var existing)) return existing;
        var value = valueFactory(key);
        TryInsertLocked(key, value, insertOnly: true);
        return value;
    }
}
```

**Step 4:** Run — 15 passing.

**Step 5:** Commit: `feat(collections): GetOrAdd (value + factory)`.

---

## Task 7: `AddOrUpdate`

**Step 1:** Failing tests:

```csharp
[Fact]
public void AddOrUpdate_NewKey_AddsWithAddValue()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, int>(4);
    Assert.Equal(10, dict.AddOrUpdate(1, 10, (k, v) => v * 2));
    Assert.Equal(10, dict[1]);
}

[Fact]
public void AddOrUpdate_ExistingKey_InvokesUpdateFactory()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, int>(4);
    dict.TryAdd(1, 10);
    Assert.Equal(20, dict.AddOrUpdate(1, -1, (k, v) => v * 2));
    Assert.Equal(20, dict[1]);
}
```

**Step 2:** Run — compile error.

**Step 3:** Implement:

```csharp
public TValue AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateFactory)
{
    if (updateFactory is null) throw new ArgumentNullException(nameof(updateFactory));
    lock (_syncRoot)
    {
        if (TryGetValueLocked(key, out var existing))
        {
            var updated = updateFactory(key, existing);
            TryInsertLocked(key, updated, insertOnly: false);
            return updated;
        }
        TryInsertLocked(key, addValue, insertOnly: true);
        return addValue;
    }
}
```

**Step 4:** Run — 17 passing.

**Step 5:** Commit: `feat(collections): AddOrUpdate`.

---

## Task 8: Snapshot APIs (`ToKeysArray`, `ToValuesArray`, `ToArray`, `GetEnumerator`)

**Step 1:** Failing tests:

```csharp
[Fact]
public void ToKeysArray_ReturnsSnapshot()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one"); dict.TryAdd(2, "two");
    var keys = dict.ToKeysArray();
    Assert.Equal(2, keys.Length);
    Assert.Contains(1, keys);
    Assert.Contains(2, keys);
}

[Fact]
public void ToValuesArray_ReturnsSnapshot()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one"); dict.TryAdd(2, "two");
    var values = dict.ToValuesArray();
    Assert.Equal(2, values.Length);
    Assert.Contains("one", values);
    Assert.Contains("two", values);
}

[Fact]
public void ToArray_ReturnsSnapshotOfPairs()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one");
    var arr = dict.ToArray();
    Assert.Single(arr);
    Assert.Equal(1, arr[0].Key);
    Assert.Equal("one", arr[0].Value);
}

[Fact]
public void GetEnumerator_IteratesAllEntries()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one"); dict.TryAdd(2, "two");
    var seen = new HashSet<int>();
    foreach (var kv in dict) seen.Add(kv.Key);
    Assert.Equal(new[] { 1, 2 }.ToHashSet(), seen);
}
```

**Step 2:** Run — compile errors.

**Step 3:** Implement — each snapshot method walks `_entries` under the lock and materialises an array:

```csharp
public TKey[] ToKeysArray()
{
    lock (_syncRoot)
    {
        var result = new TKey[_count];
        int i = 0;
        for (int j = 0; j < _entries!.Length; j++)
            if (_entries[j].State == EntryState.Occupied)
                result[i++] = _entries[j].Key;
        return result;
    }
}

public TValue[] ToValuesArray()
{
    lock (_syncRoot)
    {
        var result = new TValue[_count];
        int i = 0;
        for (int j = 0; j < _entries!.Length; j++)
            if (_entries[j].State == EntryState.Occupied)
                result[i++] = _entries[j].Value;
        return result;
    }
}

public KeyValuePair<TKey, TValue>[] ToArray()
{
    lock (_syncRoot)
    {
        var result = new KeyValuePair<TKey, TValue>[_count];
        int i = 0;
        for (int j = 0; j < _entries!.Length; j++)
            if (_entries[j].State == EntryState.Occupied)
                result[i++] = new KeyValuePair<TKey, TValue>(_entries[j].Key, _entries[j].Value);
        return result;
    }
}

public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
{
    // Snapshot semantics: materialise under the lock, iterate outside.
    var snapshot = ToArray();
    foreach (var kv in snapshot) yield return kv;
}
```

`GetEnumerator` returns a pre-snapshotted array's enumerator so iteration doesn't hold the lock.

**Step 4:** Run — 21 passing.

**Step 5:** Commit: `feat(collections): snapshot APIs (keys/values/array/enumerator)`.

---

## Task 9: Dispose idempotency + resize-through-growth test

**Step 1:** Failing tests:

```csharp
[Fact]
public void Dispose_CanBeCalledTwice()
{
    var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
    dict.TryAdd(1, "one");
    dict.Dispose();
    dict.Dispose(); // should not throw
}

[Fact]
public void Grow_FromSmallInitialCapacity_PreservesAllEntries()
{
    // Seed with enough entries to force 2–3 Grow cycles from the default capacity.
    using var dict = new ConcurrentHeapSpanDictionary<int, int>(4);
    for (int i = 0; i < 50; i++) Assert.True(dict.TryAdd(i, i * 10));
    for (int i = 0; i < 50; i++)
    {
        Assert.True(dict.TryGetValue(i, out var v));
        Assert.Equal(i * 10, v);
    }
    Assert.Equal(50, dict.Count);
}
```

**Step 2:** Run — `Grow` test likely passes already because `TryInsertLocked` delegates to the reused `GrowLocked`. `Dispose_CanBeCalledTwice` should already work (idempotent via the `if (entries is null) return` guard).

If either fails, fix in the implementation.

**Step 3:** Commit: `test(collections): dispose idempotency + grow-under-fill`.

---

## Task 10: Concurrency test 1 — 1000 concurrent distinct-key `TryAdd`

**Step 1:** Add test:

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

[Fact]
public async Task TryAdd_ConcurrentDistinctKeys_AllAdded()
{
    const int N = 1000;
    using var dict = new ConcurrentHeapSpanDictionary<int, int>(8);
    using var barrier = new Barrier(N);

    var tasks = Enumerable.Range(0, N).Select(i => Task.Run(() =>
    {
        barrier.SignalAndWait();
        Assert.True(dict.TryAdd(i, i));
    })).ToArray();

    await Task.WhenAll(tasks);

    Assert.Equal(N, dict.Count);
    for (int i = 0; i < N; i++)
        Assert.True(dict.TryGetValue(i, out var v) && v == i);
}
```

**Step 2:** Run.

```bash
dotnet test tests/ZeroAlloc.Collections.Tests --filter "FullyQualifiedName~TryAdd_ConcurrentDistinctKeys" -c Release
```
Expected: PASS. The coarse lock serialises the writes; all 1000 distinct keys land.

If flaky, add `[Repeat(10)]` or run in a loop inside the test to stress.

**Step 3:** Commit: `test(collections): concurrent TryAdd under Barrier`.

---

## Task 11: Concurrency test 2 — `GetOrAdd` factory invoked exactly once

**Step 1:** Add:

```csharp
[Fact]
public async Task GetOrAdd_Factory_InvokedExactlyOnce_UnderContention()
{
    const int N = 100;
    using var dict = new ConcurrentHeapSpanDictionary<int, int>(4);
    using var barrier = new Barrier(N);
    int invocations = 0;

    var tasks = Enumerable.Range(0, N).Select(_ => Task.Run(() =>
    {
        barrier.SignalAndWait();
        return dict.GetOrAdd(42, k => { Interlocked.Increment(ref invocations); return 1337; });
    })).ToArray();

    await Task.WhenAll(tasks);

    Assert.Equal(1, invocations);       // coarse lock guarantees exactly-once
    Assert.Equal(1337, dict[42]);
}
```

**Step 2:** Run. PASS — the coarse lock gives us the stronger "exactly once" guarantee (vs. ConcurrentDictionary which may call the factory multiple times).

**Step 3:** Commit: `test(collections): GetOrAdd factory exactly-once under contention`.

---

## Task 12: Concurrency test 3 — `AddOrUpdate` serialises against `TryGetValue`

**Step 1:** Add:

```csharp
[Fact]
public async Task AddOrUpdate_SerialisesAgainst_ConcurrentTryGetValue()
{
    const int Iterations = 5_000;
    using var dict = new ConcurrentHeapSpanDictionary<int, int>(4);
    dict.TryAdd(1, 0);

    using var stop = new CancellationTokenSource();

    var writer = Task.Run(() =>
    {
        for (int i = 1; i <= Iterations; i++)
            dict.AddOrUpdate(1, -1, (k, v) => i);  // always monotonically increasing
    });

    var reader = Task.Run(() =>
    {
        int lastSeen = 0;
        while (!writer.IsCompleted)
        {
            if (dict.TryGetValue(1, out var v))
            {
                Assert.True(v >= lastSeen, $"Value went backwards from {lastSeen} to {v}");
                lastSeen = v;
            }
        }
    });

    await Task.WhenAll(writer, reader);
    Assert.Equal(Iterations, dict[1]);
}
```

**Step 2:** Run. PASS.

**Step 3:** Commit: `test(collections): AddOrUpdate vs. concurrent TryGetValue`.

---

## Task 13: Concurrency test 4 — enumerator returns consistent snapshot

**Step 1:** Add:

```csharp
[Fact]
public async Task GetEnumerator_ReturnsConsistentSnapshot_EvenUnderConcurrentWrites()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, int>(8);
    for (int i = 0; i < 20; i++) dict.TryAdd(i, i);

    using var stop = new CancellationTokenSource();

    var writer = Task.Run(() =>
    {
        int i = 20;
        while (!stop.IsCancellationRequested)
        {
            dict.TryAdd(i, i);
            dict.TryRemove(i, out _);
            i++;
        }
    });

    for (int attempt = 0; attempt < 50; attempt++)
    {
        var snapshot = dict.ToArray();
        // Every snapshot value must match its key.
        foreach (var kv in snapshot)
            Assert.Equal(kv.Key, kv.Value);
        // No exception, no torn reads.
    }

    stop.Cancel();
    await writer;
}
```

**Step 2:** Run. PASS — snapshot is materialised under the lock.

**Step 3:** Commit: `test(collections): enumerator snapshot consistency under writes`.

---

## Task 14: Benchmark — 3-way comparison

**Files:**
- Modify: `tests/ZeroAlloc.Collections.Benchmarks/DictionaryBenchmarks.cs`

**Step 1:** Append benchmarks:

```csharp
using System.Collections.Concurrent;

[Benchmark]
public int ConcurrentDictionary_Fill()
{
    var dict = new ConcurrentDictionary<int, int>();
    for (int i = 0; i < N; i++) dict[i] = i * 10;
    int sum = 0;
    foreach (var kv in dict) sum += kv.Value;
    return sum;
}

[Benchmark]
public int DictionaryPlusLock_Fill()
{
    var dict = new Dictionary<int, int>();
    var sync = new object();
    for (int i = 0; i < N; i++) lock (sync) dict[i] = i * 10;
    int sum = 0;
    lock (sync) foreach (var kv in dict) sum += kv.Value;
    return sum;
}

[Benchmark]
public int ConcurrentHeapSpanDictionary_Fill()
{
    using var dict = new ConcurrentHeapSpanDictionary<int, int>(N);
    for (int i = 0; i < N; i++) dict.TryAdd(i, i * 10);
    int sum = 0;
    foreach (var kv in dict) sum += kv.Value;
    return sum;
}
```

**Step 2:** Build the benchmarks project.

```bash
dotnet build tests/ZeroAlloc.Collections.Benchmarks -c Release
```
Expected: 0 errors.

**Step 3:** Do not run the full benchmark here (slow). Just confirm it compiles. A fast sanity run can be done manually later:

```bash
# Manual sanity (optional, slow)
dotnet run --project tests/ZeroAlloc.Collections.Benchmarks -c Release --filter "*Dictionary*"
```

**Step 4:** Commit: `bench(collections): 3-way Fill comparison for ConcurrentHeapSpanDictionary`.

---

## Task 15: AotSmoke update

**Files:**
- Modify: `samples/ZeroAlloc.Collections.AotSmoke/Program.cs`

**Step 1:** Append a smoke section before the final `Console.WriteLine("AOT smoke: PASS");` line:

```csharp
// 4. ConcurrentHeapSpanDictionary<TKey, TValue>: TryAdd + TryGetValue under AOT.
using (var cdict = new ConcurrentHeapSpanDictionary<int, string>(capacity: 4))
{
    if (!cdict.TryAdd(1, "one")) return Fail("ConcurrentHeapSpanDictionary.TryAdd refused a new key");
    if (cdict.TryAdd(1, "ONE")) return Fail("ConcurrentHeapSpanDictionary.TryAdd should refuse a duplicate key");
    if (!cdict.TryGetValue(1, out var cv) || cv != "one")
        return Fail($"ConcurrentHeapSpanDictionary.TryGetValue expected \"one\", got \"{cv}\"");
}
```

**Step 2:** Build AOT publish.

```bash
dotnet publish samples/ZeroAlloc.Collections.AotSmoke -r win-x64 -c Release 2>&1 | tail -15
```
Expected: `Build succeeded`, published exe exists.

**Step 3:** Run the published exe.

```bash
./samples/ZeroAlloc.Collections.AotSmoke/bin/Release/net*/win-x64/publish/ZeroAlloc.Collections.AotSmoke.exe
```
Expected: `AOT smoke: PASS`.

**Step 4:** Commit: `chore(collections): AOT smoke coverage for ConcurrentHeapSpanDictionary`.

---

## Task 16: Reference docs page

**Files:**
- Create: `docs/concurrent-heap-span-dictionary.md`

**Step 1:** Content (~100 LOC):

```markdown
---
id: concurrent-heap-span-dictionary
title: ConcurrentHeapSpanDictionary
sidebar_position: 6
---

# ConcurrentHeapSpanDictionary

`ConcurrentHeapSpanDictionary<TKey, TValue>` is a thread-safe, `ArrayPool`-backed, disposable hash map. Drop-in replacement for `ConcurrentDictionary<TKey, TValue>` when pooled bucket storage is wanted.

## When to use

- You hold a **short-lived** concurrent dictionary (per-request scope, per-session accumulator, per-time-window bucket).
- Allocation pressure from bucket arrays shows up in your profile.
- You don't need throughput beyond what a coarse lock gives you — see "Concurrency model" below.

If the dictionary is **long-lived** (a singleton that lives for the app's lifetime and grows slowly), `System.Collections.Concurrent.ConcurrentDictionary` is the better choice — it scales better under read-heavy contention and the one-time bucket allocation doesn't matter.

## API

Mirrors `ConcurrentDictionary<TKey, TValue>`:

- `TryAdd`, `TryGetValue`, `TryRemove`, `TryUpdate`
- `GetOrAdd(key, value)`, `GetOrAdd(key, valueFactory)`
- `AddOrUpdate(key, addValue, updateFactory)`
- `Count`, `IsEmpty`, `ContainsKey`
- `this[TKey]` indexer
- `ToArray()`, `ToKeysArray()`, `ToValuesArray()` — snapshot copies
- `GetEnumerator()` — iterates a snapshot; safe under concurrent writes
- `Clear`
- `IDisposable.Dispose()` — returns the bucket array to `ArrayPool.Shared`

## Concurrency model

A single `lock(_syncRoot)` guards every public method. Not lock-striped, not lock-free.

Why: the value proposition is pooled bucket allocation, not beating `ConcurrentDictionary` on throughput. A coarse lock is provably correct, matches the single-threaded `HeapSpanDictionary` logic 1:1, and keeps the implementation simple.

### Factory atomicity — stronger than `ConcurrentDictionary`

`GetOrAdd(key, valueFactory)` and `AddOrUpdate(key, addValue, updateFactory)` invoke their factory exactly once per successful add/update.

`ConcurrentDictionary` documents that its factory *may* be called more than once under contention. Our coarse lock gives the stronger exactly-once guarantee for free. If you rely on this — for example, because the factory has side effects — prefer this type.

## Disposal

`Dispose()` returns the bucket array to `ArrayPool.Shared`. Required for the zero-alloc claim.

Caller contract: no concurrent operations may be in flight when `Dispose()` is called. Concurrent `Dispose` vs. operation is undefined behaviour.

Idempotent — calling `Dispose()` twice is a no-op.

## Example

```csharp
using ZeroAlloc.Collections;

using var cache = new ConcurrentHeapSpanDictionary<int, string>(capacity: 16);

// Classic concurrent populate
Parallel.For(0, 100, i => cache.TryAdd(i, $"item-{i}"));

// Exactly-once factory
var product = cache.GetOrAdd(id, k => LoadFromDb(k));
```

## See also

- [SpanDictionary](span-dictionary.md) — ref-struct variant, single-threaded
- [HeapSpanDictionary](span-dictionary.md#heapspandictionary) — class variant, single-threaded
```

**Step 2:** Verify the file renders in Docusaurus locally (if the project serves docs) or just check for frontmatter validity:

```bash
head -5 docs/concurrent-heap-span-dictionary.md
```
Expected: well-formed frontmatter.

**Step 3:** Commit: `docs(collections): reference page for ConcurrentHeapSpanDictionary`.

---

## Task 17: Full suite run + push + PR

**Step 1:** Run the full test + build.

```bash
dotnet build ZeroAlloc.Collections.slnx -c Release
dotnet test  ZeroAlloc.Collections.slnx -c Release --logger "console;verbosity=minimal"
```
Expected: 0 warnings, 0 errors, all tests pass (new 15+ tests plus original suite).

**Step 2:** Push the branch.

```bash
git push -u origin feature/concurrent-heap-span-dictionary
```

**Step 3:** Open the PR.

```bash
gh pr create \
  --title "feat(collections): ConcurrentHeapSpanDictionary — thread-safe pooled hash map" \
  --body "$(cat <<'EOF'
## Summary

Adds `ZeroAlloc.Collections.ConcurrentHeapSpanDictionary<TKey, TValue>` — a coarse-locked, `ArrayPool`-backed, disposable hash map that drops in for `ConcurrentDictionary` when pooled bucket storage is wanted.

Unblocks three downstream ecosystem integration issues: [Cache#11](https://github.com/ZeroAlloc-Net/ZeroAlloc.Cache/issues/11), [Outbox#16](https://github.com/ZeroAlloc-Net/ZeroAlloc.Outbox/issues/16), [Scheduling#14](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/14) — all of which hold `ConcurrentDictionary` today and need a thread-safe pooled variant.

## Design

- API mirrors `ConcurrentDictionary<TKey, TValue>` — drop-in for downstream migrations.
- Coarse `lock(_syncRoot)` on every public method. Not striped, not lock-free. YAGNI; revisit if profiling demands.
- Exactly-once factory atomicity for `GetOrAdd(factory)` and `AddOrUpdate` — stronger than `ConcurrentDictionary`.
- Reuses the open-addressing / linear-probing body from `HeapSpanDictionary`; the two types share no code today but the second would be the first candidate for extraction if a third concurrent variant appears.

Full design: `docs/plans/2026-04-24-concurrent-heap-span-dictionary-design.md`.
Plan: `docs/plans/2026-04-24-concurrent-heap-span-dictionary-plan.md`.

## What's in

- New type `ConcurrentHeapSpanDictionary<TKey, TValue>` in `src/ZeroAlloc.Collections/`.
- 15+ unit tests including 4 concurrency tests (distinct-key `TryAdd` under Barrier, `GetOrAdd` factory exactly-once, `AddOrUpdate` vs. concurrent `TryGetValue`, enumerator snapshot consistency).
- 3-way benchmark in `DictionaryBenchmarks`: `ConcurrentDictionary`, `Dictionary + lock`, `ConcurrentHeapSpanDictionary`.
- `AotSmoke` covers the new type.
- Reference docs page.

## Analyzer

`UndisposedPooledCollectionAnalyzer` is not updated in this PR — it currently tracks only ref-struct types (`PooledList`, `SpanDictionary`, …); none of the `Heap*` classes are tracked, including existing `HeapSpanDictionary`. Expanding coverage to heap classes is a separate change.

## Test plan

- [ ] `dotnet test ZeroAlloc.Collections.slnx -c Release` — all green.
- [ ] `dotnet publish samples/ZeroAlloc.Collections.AotSmoke -r win-x64 -c Release && ./.../ZeroAlloc.Collections.AotSmoke.exe` — prints `AOT smoke: PASS`.
- [ ] `dotnet run --project tests/ZeroAlloc.Collections.Benchmarks -c Release --filter "*Fill*"` — benchmarks run to completion with `[MemoryDiagnoser]` output; `ConcurrentHeapSpanDictionary` Gen0 column equal or lower than `ConcurrentDictionary` on short-lived workloads.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected output: URL to the new PR. Capture it.

**Step 4:** No commit in this task (the PR is the deliverable).

---

## Task 18: Close out

**Step 1:** Add a comment to each of the three downstream issues ([Cache#11](https://github.com/ZeroAlloc-Net/ZeroAlloc.Cache/issues/11), [Outbox#16](https://github.com/ZeroAlloc-Net/ZeroAlloc.Outbox/issues/16), [Scheduling#14](https://github.com/ZeroAlloc-Net/ZeroAlloc.Scheduling/issues/14)) linking the Collections PR and noting that the migration can proceed once it's merged.

```bash
for issue_url in \
  "ZeroAlloc-Net/ZeroAlloc.Cache#11" \
  "ZeroAlloc-Net/ZeroAlloc.Outbox#16" \
  "ZeroAlloc-Net/ZeroAlloc.Scheduling#14"; do
  repo="${issue_url%#*}"; num="${issue_url##*#}"
  gh issue comment --repo "$repo" "$num" --body "Blocker resolved: \`ConcurrentHeapSpanDictionary<K,V>\` is in-flight in ZeroAlloc-Net/ZeroAlloc.Collections#<PR>. Migration can proceed once that lands."
done
```

**Step 2:** Use superpowers:finishing-a-development-branch to close out the worktree once the PR is ready for review.

---

## Out of scope

- Updating the three downstream consumer repos (Cache / Outbox / Scheduling) — each has its own filed integration issue and follow-up PR.
- Striped / lock-free variants. Separate types if ever justified by profiling.
- Changes to `HeapSpanDictionary` — left alone.
- `UndisposedPooledCollectionAnalyzer` expansion to heap classes — separate concern.

## Why this plan looks TDD-y

Each task writes a failing test, runs it, implements the minimal code, reruns, and commits. 17 tasks = 17 small commits = a clean PR timeline that a reviewer can walk through linearly. No giant "added the type + 15 tests" commit.
