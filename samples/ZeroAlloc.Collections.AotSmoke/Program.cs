using System;
using ZeroAlloc.Collections;
using ZeroAlloc.Collections.AotSmoke;

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
    if (!cdict.TryGetValue(1, out var cv) || !string.Equals(cv, "one", StringComparison.Ordinal))
        return Fail($"ConcurrentHeapSpanDictionary.TryGetValue expected \"one\", got \"{cv}\"");
    if (cdict.Count != 1) return Fail($"ConcurrentHeapSpanDictionary.Count expected 1, got {cdict.Count}");
}

// Value-type shapes. dotnet/runtime#134799: under NativeAOT a generic path can hang or misbehave
// for Nullable<T> while working for strings and plain ints, so each generic path below runs with
// primitive, enum, user struct, nullable primitive and nullable struct arguments.

// 5. HeapPooledList<T> over a tuple that contains a reference: RemoveAt clears the freed slot
using (var tuples = new HeapPooledList<(string Name, int Value)>(capacity: 2))
{
    tuples.Add(("a", 1));
    tuples.Add(("b", 2));
    tuples.Add(("c", 3));
    tuples.RemoveAt(1);
    if (tuples.Count != 2) return Fail($"HeapPooledList<(string,int)>.Count expected 2, got {tuples.Count}");
    if (tuples[1] != ("c", 3)) return Fail($"HeapPooledList<(string,int)>[1] expected (c, 3), got {tuples[1]}");
    if (tuples.IndexOf(("c", 3)) != 1)
        return Fail($"HeapPooledList<(string,int)>.IndexOf((c, 3)) expected 1, got {tuples.IndexOf(("c", 3))}");
    if (tuples.IndexOf(("b", 2)) != -1)
        return Fail($"HeapPooledList<(string,int)>.IndexOf((b, 2)) expected -1, got {tuples.IndexOf(("b", 2))}");
    Console.WriteLine("AOT smoke: HeapPooledList<(string, int)> OK");
}

// 6. HeapPooledList<int?>: Contains/IndexOf/Remove with a null element
using (var nullableInts = new HeapPooledList<int?>())
{
    nullableInts.Add(1);
    nullableInts.Add(null);
    nullableInts.Add(3);
    if (!nullableInts.Contains(null)) return Fail("HeapPooledList<int?>.Contains(null) expected true");
    if (nullableInts.IndexOf(null) != 1)
        return Fail($"HeapPooledList<int?>.IndexOf(null) expected 1, got {nullableInts.IndexOf(null)}");
    if (nullableInts.IndexOf(3) != 2)
        return Fail($"HeapPooledList<int?>.IndexOf(3) expected 2, got {nullableInts.IndexOf(3)}");
    if (nullableInts.IndexOf(4) != -1)
        return Fail($"HeapPooledList<int?>.IndexOf(4) expected -1, got {nullableInts.IndexOf(4)}");
    if (!nullableInts.Remove(null)) return Fail("HeapPooledList<int?>.Remove(null) expected true");
    if (nullableInts.Contains(null)) return Fail("HeapPooledList<int?>.Contains(null) expected false after Remove");
    if (nullableInts.Count != 2 || nullableInts[1] != 3)
        return Fail($"HeapPooledList<int?> after Remove: Count={nullableInts.Count}, [1]={nullableInts[1]}");
    Console.WriteLine("AOT smoke: HeapPooledList<int?> OK");
}

// 7. PooledList<int?> (ref struct): Contains/IndexOf with a null element
{
    using var pooledNullable = new PooledList<int?>(capacity: 2);
    pooledNullable.Add(10);
    pooledNullable.Add(null);
    pooledNullable.Add(30);
    if (!pooledNullable.Contains(null)) return Fail("PooledList<int?>.Contains(null) expected true");
    if (pooledNullable.IndexOf(null) != 1)
        return Fail($"PooledList<int?>.IndexOf(null) expected 1, got {pooledNullable.IndexOf(null)}");
    if (pooledNullable.IndexOf(30) != 2)
        return Fail($"PooledList<int?>.IndexOf(30) expected 2, got {pooledNullable.IndexOf(30)}");
    if (pooledNullable.Contains(20)) return Fail("PooledList<int?>.Contains(20) expected false");
    if (pooledNullable[1] is not null) return Fail($"PooledList<int?>[1] expected null, got {pooledNullable[1]}");
    Console.WriteLine("AOT smoke: PooledList<int?> OK");
}

