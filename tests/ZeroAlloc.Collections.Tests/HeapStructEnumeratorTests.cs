using System.Collections;
using Xunit;

namespace ZeroAlloc.Collections.Tests;

/// <summary>
/// The Heap* collections return public struct enumerators (issue #152). These tests pin the
/// enumeration order, Reset, the boxed interface path and the modification rules of each one.
/// The allocation side is in <see cref="AllocationTests"/>.
/// </summary>
public class HeapStructEnumeratorTests
{
    [Theory]
    [InlineData(typeof(HeapPooledList<int>))]
    [InlineData(typeof(HeapFixedSizeList<int>))]
    [InlineData(typeof(HeapPooledQueue<int>))]
    [InlineData(typeof(HeapPooledStack<int>))]
    [InlineData(typeof(HeapRingBuffer<int>))]
    [InlineData(typeof(HeapSpanDictionary<int, int>))]
    [InlineData(typeof(HeapSpanDictionary<int, int>.KeyCollection))]
    [InlineData(typeof(HeapSpanDictionary<int, int>.ValueCollection))]
    [InlineData(typeof(ConcurrentHeapSpanDictionary<int, int>))]
    public void PublicGetEnumerator_ReturnsAStruct(Type collection)
    {
        var method = collection.GetMethod("GetEnumerator", Type.EmptyTypes);
        Assert.NotNull(method);
        Assert.True(method.ReturnType.IsValueType, $"{collection.Name}.GetEnumerator() returns {method.ReturnType}");
        Assert.True(method.ReturnType.IsNestedPublic);
    }

    // --- HeapPooledList ---

    [Fact]
    public void HeapPooledList_EnumeratesInIndexOrder_AndResets()
    {
        using var list = new HeapPooledList<int>();
        for (int i = 0; i < 10; i++) list.Add(i);

        var e = list.GetEnumerator();
        Assert.Equal(Enumerable.Range(0, 10), Drain(ref e));
        Assert.False(e.MoveNext());
        Assert.Equal(0, e.Current);
        e.Reset();
        Assert.Equal(Enumerable.Range(0, 10), Drain(ref e));
        e.Dispose();
    }

    [Fact]
    public void HeapPooledList_EnumeratesThroughTheInterfaces()
    {
        using var list = new HeapPooledList<string>();
        list.Add("a");
        list.Add("b");

        Assert.Equal(new[] { "a", "b" }, ((IEnumerable<string>)list).ToArray());
        Assert.Equal(new object[] { "a", "b" }, ((IEnumerable)list).Cast<object>().ToArray());
        IEnumerator boxed = ((IEnumerable)list).GetEnumerator();
        Assert.True(boxed.MoveNext());
        Assert.Equal("a", boxed.Current);
    }

    [Fact]
    public void HeapPooledList_EmptyAndDisposed_EnumerateNothing()
    {
        var list = new HeapPooledList<int>();
        Assert.Empty(EnumerateList(list));
        list.Add(1);
        list.Dispose();
        Assert.Empty(EnumerateList(list));
    }

    [Fact]
    public void Dispose_EmptiesTheList_QueueAndRingBuffer()
    {
        var list = new HeapPooledList<int>();
        list.Add(1);
        list.Dispose();
        Assert.Empty(list);
        list.Add(2);
        Assert.Equal(new[] { 2 }, list.ToArray());
        list.Dispose();

        var queue = new HeapPooledQueue<int>();
        queue.Enqueue(1);
        queue.Dispose();
        Assert.Empty(queue);
        queue.Enqueue(2);
        Assert.Equal(new[] { 2 }, queue.ToArray());
        queue.Dispose();

        var ring = new HeapRingBuffer<int>(4);
        ring.TryWrite(1);
        ring.Dispose();
        Assert.Empty(ring);
        Assert.True(ring.IsEmpty);
    }

    // --- HeapFixedSizeList ---

    [Fact]
    public void HeapFixedSizeList_EnumeratesInIndexOrder_AndResets()
    {
        var list = new HeapFixedSizeList<int>(8);
        for (int i = 0; i < 5; i++) list.Add(i * 2);

        var e = list.GetEnumerator();
        Assert.Equal(new[] { 0, 2, 4, 6, 8 }, Drain(ref e));
        e.Reset();
        Assert.Equal(new[] { 0, 2, 4, 6, 8 }, Drain(ref e));
        Assert.Equal(new[] { 0, 2, 4, 6, 8 }, ((IEnumerable<int>)list).ToArray());
        Assert.Empty(EnumerateFixed(new HeapFixedSizeList<int>(0)));
    }

