using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class HeapPooledListTests
{
    [Fact]
    public void Implements_IList()
    {
        using var list = new HeapPooledList<int>();
        IList<int> ilist = list;
        ilist.Add(1);
        Assert.Equal(1, ilist.Count);
        Assert.Equal(1, ilist[0]);
    }

    [Fact]
    public void Implements_IReadOnlyList()
    {
        using var list = new HeapPooledList<int>();
        list.Add(1);
        IReadOnlyList<int> ro = list;
        Assert.Equal(1, ro[0]);
    }

    [Fact]
    public void Implements_IDisposable()
    {
        IDisposable list = new HeapPooledList<int>();
        list.Dispose();
    }

    [Fact]
    public void Add_And_Enumerate()
    {
        using var list = new HeapPooledList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);
        Assert.Equal(new[] { 1, 2, 3 }, list.ToArray());
    }

    [Fact]
    public void Remove_Works()
    {
        using var list = new HeapPooledList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);
        Assert.True(list.Remove(2));
        Assert.Equal(new[] { 1, 3 }, list.ToArray());
        Assert.False(list.Remove(99));
    }

    [Fact]
    public void Insert_Works()
    {
        using var list = new HeapPooledList<int>();
        list.Add(1);
        list.Add(3);
        list.Insert(1, 2);
        Assert.Equal(new[] { 1, 2, 3 }, list.ToArray());
    }

    [Fact]
    public void CopyTo_Works()
    {
        using var list = new HeapPooledList<int>();
        list.Add(10);
        list.Add(20);
        var dest = new int[4];
        list.CopyTo(dest, 1);
        Assert.Equal(new[] { 0, 10, 20, 0 }, dest);
    }

    [Fact]
    public void CanBeStoredAsField()
    {
        var holder = new ListHolder<int>();
        holder.List.Add(42);
        Assert.Equal(42, holder.List[0]);
        holder.Dispose();
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        using var list = new HeapPooledList<int>();
        list.Add(1);
        list.Add(2);
        list.Clear();
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void Grows_Automatically()
    {
        using var list = new HeapPooledList<int>(2);
        for (int i = 0; i < 100; i++) list.Add(i);
        Assert.Equal(100, list.Count);
    }

    private class ListHolder<TItem> : IDisposable
    {
        public HeapPooledList<TItem> List { get; } = new();
        public void Dispose() => List.Dispose();
    }
}
