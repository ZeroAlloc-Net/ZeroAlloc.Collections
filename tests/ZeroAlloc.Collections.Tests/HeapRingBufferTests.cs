using System.Buffers;
using Xunit;

namespace ZeroAlloc.Collections.Tests;

public sealed class HeapRingBufferTests
{
    [Fact]
    public void Constructor_InvalidCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeapRingBuffer<int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeapRingBuffer<int>(-1));
    }

    [Fact]
    public void NewBuffer_IsEmpty_And_NotFull()
    {
        using var buf = new HeapRingBuffer<int>(4);
        Assert.True(buf.IsEmpty);
        Assert.False(buf.IsFull);
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void TryWrite_TryRead_Roundtrip()
    {
        using var buf = new HeapRingBuffer<int>(4);
        Assert.True(buf.TryWrite(10));
        Assert.True(buf.TryWrite(20));
        Assert.Equal(2, buf.Count);

        Assert.True(buf.TryRead(out var v1));
        Assert.Equal(10, v1);
        Assert.True(buf.TryRead(out var v2));
        Assert.Equal(20, v2);
        Assert.True(buf.IsEmpty);
    }

    [Fact]
    public void TryWrite_ReturnsFalse_WhenFull()
    {
        using var buf = new HeapRingBuffer<int>(2);
        Assert.True(buf.TryWrite(1));
        Assert.True(buf.TryWrite(2));
        Assert.True(buf.IsFull);
        Assert.False(buf.TryWrite(3));
    }

    [Fact]
    public void TryRead_ReturnsFalse_WhenEmpty()
    {
        using var buf = new HeapRingBuffer<int>(2);
        Assert.False(buf.TryRead(out _));
    }

    [Fact]
    public void TryPeek_ReturnsHead_WithoutRemoving()
    {
        using var buf = new HeapRingBuffer<int>(4);
        Assert.False(buf.TryPeek(out _));

        buf.TryWrite(42);
        buf.TryWrite(99);

        Assert.True(buf.TryPeek(out var peeked));
        Assert.Equal(42, peeked);
        Assert.Equal(2, buf.Count);
    }

    [Fact]
    public void WrapAround_Works()
    {
        using var buf = new HeapRingBuffer<int>(3);
        // Fill: [1,2,3]
        buf.TryWrite(1);
        buf.TryWrite(2);
        buf.TryWrite(3);
        Assert.True(buf.IsFull);

        // Read 2 items
        buf.TryRead(out var v1);
        buf.TryRead(out var v2);
        Assert.Equal(1, v1);
        Assert.Equal(2, v2);

        // Write 2 more, causing wrap
        buf.TryWrite(4);
        buf.TryWrite(5);
        Assert.True(buf.IsFull);

        // Should read in FIFO: 3, 4, 5
        buf.TryRead(out var v3);
        buf.TryRead(out var v4);
        buf.TryRead(out var v5);
        Assert.Equal(3, v3);
        Assert.Equal(4, v4);
        Assert.Equal(5, v5);
        Assert.True(buf.IsEmpty);
    }

    [Fact]
    public void Clear_ResetsBuffer()
    {
        using var buf = new HeapRingBuffer<int>(4);
        buf.TryWrite(1);
        buf.TryWrite(2);
        buf.Clear();

        Assert.True(buf.IsEmpty);
        Assert.Equal(0, buf.Count);
        Assert.False(buf.TryRead(out _));
    }

    [Fact]
    public void ToArray_ReturnsFifoOrder()
    {
        using var buf = new HeapRingBuffer<int>(4);
        buf.TryWrite(10);
        buf.TryWrite(20);
        buf.TryWrite(30);

        Assert.Equal(new[] { 10, 20, 30 }, buf.ToArray());
    }

    [Fact]
    public void ToArray_Empty_ReturnsEmptyArray()
    {
        using var buf = new HeapRingBuffer<int>(4);
        Assert.Empty(buf.ToArray());
    }

    [Fact]
    public void ToArray_AfterWrapAround()
    {
        using var buf = new HeapRingBuffer<int>(3);
        buf.TryWrite(1);
        buf.TryWrite(2);
        buf.TryWrite(3);
        buf.TryRead(out _);
        buf.TryRead(out _);
        buf.TryWrite(4);
        buf.TryWrite(5);

        Assert.Equal(new[] { 3, 4, 5 }, buf.ToArray());
    }

    [Fact]
    public void Enumeration_FifoOrder()
    {
        using var buf = new HeapRingBuffer<int>(4);
        buf.TryWrite(1);
        buf.TryWrite(2);
        buf.TryWrite(3);

        var list = new List<int>();
        foreach (var item in buf)
            list.Add(item);

        Assert.Equal(new[] { 1, 2, 3 }, list);
    }

    [Fact]
    public void Enumeration_AfterWrapAround()
    {
        using var buf = new HeapRingBuffer<int>(3);
        buf.TryWrite(1);
        buf.TryWrite(2);
        buf.TryWrite(3);
        buf.TryRead(out _);
        buf.TryWrite(4);

        var list = new List<int>();
        foreach (var item in buf)
            list.Add(item);

        Assert.Equal(new[] { 2, 3, 4 }, list);
    }

    [Fact]
    public void IReadOnlyCollection_Count()
    {
        using var buf = new HeapRingBuffer<int>(4);
        buf.TryWrite(1);
        buf.TryWrite(2);
        IReadOnlyCollection<int> col = buf;
        Assert.Equal(2, col.Count);
    }

    [Fact]
    public void Dispose_ReturnsToPool()
    {
        var pool = ArrayPool<int>.Create();
        var buf = new HeapRingBuffer<int>(4, pool);
        buf.TryWrite(42);
        buf.Dispose();

        // After dispose, the buffer should still be usable for Dispose (idempotent)
        buf.Dispose(); // no throw
    }

    [Fact]
    public void Dispose_WithCustomPool()
    {
        var pool = ArrayPool<string>.Create();
        var buf = new HeapRingBuffer<string>(4, pool);
        buf.TryWrite("hello");
        buf.Dispose();
        buf.Dispose(); // idempotent
    }

    [Fact]
    public void LinqWorksViaIEnumerable()
    {
        using var buf = new HeapRingBuffer<int>(8);
        for (int i = 1; i <= 5; i++)
            buf.TryWrite(i);

        Assert.Equal(15, buf.Sum());
        Assert.Equal(new[] { 2, 4 }, buf.Where(x => x % 2 == 0).ToArray());
    }
}
