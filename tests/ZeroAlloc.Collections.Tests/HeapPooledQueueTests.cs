using System.Collections;
using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class HeapPooledQueueTests
{
    [Fact]
    public void NewQueue_IsEmpty()
    {
        using var queue = new HeapPooledQueue<int>();
        Assert.True(queue.IsEmpty);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Enqueue_Dequeue_Fifo()
    {
        using var queue = new HeapPooledQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        Assert.True(queue.TryDequeue(out var v));
        Assert.Equal(1, v);
        Assert.True(queue.TryDequeue(out v));
        Assert.Equal(2, v);
        Assert.True(queue.TryDequeue(out v));
        Assert.Equal(3, v);
    }

    [Fact]
    public void TryPeek_ReturnsHeadWithoutRemoving()
    {
        using var queue = new HeapPooledQueue<int>();
        queue.Enqueue(42);
        Assert.True(queue.TryPeek(out var v));
        Assert.Equal(42, v);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TryDequeue_WhenEmpty_ReturnsFalse()
    {
        using var queue = new HeapPooledQueue<int>();
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void TryPeek_WhenEmpty_ReturnsFalse()
    {
        using var queue = new HeapPooledQueue<int>();
        Assert.False(queue.TryPeek(out _));
    }

    [Fact]
    public void Wraps_And_Grows()
    {
        using var queue = new HeapPooledQueue<int>(4);
        // Fill partially and drain to advance head
        for (int i = 0; i < 3; i++) queue.Enqueue(i);
        for (int i = 0; i < 3; i++) queue.TryDequeue(out _);
        // head is now advanced; fill past capacity to trigger wrap + grow
        for (int i = 0; i < 10; i++) queue.Enqueue(i);
        Assert.Equal(10, queue.Count);
        for (int i = 0; i < 10; i++)
        {
            Assert.True(queue.TryDequeue(out var v));
            Assert.Equal(i, v);
        }
    }

    [Fact]
    public void Clear_ResetsQueue()
    {
        using var queue = new HeapPooledQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Clear();
        Assert.Equal(0, queue.Count);
        Assert.True(queue.IsEmpty);
        // Can still enqueue after clear
        queue.Enqueue(99);
        Assert.True(queue.TryDequeue(out var v));
        Assert.Equal(99, v);
    }

    [Fact]
    public void ToArray_CopiesInFifoOrder()
    {
        using var queue = new HeapPooledQueue<int>();
        queue.Enqueue(10);
        queue.Enqueue(20);
        queue.Enqueue(30);
        Assert.Equal(new[] { 10, 20, 30 }, queue.ToArray());
    }

    [Fact]
    public void ToArray_WhenEmpty_ReturnsEmptyArray()
    {
        using var queue = new HeapPooledQueue<int>();
        var arr = queue.ToArray();
        Assert.Empty(arr);
    }

    [Fact]
    public void ToArray_WrappedBuffer_CopiesCorrectly()
    {
        using var queue = new HeapPooledQueue<int>(4);
        // Advance head to force wrap
        queue.Enqueue(100);
        queue.Enqueue(200);
        queue.TryDequeue(out _);
        queue.TryDequeue(out _);
        // Now enqueue items that wrap around
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        queue.Enqueue(4);
        Assert.Equal(new[] { 1, 2, 3, 4 }, queue.ToArray());
    }

    [Fact]
    public void Enumeration_Fifo()
    {
        using var queue = new HeapPooledQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        var results = new List<int>();
        foreach (var item in queue)
            results.Add(item);
        Assert.Equal(new[] { 1, 2, 3 }, results);
    }

    [Fact]
    public void Enumeration_WrappedBuffer()
    {
        using var queue = new HeapPooledQueue<int>(4);
        queue.Enqueue(100);
        queue.Enqueue(200);
        queue.TryDequeue(out _);
        queue.TryDequeue(out _);
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        queue.Enqueue(4);
        var results = new List<int>();
        foreach (var item in queue)
            results.Add(item);
        Assert.Equal(new[] { 1, 2, 3, 4 }, results);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var queue = new HeapPooledQueue<int>();
        queue.Enqueue(1);
        queue.Dispose();
        queue.Dispose(); // should not throw
    }

    [Fact]
    public void Implements_IReadOnlyCollection()
    {
        using var queue = new HeapPooledQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        IReadOnlyCollection<int> roc = queue;
        Assert.Equal(2, roc.Count);
        // IEnumerable<T> via interface
        var list = new List<int>(roc);
        Assert.Equal(new[] { 1, 2 }, list);
    }

    [Fact]
    public void Implements_IEnumerable_NonGeneric()
    {
        using var queue = new HeapPooledQueue<string>();
        queue.Enqueue("a");
        queue.Enqueue("b");
        IEnumerable enumerable = queue;
        var results = new List<object?>();
        foreach (var item in enumerable)
            results.Add(item);
        Assert.Equal(new object[] { "a", "b" }, results);
    }

    [Fact]
    public void ReferenceType_ClearsOnDequeue()
    {
        using var queue = new HeapPooledQueue<string>();
        queue.Enqueue("hello");
        Assert.True(queue.TryDequeue(out var v));
        Assert.Equal("hello", v);
        Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void LargeGrowSequence()
    {
        using var queue = new HeapPooledQueue<int>();
        for (int i = 0; i < 1000; i++)
            queue.Enqueue(i);
        Assert.Equal(1000, queue.Count);
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(queue.TryDequeue(out var v));
            Assert.Equal(i, v);
        }
        Assert.True(queue.IsEmpty);
    }
}