// 8. HeapSpanDictionary<int?, Guid?>: a null key and a null value. The implementation hashes a
//    null key to 0 instead of rejecting it, so null is an ordinary key here.
var g1 = new Guid("11111111-1111-1111-1111-111111111111");
var g2 = new Guid("22222222-2222-2222-2222-222222222222");
using (var nullableDict = new HeapSpanDictionary<int?, Guid?>(capacity: 2))
{
    nullableDict.Add(null, g1);
    nullableDict.Add(1, null);
    nullableDict.Add(2, g2);
    if (nullableDict.Count != 3)
        return Fail($"HeapSpanDictionary<int?, Guid?>.Count expected 3, got {nullableDict.Count}");
    if (!nullableDict.TryGetValue(null, out var nv) || nv != g1)
        return Fail($"HeapSpanDictionary<int?, Guid?>.TryGetValue(null) expected {g1}, got {nv}");
    if (!nullableDict.TryGetValue(1, out var nullValue) || nullValue is not null)
        return Fail($"HeapSpanDictionary<int?, Guid?>.TryGetValue(1) expected null, got {nullValue}");
    if (nullableDict.TryGetValue(3, out _)) return Fail("HeapSpanDictionary<int?, Guid?>.TryGetValue(3) expected false");
    if (nullableDict[2] != g2) return Fail($"HeapSpanDictionary<int?, Guid?>[2] expected {g2}, got {nullableDict[2]}");
    if (!nullableDict.Contains(new KeyValuePair<int?, Guid?>(1, null)))
        return Fail("HeapSpanDictionary<int?, Guid?>.Contains((1, null)) expected true");
    if (nullableDict.Contains(new KeyValuePair<int?, Guid?>(1, g1)))
        return Fail("HeapSpanDictionary<int?, Guid?>.Contains((1, g1)) expected false");
    if (!nullableDict.Contains(new KeyValuePair<int?, Guid?>(null, g1)))
        return Fail("HeapSpanDictionary<int?, Guid?>.Contains((null, g1)) expected true");
    if (nullableDict.Remove(new KeyValuePair<int?, Guid?>(null, g2)))
        return Fail("HeapSpanDictionary<int?, Guid?>.Remove((null, g2)) expected false");
    if (!nullableDict.Remove(new KeyValuePair<int?, Guid?>(null, g1)))
        return Fail("HeapSpanDictionary<int?, Guid?>.Remove((null, g1)) expected true");
    if (nullableDict.ContainsKey(null))
        return Fail("HeapSpanDictionary<int?, Guid?>.ContainsKey(null) expected false after Remove");
    if (!nullableDict.Remove(1)) return Fail("HeapSpanDictionary<int?, Guid?>.Remove(1) expected true");
    if (nullableDict.Count != 1)
        return Fail($"HeapSpanDictionary<int?, Guid?>.Count expected 1, got {nullableDict.Count}");
    Console.WriteLine("AOT smoke: HeapSpanDictionary<int?, Guid?> OK");
}

// 9. SpanDictionary<TKey, TValue> (ref struct) keyed by a user struct, valued by an enum
{
    var k1 = new SmokeKey(1, 1);
    var k2 = new SmokeKey(1, 2);
    // Not a using variable: the indexer setter below mutates the ref struct in place.
    var structDict = new SpanDictionary<SmokeKey, SmokeColor>(capacity: 2);
    try
    {
        structDict.Add(k1, SmokeColor.Red);
        structDict.Add(k2, SmokeColor.Blue);
        structDict[k1] = SmokeColor.Blue;
        if (structDict.Count != 2)
            return Fail($"SpanDictionary<SmokeKey, SmokeColor>.Count expected 2, got {structDict.Count}");
        if (!structDict.TryGetValue(new SmokeKey(1, 1), out var c1) || c1 != SmokeColor.Blue)
            return Fail($"SpanDictionary<SmokeKey, SmokeColor>.TryGetValue(k1) expected Blue, got {c1}");
        if (structDict.ContainsKey(new SmokeKey(2, 1)))
            return Fail("SpanDictionary<SmokeKey, SmokeColor>.ContainsKey((2, 1)) expected false");
        if (!structDict.Remove(k2)) return Fail("SpanDictionary<SmokeKey, SmokeColor>.Remove(k2) expected true");
        if (structDict.ContainsKey(k2) || structDict.Count != 1)
            return Fail($"SpanDictionary<SmokeKey, SmokeColor> after Remove: Count={structDict.Count}");
    }
    finally
    {
        structDict.Dispose();
    }
    Console.WriteLine("AOT smoke: SpanDictionary<SmokeKey, SmokeColor> OK");
}

