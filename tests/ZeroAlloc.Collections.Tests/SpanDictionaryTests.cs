using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class SpanDictionaryTests
{
    [Fact]
    public void Add_And_TryGetValue()
    {
        var dict = new SpanDictionary<int, string>(4);
        dict.Add(1, "one");
        dict.Add(2, "two");
        Assert.True(dict.TryGetValue(1, out var v));
        Assert.Equal("one", v);
        dict.Dispose();
    }

    [Fact]
    public void Indexer_Set_And_Get()
    {
        var dict = new SpanDictionary<int, string>(4);
        dict[1] = "one";
        Assert.Equal("one", dict[1]);
        dict[1] = "ONE";
        Assert.Equal("ONE", dict[1]);
        dict.Dispose();
    }

    [Fact]
    public void ContainsKey()
    {
        var dict = new SpanDictionary<int, string>(4);
        dict.Add(1, "one");
        Assert.True(dict.ContainsKey(1));
        Assert.False(dict.ContainsKey(2));
        dict.Dispose();
    }

    [Fact]
    public void Remove_ExistingKey()
    {
        var dict = new SpanDictionary<int, string>(4);
        dict.Add(1, "one");
        Assert.True(dict.Remove(1));
        Assert.False(dict.ContainsKey(1));
        Assert.Equal(0, dict.Count);
        dict.Dispose();
    }

    [Fact]
    public void Remove_NonExistingKey_ReturnsFalse()
    {
        var dict = new SpanDictionary<int, string>(4);
        Assert.False(dict.Remove(99));
        dict.Dispose();
    }

    [Fact]
    public void Add_DuplicateKey_Throws()
    {
        var dict = new SpanDictionary<int, string>(4);
        dict.Add(1, "one");
        bool threw = false;
        try { dict.Add(1, "duplicate"); }
        catch (ArgumentException) { threw = true; }
        finally { dict.Dispose(); }
        Assert.True(threw);
    }

    [Fact]
    public void Grows_When_LoadFactor_Exceeded()
    {
        var dict = new SpanDictionary<int, int>(4);
        for (int i = 0; i < 100; i++)
            dict.Add(i, i * 10);
        Assert.Equal(100, dict.Count);
        for (int i = 0; i < 100; i++)
        {
            Assert.True(dict.TryGetValue(i, out var v));
            Assert.Equal(i * 10, v);
        }
        dict.Dispose();
    }

    [Fact]
    public void Indexer_Get_NonExistent_Throws()
    {
        var dict = new SpanDictionary<int, string>(4);
        bool threw = false;
        try { var _ = dict[99]; }
        catch (KeyNotFoundException) { threw = true; }
        finally { dict.Dispose(); }
        Assert.True(threw);
    }

    [Fact]
    public void Foreach_EnumeratesAllEntries()
    {
        var dict = new SpanDictionary<int, string>(8);
        dict.Add(1, "one");
        dict.Add(2, "two");
        dict.Add(3, "three");

        var results = new Dictionary<int, string>();
        foreach (var kvp in dict)
            results[kvp.Key] = kvp.Value;

        Assert.Equal(3, results.Count);
        Assert.Equal("one", results[1]);
        Assert.Equal("two", results[2]);
        Assert.Equal("three", results[3]);
        dict.Dispose();
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var dict = new SpanDictionary<int, int>(4);
        dict.Add(1, 10);
        dict.Add(2, 20);
        dict.Clear();
        Assert.Equal(0, dict.Count);
        Assert.False(dict.ContainsKey(1));
        dict.Dispose();
    }

    [Fact]
    public void Remove_Then_Add_SameKey()
    {
        var dict = new SpanDictionary<int, string>(4);
        dict.Add(1, "one");
        dict.Remove(1);
        dict.Add(1, "ONE");
        Assert.True(dict.TryGetValue(1, out var v));
        Assert.Equal("ONE", v);
        dict.Dispose();
    }

    [Fact]
    public void StringKeys_Work()
    {
        var dict = new SpanDictionary<string, int>(4);
        dict.Add("hello", 1);
        dict.Add("world", 2);
        Assert.True(dict.TryGetValue("hello", out var v));
        Assert.Equal(1, v);
        dict.Dispose();
    }
}
