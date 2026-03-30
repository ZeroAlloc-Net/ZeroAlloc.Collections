---
id: testing
title: Testing
sidebar_position: 10
---

# Testing

ZeroAlloc.Collections types are plain structs and classes with no framework dependencies beyond `System.Buffers`. Testing them requires no special adapter or mock library — instantiate, use, assert, dispose.

This page covers patterns for testing ref struct collections, heap variants, custom pool verification, and source-generated types.

## Testing ref struct Collections

`ref struct` types (`PooledList<T>`, `RingBuffer<T>`, `SpanDictionary<TKey, TValue>`, `PooledStack<T>`, `PooledQueue<T>`, `FixedSizeList<T>`) cannot be used in `async` test methods because the compiler forbids ref structs in async state machines. Keep your tests synchronous:

```csharp
[Fact]
public void PooledList_Add_IncreasesCount()
{
    using var list = new PooledList<int>();

    list.Add(42);

    Assert.Equal(1, list.Count);
    Assert.Equal(42, list[0]);
}

[Fact]
public void FixedSizeList_TryAdd_ReturnsFalseWhenFull()
{
    var list = new FixedSizeList<int>(stackalloc int[2]);

    Assert.True(list.TryAdd(1));
    Assert.True(list.TryAdd(2));
    Assert.False(list.TryAdd(3));
    Assert.True(list.IsFull);
}
```

If you need to test behavior that involves async code, use the `Heap*` variant instead (see next section).

## Testing Heap Variants

`HeapPooledList<T>`, `HeapRingBuffer<T>`, `HeapSpanDictionary<TKey, TValue>`, `HeapPooledStack<T>`, `HeapPooledQueue<T>`, and `HeapFixedSizeList<T>` are regular classes. They work in async tests, implement `IDisposable` (where applicable), and can be passed to methods that accept `IList<T>` or `IReadOnlyList<T>`:

```csharp
[Fact]
public async Task HeapPooledList_CanBeUsedInAsyncCode()
{
    using var list = new HeapPooledList<string>();

    list.Add("hello");
    await Task.Yield(); // ref struct variant would not compile here

    Assert.Equal(1, list.Count);
    Assert.Equal("hello", list[0]);
}

[Fact]
public void HeapFixedSizeList_ImplementsIList()
{
    var list = new HeapFixedSizeList<int>(capacity: 8);
    list.Add(1);
    list.Add(2);
    list.Add(3);

    IList<int> ilist = list;
    ilist.Insert(1, 10);

    Assert.Equal(4, list.Count);
    Assert.Equal(10, list[1]);
}
```

## Testing with a Custom ArrayPool

To verify that a collection returns its buffer to the pool on disposal, create a tracking `ArrayPool<T>`:

```csharp
public sealed class TrackingPool<T> : ArrayPool<T>
{
    private int _rented;
    private int _returned;

    public int Rented => _rented;
    public int Returned => _returned;
    public int Outstanding => _rented - _returned;

    public override T[] Rent(int minimumLength)
    {
        Interlocked.Increment(ref _rented);
        return new T[minimumLength];
    }

    public override void Return(T[] array, bool clearArray = false)
    {
        Interlocked.Increment(ref _returned);
        if (clearArray)
            Array.Clear(array, 0, array.Length);
    }
}

[Fact]
public void PooledList_Dispose_ReturnsBufferToPool()
{
    var pool = new TrackingPool<int>();

    var list = new PooledList<int>(capacity: 16, pool);
    list.Add(1);
    list.Add(2);
    list.Dispose();

    Assert.Equal(0, pool.Outstanding);
}

[Fact]
public void PooledList_Grow_ReturnsOldBufferToPool()
{
    var pool = new TrackingPool<int>();

    var list = new PooledList<int>(capacity: 2, pool);
    list.Add(1);
    list.Add(2);
    list.Add(3); // triggers grow — old buffer returned, new buffer rented

    Assert.True(pool.Rented >= 2);    // at least initial + grown
    Assert.True(pool.Returned >= 1);  // old buffer returned on grow

    list.Dispose();
    Assert.Equal(0, pool.Outstanding);
}
```

## Testing Enumeration

Verify that the zero-allocation enumerator produces the correct sequence. Because the enumerator is a `ref struct`, use a synchronous test and collect results into a local list:

```csharp
[Fact]
public void PooledList_Enumerator_YieldsAllElements()
{
    using var list = new PooledList<int>();
    list.Add(10);
    list.Add(20);
    list.Add(30);

    var results = new List<int>();
    foreach (ref readonly var item in list)
    {
        results.Add(item);
    }

    Assert.Equal([10, 20, 30], results);
}
```

