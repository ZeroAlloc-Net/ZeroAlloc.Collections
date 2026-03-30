using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class HeapSpanDictionaryTests
{
    [Fact]
    public void Add_And_TryGetValue()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        dict.Add(2, "two");
        Assert.True(dict.TryGetValue(1, out var v));
        Assert.Equal("one", v);
    }

    [Fact]
    public void Indexer_Set_And_Get()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict[1] = "one";
        Assert.Equal("one", dict[1]);
        dict[1] = "ONE";
        Assert.Equal("ONE", dict[1]);
    }

    [Fact]
    public void Indexer_Get_NonExistent_Throws()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        Assert.Throws<KeyNotFoundException>(() => dict[99]);
    }

    [Fact]
    public void ContainsKey()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        Assert.True(dict.ContainsKey(1));
        Assert.False(dict.ContainsKey(2));
    }

    [Fact]
    public void Remove_ExistingKey()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        Assert.True(dict.Remove(1));
        Assert.False(dict.ContainsKey(1));
        Assert.Equal(0, dict.Count);
    }

    [Fact]
    public void Remove_NonExistingKey_ReturnsFalse()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        Assert.False(dict.Remove(99));
    }

    [Fact]
    public void Add_DuplicateKey_Throws()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        Assert.Throws<ArgumentException>(() => dict.Add(1, "duplicate"));
    }

    [Fact]
    public void Grows_When_LoadFactor_Exceeded()
    {
        using var dict = new HeapSpanDictionary<int, int>(4);
        for (int i = 0; i < 100; i++)
            dict.Add(i, i * 10);
        Assert.Equal(100, dict.Count);
        for (int i = 0; i < 100; i++)
        {
            Assert.True(dict.TryGetValue(i, out var v));
            Assert.Equal(i * 10, v);
        }
    }

    [Fact]
    public void Foreach_EnumeratesAllEntries()
    {
        using var dict = new HeapSpanDictionary<int, string>(8);
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
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        using var dict = new HeapSpanDictionary<int, int>(4);
        dict.Add(1, 10);
        dict.Add(2, 20);
        dict.Clear();
        Assert.Equal(0, dict.Count);
        Assert.False(dict.ContainsKey(1));
    }

    [Fact]
    public void Remove_Then_Add_SameKey()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        dict.Remove(1);
        dict.Add(1, "ONE");
        Assert.True(dict.TryGetValue(1, out var v));
        Assert.Equal("ONE", v);
    }

    [Fact]
    public void StringKeys_Work()
    {
        using var dict = new HeapSpanDictionary<string, int>(4);
        dict.Add("hello", 1);
        dict.Add("world", 2);
        Assert.True(dict.TryGetValue("hello", out var v));
        Assert.Equal(1, v);
    }

    [Fact]
    public void DefaultConstructor_Works()
    {
        using var dict = new HeapSpanDictionary<int, int>();
        dict.Add(1, 10);
        Assert.Equal(1, dict.Count);
    }

    [Fact]
    public void IsReadOnly_ReturnsFalse()
    {
        using var dict = new HeapSpanDictionary<int, int>();
        Assert.False(dict.IsReadOnly);
    }

    // --- IDictionary interface tests ---

    [Fact]
    public void IDictionary_Add_KeyValuePair()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        ICollection<KeyValuePair<int, string>> col = dict;
        col.Add(new KeyValuePair<int, string>(1, "one"));
        Assert.Equal("one", dict[1]);
    }

    [Fact]
    public void IDictionary_Contains_KeyValuePair()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        ICollection<KeyValuePair<int, string>> col = dict;
        Assert.True(col.Contains(new KeyValuePair<int, string>(1, "one")));
        Assert.False(col.Contains(new KeyValuePair<int, string>(1, "wrong")));
        Assert.False(col.Contains(new KeyValuePair<int, string>(99, "one")));
    }

    [Fact]
    public void IDictionary_Remove_KeyValuePair()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        ICollection<KeyValuePair<int, string>> col = dict;
        Assert.False(col.Remove(new KeyValuePair<int, string>(1, "wrong")));
        Assert.True(col.Remove(new KeyValuePair<int, string>(1, "one")));
        Assert.Equal(0, dict.Count);
    }

    [Fact]
    public void IDictionary_CopyTo()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        dict.Add(2, "two");

        var array = new KeyValuePair<int, string>[4];
        ((ICollection<KeyValuePair<int, string>>)dict).CopyTo(array, 1);

        var copied = array.Skip(1).Take(2).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(2, copied.Count);
        Assert.Equal("one", copied[1]);
        Assert.Equal("two", copied[2]);
    }

    // --- Keys / Values properties ---

    [Fact]
    public void Keys_ReturnsAllKeys()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        dict.Add(2, "two");
        dict.Add(3, "three");

        var keys = dict.Keys;
        Assert.Equal(3, keys.Count);
        Assert.Contains(1, keys);
        Assert.Contains(2, keys);
        Assert.Contains(3, keys);
    }

    [Fact]
    public void Values_ReturnsAllValues()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        dict.Add(2, "two");

        var values = dict.Values;
        Assert.Equal(2, values.Count);
        Assert.Contains("one", values);
        Assert.Contains("two", values);
    }

    // --- IReadOnlyDictionary interface ---

    [Fact]
    public void IReadOnlyDictionary_Access()
    {
        using var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        dict.Add(2, "two");

        IReadOnlyDictionary<int, string> rod = dict;
        Assert.Equal(2, rod.Count);
        Assert.Equal("one", rod[1]);
        Assert.True(rod.ContainsKey(2));
        Assert.True(rod.TryGetValue(1, out var v));
        Assert.Equal("one", v);
        Assert.Equal(2, rod.Keys.Count());
        Assert.Equal(2, rod.Values.Count());
    }

    [Fact]
    public void Dispose_ClearsState()
    {
        var dict = new HeapSpanDictionary<int, string>(4);
        dict.Add(1, "one");
        dict.Dispose();
        Assert.Equal(0, dict.Count);
    }
}