    // --- HeapPooledStack ---

    [Fact]
    public void HeapPooledStack_EnumeratesTopToBottom_AndResets()
    {
        using var stack = new HeapPooledStack<int>();
        for (int i = 0; i < 6; i++) stack.Push(i);

        var e = stack.GetEnumerator();
        Assert.Equal(new[] { 5, 4, 3, 2, 1, 0 }, Drain(ref e));
        Assert.False(e.MoveNext());
        e.Reset();
        Assert.Equal(new[] { 5, 4, 3, 2, 1, 0 }, Drain(ref e));
        Assert.Equal(new[] { 5, 4, 3, 2, 1, 0 }, ((IEnumerable<int>)stack).ToArray());
    }

    [Fact]
    public void HeapPooledStack_Enumerator_SkipsPoppedElements_AndEndsWhenDisposed()
    {
        var stack = new HeapPooledStack<int>();
        for (int i = 0; i < 4; i++) stack.Push(i);

        var popped = stack.GetEnumerator();
        stack.TryPop(out _);
        Assert.Equal(new[] { 2, 1, 0 }, Drain(ref popped));

        var disposed = stack.GetEnumerator();
        stack.Dispose();
        Assert.False(disposed.MoveNext());
    }

    [Fact]
    public void HeapPooledStack_Empty_EnumeratesNothing()
    {
        using var stack = new HeapPooledStack<int>();
        var e = stack.GetEnumerator();
        Assert.False(e.MoveNext());
        Assert.False(e.MoveNext());
    }

    // --- HeapPooledQueue ---

    [Fact]
    public void HeapPooledQueue_EnumeratesInFifoOrder_AcrossTheWrap()
    {
        // ArrayPool<T>.Shared rents at least 16 slots; an exact-size pool makes four writes wrap.
        using var queue = new HeapPooledQueue<int>(4, new ExactSizePool<int>());
        for (int i = 0; i < 4; i++) queue.Enqueue(i);
        queue.TryDequeue(out _);
        queue.TryDequeue(out _);
        queue.Enqueue(4);
        queue.Enqueue(5);

        var e = queue.GetEnumerator();
        Assert.Equal(new[] { 2, 3, 4, 5 }, Drain(ref e));
        e.Reset();
        Assert.Equal(new[] { 2, 3, 4, 5 }, Drain(ref e));
        Assert.Equal(queue.ToArray(), ((IEnumerable<int>)queue).ToArray());
    }

