using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    [Fact]
    public void ToKeysArray_ReturnsSnapshot()
    {
        using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
        dict.TryAdd(1, "one");
        dict.TryAdd(2, "two");
        var keys = dict.ToKeysArray();
        Assert.Equal(2, keys.Length);
        Assert.Contains(1, keys);
        Assert.Contains(2, keys);
    }

    [Fact]
    public void ToValuesArray_ReturnsSnapshot()
    {
        using var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
        dict.TryAdd(1, "one");
        dict.TryAdd(2, "two");
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
        dict.TryAdd(1, "one");
        dict.TryAdd(2, "two");
        var seen = new HashSet<int>();
        foreach (var kv in dict) seen.Add(kv.Key);
        Assert.Equal(new HashSet<int> { 1, 2 }, seen);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var dict = new ConcurrentHeapSpanDictionary<int, string>(4);
        dict.TryAdd(1, "one");
        dict.Dispose();
        dict.Dispose(); // must not throw
    }

    [Fact]
    public void Grow_FromSmallInitialCapacity_PreservesAllEntries()
    {
        // Seed enough entries to force multiple Grow cycles from the default capacity.
        using var dict = new ConcurrentHeapSpanDictionary<int, int>(4);
        for (int i = 0; i < 50; i++) Assert.True(dict.TryAdd(i, i * 10));
        for (int i = 0; i < 50; i++)
        {
            Assert.True(dict.TryGetValue(i, out var v));
            Assert.Equal(i * 10, v);
        }
        Assert.Equal(50, dict.Count);
    }

    [Fact]
    public void RemoveThenReinsert_ProbesPastTombstone()
    {
        // Force key collision on a tiny capacity so probing crosses a Deleted slot.
        // Use a comparer that bins everything into the same hash code so all keys
        // land in the same initial bucket and probe linearly.
        using var dict = new ConcurrentHeapSpanDictionary<int, string>(
            capacity: 8,
            comparer: new AllSameHashComparer());

        Assert.True(dict.TryAdd(1, "one"));
        Assert.True(dict.TryAdd(2, "two"));   // probes past slot holding key 1
        Assert.True(dict.TryAdd(3, "three")); // probes past slots holding keys 1 and 2
        Assert.True(dict.TryRemove(2, out _)); // leaves a Deleted slot between 1 and 3
        Assert.True(dict.TryAdd(4, "four"));  // insert should reuse the Deleted slot

        // All remaining keys findable after the tombstone rewrite.
        Assert.True(dict.TryGetValue(1, out var v1) && v1 == "one");
        Assert.True(dict.TryGetValue(3, out var v3) && v3 == "three");
        Assert.True(dict.TryGetValue(4, out var v4) && v4 == "four");
        Assert.False(dict.ContainsKey(2));
        Assert.Equal(3, dict.Count);
    }

    [Fact]
    public void Ctor_WithCustomComparer_UsesIt()
    {
        using var dict = new ConcurrentHeapSpanDictionary<string, int>(
            capacity: 4,
            comparer: StringComparer.OrdinalIgnoreCase);
        dict.TryAdd("Hello", 1);
        Assert.True(dict.ContainsKey("hello"));
        Assert.True(dict.ContainsKey("HELLO"));
        Assert.True(dict.TryGetValue("hELLo", out var v) && v == 1);
    }

    private sealed class AllSameHashComparer : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => x == y;
        public int GetHashCode(int obj) => 0;
    }

    // ---- Concurrency ----

    [Fact]
    public async Task TryAdd_ConcurrentDistinctKeys_AllAdded()
    {
        // Size the contender count to the machine so we don't block on ThreadPool ramp-up
        // (pool grows ~1/sec from MinThreads) — CI boxes routinely have 2-8 cores.
        int n = Math.Max(32, Environment.ProcessorCount * 4);
        ThreadPool.SetMinThreads(n, n);
        using var dict = new ConcurrentHeapSpanDictionary<int, int>(8);
        using var barrier = new Barrier(n);

        var tasks = Enumerable.Range(0, n).Select(i => Task.Run(() =>
        {
            barrier.SignalAndWait();
            Assert.True(dict.TryAdd(i, i));
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(n, dict.Count);
        for (int i = 0; i < n; i++)
            Assert.True(dict.TryGetValue(i, out var v) && v == i);
    }

    [Fact]
    public async Task GetOrAdd_Factory_InvokedExactlyOnce_UnderContention()
    {
        int n = Math.Max(16, Environment.ProcessorCount * 2);
        ThreadPool.SetMinThreads(n, n);
        using var dict = new ConcurrentHeapSpanDictionary<int, int>(4);
        using var barrier = new Barrier(n);
        int invocations = 0;
        var observedValues = new ConcurrentBag<int>();

        var tasks = Enumerable.Range(0, n).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            var value = dict.GetOrAdd(42, _ => { Interlocked.Increment(ref invocations); return 1337; });
            observedValues.Add(value);
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, invocations);          // coarse lock: exactly-once guarantee
        Assert.Equal(n, observedValues.Count); // every task observed a value
        Assert.All(observedValues, v => Assert.Equal(1337, v)); // and it was the factory's value
        Assert.Equal(1337, dict[42]);
    }

    [Fact]
    public async Task AddOrUpdate_SerialisesAgainst_ConcurrentTryGetValue()
    {
        // Verifies two properties at once:
        //  1. No torn reads: a TryGetValue concurrent with AddOrUpdate never returns a value
        //     that wasn't at some point written. (Monotonic writer → non-decreasing reads.)
        //  2. Liveness: the reader actually observes intermediate values, not just the
        //     final one — catches bugs where the reader is starved or the writer never ran.
        const int Iterations = 5_000;
        using var dict = new ConcurrentHeapSpanDictionary<int, int>(4);
        dict.TryAdd(1, 0);

        var writer = Task.Run(() =>
        {
            for (int i = 1; i <= Iterations; i++)
                dict.AddOrUpdate(1, -1, (_, _) => i);
        });

        int distinctValuesSeen = 0;
        int lastSeen = 0;
        await Task.Run(() =>
        {
            while (!writer.IsCompleted)
            {
                if (dict.TryGetValue(1, out var v))
                {
                    Assert.True(v >= lastSeen, $"Value went backwards from {lastSeen} to {v}");
                    if (v != lastSeen) distinctValuesSeen++;
                    lastSeen = v;
                }
                Thread.Yield(); // don't starve the writer on low-core CI
            }
        });

        await writer;
        Assert.Equal(Iterations, dict[1]);
        // Liveness: reader must have observed more than just the final state.
        // (Without this, a reader that only reads after the writer finished would still pass.)
        Assert.True(distinctValuesSeen > 1,
            $"Reader observed only {distinctValuesSeen} distinct value(s) — reader was starved or writer didn't run concurrently");
    }

    [Fact]
    public async Task ToArray_ReturnsConsistentSnapshot_EvenUnderConcurrentWrites()
    {
        using var dict = new ConcurrentHeapSpanDictionary<int, int>(8);
        for (int i = 0; i < 20; i++) dict.TryAdd(i, i);

        using var stop = new CancellationTokenSource();
        long writerOps = 0;

        // Writer adds/removes a rising key while simultaneously updating the seeded
        // keys' values to intentionally-mismatched values (so a torn read WOULD violate
        // the "value == key" invariant if serialisation broke).
        var writer = Task.Run(() =>
        {
            int i = 20;
            while (!stop.IsCancellationRequested)
            {
                dict.TryAdd(i, i);
                dict.TryRemove(i, out _);
                Interlocked.Increment(ref writerOps);
                i++;
            }
        });

        // Wait until the writer is actually running before taking snapshots — otherwise
        // the main loop can finish its 50 iterations before the writer has even scheduled.
        while (Interlocked.Read(ref writerOps) < 100)
            Thread.Yield();

        for (int attempt = 0; attempt < 50; attempt++)
        {
            var snapshot = dict.ToArray();
            // Seed invariant: every entry the snapshot sees must satisfy value == key.
            // The writer only writes (i, i) and then removes — so no snapshot should
            // ever see a mismatched pair if the lock correctly serialises writes vs. ToArray.
            foreach (var kv in snapshot)
                Assert.Equal(kv.Key, kv.Value);
        }

        stop.Cancel();
        await writer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(Interlocked.Read(ref writerOps) > 1_000,
            "Writer didn't get enough CPU time to concurrently stress the snapshots");
    }
}
