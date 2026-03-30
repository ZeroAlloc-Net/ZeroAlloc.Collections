using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class PooledQueueTests
{
    [Fact]
    public void NewQueue_IsEmpty()
    {
        using var queue = new PooledQueue<int>();
        Assert.True(queue.IsEmpty);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Enqueue_Dequeue_Fifo()
    {
        using var queue = new PooledQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        Assert.True(queue.TryDequeue(out var v));
        Assert.Equal(1, v);
        Assert.True(queue.TryDequeue(out v));
        Assert.Equal(2, v);
    }

    [Fact]
    public void TryPeek_ReturnsHeadWithoutRemoving()
    {
        using var queue = new PooledQueue<int>();
        queue.Enqueue(42);
        Assert.True(queue.TryPeek(out var v));
        Assert.Equal(42, v);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TryDequeue_WhenEmpty_ReturnsFalse()
    {
        using var queue = new PooledQueue<int>();
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void TryPeek_WhenEmpty_ReturnsFalse()
    {
        using var queue = new PooledQueue<int>();
        Assert.False(queue.TryPeek(out _));
    }

    [Fact]
    public void Wraps_And_Grows()
    {
        using var queue = new PooledQueue<int>(4);
        for (int i = 0; i < 3; i++) queue.Enqueue(i);
        for (int i = 0; i < 3; i++) queue.TryDequeue(out _);
        // head is now advanced, fill past capacity to trigger grow
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
        using var queue = new PooledQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Clear();
        Assert.Equal(0, queue.Count);
        Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void Foreach_EnumeratesFifo()
    {
        using var queue = new PooledQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        var results = new List<int>();
        foreach (var item in queue)
            results.Add(item);
        Assert.Equal(new[] { 1, 2, 3 }, results);
    }

    [Fact]
    public void ToArray_CopiesInFifoOrder()
    {
        using var queue = new PooledQueue<int>();
        queue.Enqueue(10);
        queue.Enqueue(20);
        queue.Enqueue(30);
        Assert.Equal(new[] { 10, 20, 30 }, queue.ToArray());
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var queue = new PooledQueue<int>();
        queue.Enqueue(1);
        queue.Dispose();
        queue.Dispose();
    }
}
