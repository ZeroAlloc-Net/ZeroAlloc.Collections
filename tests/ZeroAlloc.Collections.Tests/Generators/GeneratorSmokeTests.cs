using Xunit;

namespace ZeroAlloc.Collections.Tests.Generators;

// These tests verify that the source generators produce compilable code
// by using the generated types at runtime.

[ZeroAllocList(typeof(int))]
public partial struct GeneratedIntList;

[ZeroAllocList(typeof(string))]
public partial struct GeneratedStringList;

[PooledCollection(typeof(double))]
public partial struct GeneratedDoubleBuffer;

[ZeroAllocEnumerable]
public partial struct CustomCollection
{
    private int[] _items;
    private int _count;

    public CustomCollection(int[] items, int count)
    {
        _items = items;
        _count = count;
    }
}

public class GeneratorSmokeTests
{
    [Fact]
    public void ZeroAllocList_Int_AddAndAccess()
    {
        var list = new GeneratedIntList();
        list.Add(42);
        Assert.Equal(1, list.Count);
        Assert.Equal(42, list[0]);
        list.Dispose();
    }

    [Fact]
    public void ZeroAllocList_String_Works()
    {
        var list = new GeneratedStringList();
        list.Add("hello");
        Assert.Equal("hello", list[0]);
        list.Dispose();
    }

    [Fact]
    public void PooledCollection_AddAndDispose()
    {
        var buf = new GeneratedDoubleBuffer();
        buf.Add(3.14);
        Assert.Equal(1, buf.Count);
        buf.Dispose();
    }

    [Fact]
    public void ZeroAllocEnumerable_Foreach_Works()
    {
        var coll = new CustomCollection(new[] { 1, 2, 3, 0, 0 }, 3);
        var results = new List<int>();
        foreach (var item in coll)
            results.Add(item);
        Assert.Equal(new[] { 1, 2, 3 }, results);
    }
}
