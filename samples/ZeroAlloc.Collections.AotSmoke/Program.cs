using System;
using ZeroAlloc.Collections;

// Exercise representative heap classes + ref struct primitives under
// PublishAot=true. Ref structs stay scoped to Main — they can't escape into
// async / closures, so we use them directly.

// 1. HeapPooledList<T>: growth across the initial capacity boundary
using var list = new HeapPooledList<int>(capacity: 2);
for (var i = 0; i < 10; i++) list.Add(i);
if (list.Count != 10) return Fail($"HeapPooledList.Count expected 10, got {list.Count}");
if (list[9] != 9) return Fail($"HeapPooledList[9] expected 9, got {list[9]}");
list.RemoveAt(5);
if (list.Count != 9 || list[5] != 6)
    return Fail($"HeapPooledList.RemoveAt broke: Count={list.Count}, [5]={list[5]}");

// 2. HeapRingBuffer<T>: wrap-around semantics
using var ring = new HeapRingBuffer<int>(capacity: 3);
if (!ring.TryWrite(1) || !ring.TryWrite(2) || !ring.TryWrite(3))
    return Fail("HeapRingBuffer.TryWrite rejected writes within capacity");
if (ring.TryWrite(4)) return Fail("HeapRingBuffer.TryWrite should have refused over capacity");
if (!ring.TryRead(out var r) || r != 1) return Fail($"HeapRingBuffer.TryRead expected 1, got {r}");
if (!ring.TryWrite(4)) return Fail("HeapRingBuffer.TryWrite should accept after a read");
if (!ring.TryPeek(out var p) || p != 2) return Fail($"HeapRingBuffer.TryPeek expected 2, got {p}");

// 3. PooledList<T> (ref struct): exercised in-scope since it cannot escape
{
    using var pooled = new PooledList<int>(capacity: 4);
    for (var i = 0; i < 5; i++) pooled.Add(i * 10);
    if (pooled.Count != 5) return Fail($"PooledList.Count expected 5, got {pooled.Count}");
    if (pooled[4] != 40) return Fail($"PooledList[4] expected 40, got {pooled[4]}");
}

// 4. ConcurrentHeapSpanDictionary<TKey, TValue>: TryAdd/TryGetValue/Dispose under AOT
using (var cdict = new ConcurrentHeapSpanDictionary<int, string>(capacity: 4))
{
    if (!cdict.TryAdd(1, "one")) return Fail("ConcurrentHeapSpanDictionary.TryAdd refused a new key");
    if (cdict.TryAdd(1, "ONE")) return Fail("ConcurrentHeapSpanDictionary.TryAdd should refuse a duplicate key");
    if (!cdict.TryGetValue(1, out var cv) || cv != "one")
        return Fail($"ConcurrentHeapSpanDictionary.TryGetValue expected \"one\", got \"{cv}\"");
    if (cdict.Count != 1) return Fail($"ConcurrentHeapSpanDictionary.Count expected 1, got {cdict.Count}");
}

Console.WriteLine("AOT smoke: PASS");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — {message}");
    return 1;
}