## Testing SpanDictionary

Test lookup, collision, and missing-key scenarios:

```csharp
[Fact]
public void SpanDictionary_TryGetValue_ReturnsTrueForExistingKey()
{
    using var dict = new SpanDictionary<string, int>(capacity: 8);
    dict.TryAdd("alpha", 1);
    dict.TryAdd("beta", 2);

    Assert.True(dict.TryGetValue("alpha", out var value));
    Assert.Equal(1, value);
}

[Fact]
public void SpanDictionary_TryGetValue_ReturnsFalseForMissingKey()
{
    using var dict = new SpanDictionary<string, int>(capacity: 8);
    dict.TryAdd("alpha", 1);

    Assert.False(dict.TryGetValue("gamma", out _));
}
```

## Testing RingBuffer

Test the circular write/read behavior and full/empty boundary conditions:

```csharp
[Fact]
public void RingBuffer_WrapAround_ReadsCorrectOrder()
{
    using var ring = new RingBuffer<int>(capacity: 4);

    ring.TryWrite(1);
    ring.TryWrite(2);
    ring.TryWrite(3);
    ring.TryWrite(4);

    Assert.True(ring.IsFull);
    Assert.True(ring.TryRead(out var v1));
    Assert.Equal(1, v1);

    // Now there is space for one more
    Assert.True(ring.TryWrite(5));

    // Read remaining in FIFO order
    Assert.True(ring.TryRead(out var v2));
    Assert.Equal(2, v2);
}
```

## Testing Source-Generated Types

Source-generated types (`[ZeroAllocList]`, `[PooledCollection]`, `[ZeroAllocEnumerable]`) are tested like any other type. Declare the annotated type in the test project (or a referenced project) and exercise the generated API:

```csharp
[ZeroAllocList(typeof(double))]
public partial struct DoubleList;

[Fact]
public void ZeroAllocList_Generated_AddAndEnumerate()
{
    var list = new DoubleList(capacity: 16);
    try
    {
        list.Add(1.0);
        list.Add(2.0);
        list.Add(3.0);

        Assert.Equal(3, list.Count);

        var results = new List<double>();
        foreach (ref readonly var item in list)
        {
            results.Add(item);
        }

        Assert.Equal([1.0, 2.0, 3.0], results);
    }
    finally
    {
        list.Dispose();
    }
}
```

### Testing the ZeroAllocEnumerable Generator

```csharp
[ZeroAllocEnumerable]
public partial struct TestBuffer
{
    private int[] _data;
    private int _count;

    public TestBuffer(int capacity)
    {
        _data = new int[capacity];
        _count = 0;
    }

    public void Add(int value) => _data[_count++] = value;
}

[Fact]
public void ZeroAllocEnumerable_Generated_EnumeratesCorrectly()
{
    var buffer = new TestBuffer(8);
    buffer.Add(10);
    buffer.Add(20);

    var results = new List<int>();
    foreach (ref readonly var item in buffer)
    {
        results.Add(item);
    }

    Assert.Equal([10, 20], results);
}
```

## Testing Generator Diagnostics

To verify that the ZAC001 analyzer correctly reports warnings, use `Microsoft.CodeAnalysis.Testing` to run the analyzer against an in-memory compilation:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Collections.Generators.Diagnostics;

[Fact]
public async Task ZAC001_TriggersOnUndisposedPooledList()
{
    var testCode = """
        using ZeroAlloc.Collections;
        class C
        {
            void M()
            {
                var list = new PooledList<int>();
            }
        }
        """;

    var expected = DiagnosticResult.CompilerWarning("ZAC001")
        .WithSpan(6, 13, 6, 17)
        .WithArguments("PooledList");

    // Use CSharpAnalyzerTest with UndisposedPooledCollectionAnalyzer
    // (actual test infrastructure depends on your test project setup)
}
```

## Integration Test Strategies

For integration tests that exercise collections in realistic scenarios:

1. **Allocate, populate, process, dispose** — verify end-to-end correctness with realistic data volumes.
2. **Verify pool hygiene** — use `TrackingPool<T>` to assert that all rented buffers are returned after a full request/response cycle.
3. **Stress test capacity transitions** — add more items than the initial capacity to exercise grow logic, then verify all items are present.
4. **Multi-threaded ring buffer tests** — `RingBuffer<T>` is designed for single-threaded use. If you wrap it with synchronization for producer/consumer scenarios, test concurrent write/read correctness.
5. **Native AOT smoke test** — publish a small console app with `PublishAot` that exercises each collection type, then run it to verify no trimming or AOT issues.