// 10. ConcurrentHeapSpanDictionary<Guid, decimal?>: TryUpdate against a null comparand,
//     GetOrAdd, AddOrUpdate and TryRemove with a nullable value type
using (var cnull = new ConcurrentHeapSpanDictionary<Guid, decimal?>(capacity: 2))
{
    var g3 = new Guid("33333333-3333-3333-3333-333333333333");
    var g4 = new Guid("44444444-4444-4444-4444-444444444444");
    if (!cnull.TryAdd(g1, null)) return Fail("ConcurrentHeapSpanDictionary<Guid, decimal?>.TryAdd(g1, null) refused");
    if (!cnull.TryUpdate(g1, 1.5m, null))
        return Fail("ConcurrentHeapSpanDictionary<Guid, decimal?>.TryUpdate with null comparand expected true");
    if (cnull.TryUpdate(g1, 2m, null))
        return Fail("ConcurrentHeapSpanDictionary<Guid, decimal?>.TryUpdate with stale null comparand expected false");
    if (cnull[g1] != 1.5m) return Fail($"ConcurrentHeapSpanDictionary<Guid, decimal?>[g1] expected 1.5, got {cnull[g1]}");
    var added = cnull.GetOrAdd(g2, 7m);
    var existing = cnull.GetOrAdd(g2, 9m);
    if (added != 7m || existing != 7m)
        return Fail($"ConcurrentHeapSpanDictionary<Guid, decimal?>.GetOrAdd expected 7 twice, got {added} and {existing}");
    var factoryNull = cnull.GetOrAdd(g3, static _ => null);
    if (factoryNull is not null || !cnull.ContainsKey(g3))
        return Fail($"ConcurrentHeapSpanDictionary<Guid, decimal?>.GetOrAdd factory expected a stored null, got {factoryNull}");
    var updated = cnull.AddOrUpdate(g1, 0m, static (_, v) => v + 1m);
    if (updated != 2.5m)
        return Fail($"ConcurrentHeapSpanDictionary<Guid, decimal?>.AddOrUpdate update expected 2.5, got {updated}");
    var addedNull = cnull.AddOrUpdate(g4, null, static (_, v) => v);
    if (addedNull is not null || !cnull.TryGetValue(g4, out var g4Value) || g4Value is not null)
        return Fail("ConcurrentHeapSpanDictionary<Guid, decimal?>.AddOrUpdate add expected a stored null");
    if (!cnull.TryRemove(g1, out var removed) || removed != 2.5m)
        return Fail($"ConcurrentHeapSpanDictionary<Guid, decimal?>.TryRemove(g1) expected 2.5, got {removed}");
    if (cnull.TryRemove(g1, out _))
        return Fail("ConcurrentHeapSpanDictionary<Guid, decimal?>.TryRemove(g1) twice expected false");
    if (!cnull.TryRemove(g3, out var removedNull) || removedNull is not null)
        return Fail($"ConcurrentHeapSpanDictionary<Guid, decimal?>.TryRemove(g3) expected a null value, got {removedNull}");
    if (cnull.Count != 2)
        return Fail($"ConcurrentHeapSpanDictionary<Guid, decimal?>.Count expected 2, got {cnull.Count}");
    Console.WriteLine("AOT smoke: ConcurrentHeapSpanDictionary<Guid, decimal?> OK");
}