    [Fact]
    public void HeapPooledQueue_EnumeratorCreatedBeforeAGrow_StaysInOrder()
    {
        using var queue = new HeapPooledQueue<int>(4, new ExactSizePool<int>());
        for (int i = 0; i < 4; i++) queue.Enqueue(i);
        queue.TryDequeue(out _);
        queue.Enqueue(4);

        // The head sits at slot 1; growing moves it to slot 0.
        var e = queue.GetEnumerator();
        queue.Enqueue(5);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Drain(ref e));
    }

    [Fact]
    public void HeapPooledQueue_Empty_EnumeratesNothing()
    {
        using var queue = new HeapPooledQueue<int>();
        var e = queue.GetEnumerator();
        Assert.False(e.MoveNext());
    }

    // --- HeapRingBuffer ---

    [Fact]
    public void HeapRingBuffer_EnumeratesInFifoOrder_AcrossTheWrap()
    {
        using var ring = new HeapRingBuffer<int>(4);
        for (int i = 0; i < 4; i++) ring.TryWrite(i);
        ring.TryRead(out _);
        ring.TryRead(out _);
        ring.TryWrite(4);
        ring.TryWrite(5);

        var e = ring.GetEnumerator();
        Assert.Equal(new[] { 2, 3, 4, 5 }, Drain(ref e));
        Assert.Equal(ring.ToArray(), ((IEnumerable<int>)ring).ToArray());
    }

    [Fact]
    public void HeapRingBuffer_Reset_TakesTheCurrentContents()
    {
        using var ring = new HeapRingBuffer<int>(4);
        ring.TryWrite(1);
        ring.TryWrite(2);

        var e = ring.GetEnumerator();
        Assert.Equal(new[] { 1, 2 }, Drain(ref e));
        ring.TryWrite(3);
        Assert.False(e.MoveNext());
        e.Reset();
        Assert.Equal(new[] { 1, 2, 3 }, Drain(ref e));
    }

    [Fact]
    public void HeapRingBuffer_Enumerator_EndsWhenDisposed()
    {
        var ring = new HeapRingBuffer<int>(4);
        ring.TryWrite(1);
        ring.TryWrite(2);

        var e = ring.GetEnumerator();
        Assert.True(e.MoveNext());
        ring.Dispose();
        Assert.False(e.MoveNext());
        Assert.Equal(0, e.Current);
    }

    // --- HeapSpanDictionary ---

    [Fact]
    public void HeapSpanDictionary_EnumeratesEveryPair_AndResets()
    {
        using var dict = new HeapSpanDictionary<int, string>();
        for (int i = 0; i < 20; i++) dict.Add(i, i.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var e = dict.GetEnumerator();
        var pairs = new List<KeyValuePair<int, string>>();
        while (e.MoveNext()) pairs.Add(e.Current);
        Assert.Equal(Enumerable.Range(0, 20), pairs.Select(p => p.Key).Order());
        Assert.All(pairs, p => Assert.Equal(p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Value));
        Assert.Equal(default, e.Current);

        e.Reset();
        int count = 0;
        while (e.MoveNext()) count++;
        Assert.Equal(20, count);
        Assert.Equal(20, ((IEnumerable<KeyValuePair<int, string>>)dict).Count());
    }

    [Fact]
    public void HeapSpanDictionary_Enumeration_ThrowsWhenAKeyIsAdded()
    {
        using var dict = new HeapSpanDictionary<int, int>();
        dict.Add(1, 1);
        dict.Add(2, 2);

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var pair in dict) dict.Add(pair.Key + 100, 0);
        });
    }

    [Fact]
    public void HeapSpanDictionary_Enumeration_ThrowsWhenCleared()
    {
        using var dict = new HeapSpanDictionary<int, int>();
        dict.Add(1, 1);

        var e = dict.GetEnumerator();
        dict.Clear();
        Assert.Throws<InvalidOperationException>(() => e.MoveNext());
        Assert.Throws<InvalidOperationException>(() => e.Reset());
    }

    [Fact]
    public void HeapSpanDictionary_Enumeration_AllowsRemoveAndOverwrite()
    {
        using var dict = new HeapSpanDictionary<int, int>();
        for (int i = 0; i < 10; i++) dict.Add(i, i);

        foreach (var pair in dict)
        {
            if (pair.Key % 2 == 0) dict.Remove(pair.Key);
            else dict[pair.Key] = pair.Value * 10;
        }

        Assert.Equal(new[] { 1, 3, 5, 7, 9 }, dict.Keys.Order());
        Assert.Equal(new[] { 10, 30, 50, 70, 90 }, dict.Values.Order());
    }

    [Fact]
    public void HeapSpanDictionary_EnumeratorOfADisposedDictionary_EnumeratesNothing()
    {
        var dict = new HeapSpanDictionary<int, int>();
        dict.Add(1, 1);
        dict.Dispose();

        var e = dict.GetEnumerator();
        Assert.False(e.MoveNext());
        var keys = dict.Keys.GetEnumerator();
        Assert.False(keys.MoveNext());
        var values = dict.Values.GetEnumerator();
        Assert.False(values.MoveNext());
    }

    [Fact]
    public void HeapSpanDictionary_KeyAndValueEnumerators_Reset()
    {
        using var dict = new HeapSpanDictionary<int, int>();
        dict.Add(1, 10);
        dict.Add(2, 20);

        var keys = dict.Keys.GetEnumerator();
        int keySum = 0;
        while (keys.MoveNext()) keySum += keys.Current;
        Assert.Equal(0, keys.Current);
        keys.Reset();
        while (keys.MoveNext()) keySum += keys.Current;
        Assert.Equal(6, keySum);
        keys.Dispose();

        var values = dict.Values.GetEnumerator();
        int valueSum = 0;
        while (values.MoveNext()) valueSum += values.Current;
        Assert.Equal(0, values.Current);
        values.Reset();
        while (values.MoveNext()) valueSum += values.Current;
        Assert.Equal(60, valueSum);
        values.Dispose();

        dict.Add(3, 30);
        Assert.Throws<InvalidOperationException>(() => keys.Reset());
        Assert.Throws<InvalidOperationException>(() => values.Reset());
    }

    [Fact]
    public void HeapSpanDictionary_KeyAndValueViews_EnumerateThroughTheInterfaces()
    {
        using var dict = new HeapSpanDictionary<int, int>();
        dict.Add(1, 10);
        dict.Add(2, 20);

        IDictionary<int, int> asDictionary = dict;
        IReadOnlyDictionary<int, int> asReadOnly = dict;
        Assert.Equal(new[] { 1, 2 }, asDictionary.Keys.Order());
        Assert.Equal(new[] { 10, 20 }, asDictionary.Values.Order());
        Assert.Equal(new[] { 1, 2 }, asReadOnly.Keys.Order());
        Assert.Equal(new[] { 10, 20 }, asReadOnly.Values.Order());
        Assert.Equal(2, ((IEnumerable)dict.Keys).Cast<int>().Count());
        Assert.Equal(2, ((IEnumerable)dict.Values).Cast<int>().Count());
        IReadOnlyCollection<int> readOnlyKeys = dict.Keys;
        Assert.Equal(2, readOnlyKeys.Count);
    }

    // --- ConcurrentHeapSpanDictionary ---

    [Fact]
    public void ConcurrentHeapSpanDictionary_Enumerator_DisposeIsIdempotent()
    {
        using var dict = new ConcurrentHeapSpanDictionary<int, int>();
        dict.TryAdd(1, 10);

        var e = dict.GetEnumerator();
        Assert.True(e.MoveNext());
        Assert.Equal(new KeyValuePair<int, int>(1, 10), e.Current);
        e.Dispose();
        Assert.False(e.MoveNext());
        Assert.Equal(default, e.Current);
        e.Dispose();
    }

    [Fact]
    public void ConcurrentHeapSpanDictionary_EnumeratesThroughTheInterfaces()
    {
        using var dict = new ConcurrentHeapSpanDictionary<int, int>();
        for (int i = 0; i < 5; i++) dict.TryAdd(i, i);

        Assert.Equal(Enumerable.Range(0, 5), ((IEnumerable<KeyValuePair<int, int>>)dict).Select(p => p.Key).Order());
        IEnumerator boxed = ((IEnumerable)dict).GetEnumerator();
        int count = 0;
        while (boxed.MoveNext()) count++;
        Assert.Equal(5, count);
        ((IDisposable)boxed).Dispose();
    }

    private static List<T> Drain<TEnumerator, T>(ref TEnumerator enumerator)
        where TEnumerator : struct, IEnumerator<T>
    {
        var items = new List<T>();
        while (enumerator.MoveNext()) items.Add(enumerator.Current);
        return items;
    }

    private static List<int> Drain(ref HeapPooledList<int>.Enumerator e) => Drain<HeapPooledList<int>.Enumerator, int>(ref e);

    private static List<int> Drain(ref HeapFixedSizeList<int>.Enumerator e) => Drain<HeapFixedSizeList<int>.Enumerator, int>(ref e);

    private static List<int> Drain(ref HeapPooledStack<int>.Enumerator e) => Drain<HeapPooledStack<int>.Enumerator, int>(ref e);

    private static List<int> Drain(ref HeapPooledQueue<int>.Enumerator e) => Drain<HeapPooledQueue<int>.Enumerator, int>(ref e);

    private static List<int> Drain(ref HeapRingBuffer<int>.Enumerator e) => Drain<HeapRingBuffer<int>.Enumerator, int>(ref e);

    private static List<int> EnumerateList(HeapPooledList<int> list)
    {
        var items = new List<int>();
        foreach (var item in list) items.Add(item);
        return items;
    }

    private static List<int> EnumerateFixed(HeapFixedSizeList<int> list)
    {
        var items = new List<int>();
        foreach (var item in list) items.Add(item);
        return items;
    }

    /// <summary>Rents arrays of exactly the requested length, so wrap-around is deterministic.</summary>
    private sealed class ExactSizePool<T> : System.Buffers.ArrayPool<T>
    {
        public override T[] Rent(int minimumLength) => new T[minimumLength];

        public override void Return(T[] array, bool clearArray = false) { }
    }
}
