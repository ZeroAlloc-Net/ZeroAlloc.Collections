using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class FixedSizeListTests
{
    [Fact]
    public void Add_WithinCapacity_Works()
    {
        Span<int> buffer = stackalloc int[4];
        var list = new FixedSizeList<int>(buffer);
        list.Add(1);
        list.Add(2);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
    }

    [Fact]
    public void Add_BeyondCapacity_Throws()
    {
        Span<int> buffer = stackalloc int[2];
        var list = new FixedSizeList<int>(buffer);
        list.Add(1);
        list.Add(2);
        bool threw = false;
        try { list.Add(3); } catch (InvalidOperationException) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    public void TryAdd_BeyondCapacity_ReturnsFalse()
    {
        Span<int> buffer = stackalloc int[1];
        var list = new FixedSizeList<int>(buffer);
        Assert.True(list.TryAdd(1));
        Assert.False(list.TryAdd(2));
    }

    [Fact]
    public void AsSpan_ReturnsCorrectSlice()
    {
        Span<int> buffer = stackalloc int[8];
        var list = new FixedSizeList<int>(buffer);
        list.Add(10);
        list.Add(20);
        var span = list.AsSpan();
        Assert.Equal(2, span.Length);
        Assert.Equal(10, span[0]);
        Assert.Equal(20, span[1]);
    }

    [Fact]
    public void Capacity_ReturnsBufferLength()
    {
        Span<int> buffer = stackalloc int[16];
        var list = new FixedSizeList<int>(buffer);
        Assert.Equal(16, list.Capacity);
    }

    [Fact]
    public void IsFull_WhenAtCapacity()
    {
        Span<int> buffer = stackalloc int[2];
        var list = new FixedSizeList<int>(buffer);
        Assert.False(list.IsFull);
        list.Add(1);
        list.Add(2);
        Assert.True(list.IsFull);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        Span<int> buffer = stackalloc int[4];
        var list = new FixedSizeList<int>(buffer);
        list.Add(1);
        list.Add(2);
        list.Clear();
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void Foreach_EnumeratesAllItems()
    {
        Span<int> buffer = stackalloc int[4];
        var list = new FixedSizeList<int>(buffer);
        list.Add(1);
        list.Add(2);
        list.Add(3);
        var results = new List<int>();
        foreach (ref readonly var item in list)
            results.Add(item);
        Assert.Equal(new[] { 1, 2, 3 }, results);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        Span<int> buffer = stackalloc int[4];
        var list = new FixedSizeList<int>(buffer);
        list.Add(1);
        bool threw = false;
        try { var _ = list[1]; } catch (ArgumentOutOfRangeException) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    public void Indexer_Set_UpdatesValue()
    {
        Span<int> buffer = stackalloc int[4];
        var list = new FixedSizeList<int>(buffer);
        list.Add(1);
        list[0] = 99;
        Assert.Equal(99, list[0]);
    }
}