// 11. HeapPooledQueue<T> and PooledStack<T> over a nullable user struct
using (var queue = new HeapPooledQueue<SmokeKey?>(capacity: 2))
{
    queue.Enqueue(new SmokeKey(1, 0));
    queue.Enqueue(null);
    queue.Enqueue(new SmokeKey(3, 0));
    if (!queue.TryPeek(out var head) || head != new SmokeKey(1, 0))
        return Fail($"HeapPooledQueue<SmokeKey?>.TryPeek expected (1, 0), got {head}");
    if (!queue.TryDequeue(out var q1) || q1 != new SmokeKey(1, 0))
        return Fail($"HeapPooledQueue<SmokeKey?>.TryDequeue #1 expected (1, 0), got {q1}");
    if (!queue.TryDequeue(out var q2) || q2 is not null)
        return Fail($"HeapPooledQueue<SmokeKey?>.TryDequeue #2 expected null, got {q2}");
    if (!queue.TryDequeue(out var q3) || q3 != new SmokeKey(3, 0))
        return Fail($"HeapPooledQueue<SmokeKey?>.TryDequeue #3 expected (3, 0), got {q3}");
    if (queue.TryDequeue(out _) || !queue.IsEmpty) return Fail("HeapPooledQueue<SmokeKey?> expected empty");
    Console.WriteLine("AOT smoke: HeapPooledQueue<SmokeKey?> OK");
}
{
    using var stack = new PooledStack<SmokeKey?>(capacity: 2);
    stack.Push(new SmokeKey(1, 0));
    stack.Push(null);
    stack.Push(new SmokeKey(3, 0));
    if (!stack.TryPop(out var s1) || s1 != new SmokeKey(3, 0))
        return Fail($"PooledStack<SmokeKey?>.TryPop #1 expected (3, 0), got {s1}");
    if (!stack.TryPeek(out var top) || top is not null)
        return Fail($"PooledStack<SmokeKey?>.TryPeek expected null, got {top}");
    if (!stack.TryPop(out var s2) || s2 is not null)
        return Fail($"PooledStack<SmokeKey?>.TryPop #2 expected null, got {s2}");
    if (!stack.TryPop(out var s3) || s3 != new SmokeKey(1, 0))
        return Fail($"PooledStack<SmokeKey?>.TryPop #3 expected (1, 0), got {s3}");
    if (stack.Count != 0) return Fail($"PooledStack<SmokeKey?>.Count expected 0, got {stack.Count}");
    Console.WriteLine("AOT smoke: PooledStack<SmokeKey?> OK");
}

// 12. HeapFixedSizeList<T> over a nullable enum: IndexOf/Remove through Array.IndexOf
{
    var fixedList = new HeapFixedSizeList<SmokeColor?>(capacity: 3);
    fixedList.Add(SmokeColor.Red);
    fixedList.Add(null);
    fixedList.Add(SmokeColor.Blue);
    if (!fixedList.IsFull) return Fail("HeapFixedSizeList<SmokeColor?>.IsFull expected true");
    if (fixedList.IndexOf(null) != 1)
        return Fail($"HeapFixedSizeList<SmokeColor?>.IndexOf(null) expected 1, got {fixedList.IndexOf(null)}");
    if (fixedList.IndexOf(SmokeColor.Blue) != 2)
        return Fail($"HeapFixedSizeList<SmokeColor?>.IndexOf(Blue) expected 2, got {fixedList.IndexOf(SmokeColor.Blue)}");
    if (fixedList.Contains(SmokeColor.None)) return Fail("HeapFixedSizeList<SmokeColor?>.Contains(None) expected false");
    if (!fixedList.Remove(null) || fixedList.Count != 2 || fixedList[1] != SmokeColor.Blue)
        return Fail($"HeapFixedSizeList<SmokeColor?>.Remove(null) broke: Count={fixedList.Count}");
    Console.WriteLine("AOT smoke: HeapFixedSizeList<SmokeColor?> OK");
}

// 13. [ZeroAllocList(typeof(int?))]: generator-emitted list over a nullable primitive
{
    using var generated = new NullableIntList(capacity: 2);
    generated.Add(5);
    generated.Add(null);
    generated.Add(7);
    if (generated.Count != 3) return Fail($"NullableIntList.Count expected 3, got {generated.Count}");
    if (generated[1] is not null) return Fail($"NullableIntList[1] expected null, got {generated[1]}");
    var sum = 0;
    var nulls = 0;
    foreach (var item in generated)
    {
        if (item is null) nulls++;
        else sum += item.Value;
    }
    if (sum != 12 || nulls != 1)
        return Fail($"NullableIntList enumeration expected sum 12 and 1 null, got {sum} and {nulls}");
    var arr = generated.ToArray();
    if (arr.Length != 3 || arr[0] != 5 || arr[1] is not null || arr[2] != 7)
        return Fail("NullableIntList.ToArray expected [5, null, 7]");
    Console.WriteLine("AOT smoke: [ZeroAllocList(typeof(int?))] OK");
}

Console.WriteLine("AOT smoke: PASS");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — {message}");
    return 1;
}
