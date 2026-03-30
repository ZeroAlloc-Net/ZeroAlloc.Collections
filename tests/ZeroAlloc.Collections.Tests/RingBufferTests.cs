using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class RingBufferTests
{
    [Fact]
    public void NewBuffer_IsEmpty()
    {
        using var buf = new RingBuffer<int>(4);
        Assert.True(buf.IsEmpty);
        Assert.False(buf.IsFull);
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void TryWrite_TryRead_SingleItem()
    {
        using var buf = new RingBuffer<int>(4);
        Assert.True(buf.TryWrite(42));
        Assert.True(buf.TryRead(out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryWrite_WhenFull_ReturnsFalse()
    {
        using var buf = new RingBuffer<int>(2);
        Assert.True(buf.TryWrite(1));
        Assert.True(buf.TryWrite(2));
        Assert.True(buf.IsFull);
        Assert.False(buf.TryWrite(3));
    }

    [Fact]
    public void TryRead_WhenEmpty_ReturnsFalse()
    {
        using var buf = new RingBuffer<int>(4);
        Assert.False(buf.TryRead(out _));
    }

    [Fact]
    public void Wraps_Around_Correctly()
    {
        using var buf = new RingBuffer<int>(4);
        buf.TryWrite(1);
        buf.TryWrite(2);
        buf.TryRead(out _);
        buf.TryRead(out _);
        buf.TryWrite(3);
        buf.TryWrite(4);
        buf.TryWrite(5);
        buf.TryWrite(6);
        Assert.True(buf.TryRead(out var v1));
        Assert.Equal(3, v1);
        Assert.True(buf.TryRead(out var v2));
        Assert.Equal(4, v2);
    }

    [Fact]
    public void TryPeek_ReturnsHeadWithoutRemoving()
    {
        using var buf = new RingBuffer<int>(4);
        buf.TryWrite(42);
        Assert.True(buf.TryPeek(out var value));
        Assert.Equal(42, value);
        Assert.Equal(1, buf.Count);
    }

    [Fact]
    public void TryPeek_WhenEmpty_ReturnsFalse()
    {
        using var buf = new RingBuffer<int>(4);
        Assert.False(buf.TryPeek(out _));
    }

    [Fact]
    public void Foreach_EnumeratesInFifoOrder()
    {
        using var buf = new RingBuffer<int>(4);
        buf.TryWrite(1);
        buf.TryWrite(2);
        buf.TryWrite(3);

        var results = new List<int>();
        foreach (var item in buf)
            results.Add(item);

        Assert.Equal(new[] { 1, 2, 3 }, results);
    }

    [Fact]
    public void Foreach_AfterWrap_EnumeratesCorrectly()
    {
        using var buf = new RingBuffer<int>(4);
        buf.TryWrite(1);
        buf.TryWrite(2);
        buf.TryRead(out _);
        buf.TryRead(out _);
        buf.TryWrite(3);
        buf.TryWrite(4);
        buf.TryWrite(5);

        var results = new List<int>();
        foreach (var item in buf)
            results.Add(item);

        Assert.Equal(new[] { 3, 4, 5 }, results);
    }

    [Fact]
    public void Clear_ResetsBuffer()
    {
        using var buf = new RingBuffer<int>(4);
        buf.TryWrite(1);
        buf.TryWrite(2);
        buf.Clear();
        Assert.True(buf.IsEmpty);
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var buf = new RingBuffer<int>(4);
        buf.Dispose();
        buf.Dispose();
    }
}
