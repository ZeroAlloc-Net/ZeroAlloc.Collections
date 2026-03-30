using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class HeapFixedSizeListTests
{
    [Fact]
    public void Add_WithinCapacity_Works()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        list.Add(2);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
    }

    [Fact]
    public void Add_BeyondCapacity_Throws()
    {
        var list = new HeapFixedSizeList<int>(2);
        list.Add(1);
        list.Add(2);
        Assert.Throws<InvalidOperationException>(() => list.Add(3));
    }

    [Fact]
    public void TryAdd_WithinCapacity_ReturnsTrue()
    {
        var list = new HeapFixedSizeList<int>(2);
        Assert.True(list.TryAdd(1));
    }

    [Fact]
    public void TryAdd_BeyondCapacity_ReturnsFalse()
    {
        var list = new HeapFixedSizeList<int>(1);
        Assert.True(list.TryAdd(1));
        Assert.False(list.TryAdd(2));
    }

    [Fact]
    public void Indexer_Get_ReturnsCorrectValue()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(10);
        list.Add(20);
        Assert.Equal(10, list[0]);
        Assert.Equal(20, list[1]);
    }

    [Fact]
    public void Indexer_Set_UpdatesValue()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        list[0] = 99;
        Assert.Equal(99, list[0]);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[-1]);
    }

    [Fact]
    public void Capacity_ReturnsConstructorValue()
    {
        var list = new HeapFixedSizeList<int>(16);
        Assert.Equal(16, list.Capacity);
    }

    [Fact]
    public void Count_ReflectsItems()
    {
        var list = new HeapFixedSizeList<int>(4);
        Assert.Equal(0, list.Count);
        list.Add(1);
        Assert.Equal(1, list.Count);
    }

    [Fact]
    public void IsFull_WhenAtCapacity()
    {
        var list = new HeapFixedSizeList<int>(2);
        Assert.False(list.IsFull);
        list.Add(1);
        list.Add(2);
        Assert.True(list.IsFull);
    }

    [Fact]
    public void IsReadOnly_ReturnsFalse()
    {
        var list = new HeapFixedSizeList<int>(4);
        Assert.False(list.IsReadOnly);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        list.Add(2);
        list.Clear();
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void Enumeration_YieldsAllItems()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        list.Add(2);
        list.Add(3);
        var results = new List<int>();
        foreach (var item in list)
            results.Add(item);
        Assert.Equal(new[] { 1, 2, 3 }, results);
    }

    [Fact]
    public void RemoveAt_RemovesAndShifts()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(10);
        list.Add(20);
        list.Add(30);
        list.RemoveAt(1);
        Assert.Equal(2, list.Count);
        Assert.Equal(10, list[0]);
        Assert.Equal(30, list[1]);
    }

    [Fact]
    public void RemoveAt_OutOfRange_Throws()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(1));
    }

    [Fact]
    public void Insert_ShiftsElements()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        list.Add(3);
        list.Insert(1, 2);
        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
        Assert.Equal(3, list[2]);
    }

    [Fact]
    public void Insert_WhenFull_Throws()
    {
        var list = new HeapFixedSizeList<int>(2);
        list.Add(1);
        list.Add(2);
        Assert.Throws<InvalidOperationException>(() => list.Insert(0, 0));
    }

    [Fact]
    public void Contains_FindsItem()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(10);
        list.Add(20);
        Assert.True(list.Contains(10));
        Assert.True(list.Contains(20));
        Assert.False(list.Contains(30));
    }

    [Fact]
    public void IndexOf_ReturnsCorrectIndex()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(10);
        list.Add(20);
        list.Add(30);
        Assert.Equal(0, list.IndexOf(10));
        Assert.Equal(1, list.IndexOf(20));
        Assert.Equal(2, list.IndexOf(30));
        Assert.Equal(-1, list.IndexOf(99));
    }

    [Fact]
    public void Remove_RemovesFirstOccurrence()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        list.Add(2);
        list.Add(3);
        Assert.True(list.Remove(2));
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(3, list[1]);
    }

    [Fact]
    public void Remove_NotFound_ReturnsFalse()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        Assert.False(list.Remove(99));
    }

    [Fact]
    public void CopyTo_CopiesElements()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        list.Add(2);
        var array = new int[4];
        list.CopyTo(array, 1);
        Assert.Equal(0, array[0]);
        Assert.Equal(1, array[1]);
        Assert.Equal(2, array[2]);
        Assert.Equal(0, array[3]);
    }

    [Fact]
    public void ToArray_ReturnsCorrectArray()
    {
        var list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        list.Add(2);
        var arr = list.ToArray();
        Assert.Equal(new[] { 1, 2 }, arr);
    }

    [Fact]
    public void ToArray_Empty_ReturnsEmptyArray()
    {
        var list = new HeapFixedSizeList<int>(4);
        var arr = list.ToArray();
        Assert.Empty(arr);
    }

    [Fact]
    public void IList_Interface_Works()
    {
        IList<int> list = new HeapFixedSizeList<int>(4);
        list.Add(1);
        list.Add(2);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0]);
    }

    [Fact]
    public void IReadOnlyList_Interface_Works()
    {
        IReadOnlyList<int> list = new HeapFixedSizeList<int>(4);
        ((IList<int>)list).Add(1);
        Assert.Equal(1, list.Count);
        Assert.Equal(1, list[0]);
    }
}
