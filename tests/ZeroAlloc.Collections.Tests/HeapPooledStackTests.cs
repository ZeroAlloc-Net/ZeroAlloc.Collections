using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class HeapPooledStackTests
{
    [Fact]
    public void NewStack_IsEmpty()
    {
        using var stack = new HeapPooledStack<int>();
        Assert.True(stack.IsEmpty);
        Assert.Equal(0, stack.Count);
    }

    [Fact]
    public void Push_Pop_Lifo()
    {
        using var stack = new HeapPooledStack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);
        Assert.True(stack.TryPop(out var v));
        Assert.Equal(3, v);
        Assert.True(stack.TryPop(out v));
        Assert.Equal(2, v);
        Assert.True(stack.TryPop(out v));
        Assert.Equal(1, v);
    }

    [Fact]
    public void TryPeek_ReturnsTopWithoutRemoving()
    {
        using var stack = new HeapPooledStack<int>();
        stack.Push(42);
        Assert.True(stack.TryPeek(out var v));
        Assert.Equal(42, v);
        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void TryPop_WhenEmpty_ReturnsFalse()
    {
        using var stack = new HeapPooledStack<int>();
        Assert.False(stack.TryPop(out _));
    }

    [Fact]
    public void TryPeek_WhenEmpty_ReturnsFalse()
    {
        using var stack = new HeapPooledStack<int>();
        Assert.False(stack.TryPeek(out _));
    }

    [Fact]
    public void Grows_Automatically()
    {
        using var stack = new HeapPooledStack<int>(2);
        for (int i = 0; i < 50; i++) stack.Push(i);
        Assert.Equal(50, stack.Count);
        for (int i = 49; i >= 0; i--)
        {
            Assert.True(stack.TryPop(out var v));
            Assert.Equal(i, v);
        }
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        using var stack = new HeapPooledStack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Clear();
        Assert.Equal(0, stack.Count);
        Assert.True(stack.IsEmpty);
    }

    [Fact]
    public void ToArray_CopiesInStackOrder()
    {
        using var stack = new HeapPooledStack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);
        Assert.Equal(new[] { 3, 2, 1 }, stack.ToArray());
    }

    [Fact]
    public void ToArray_WhenEmpty_ReturnsEmptyArray()
    {
        using var stack = new HeapPooledStack<int>();
        Assert.Empty(stack.ToArray());
    }

    [Fact]
    public void Enumeration_TopToBottom()
    {
        using var stack = new HeapPooledStack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);
        var results = new List<int>();
        foreach (var item in stack)
            results.Add(item);
        Assert.Equal(new[] { 3, 2, 1 }, results);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var stack = new HeapPooledStack<int>();
        stack.Push(1);
        stack.Dispose();
        stack.Dispose();
    }

    [Fact]
    public void Implements_IReadOnlyCollection()
    {
        using var stack = new HeapPooledStack<int>();
        stack.Push(10);
        stack.Push(20);
        IReadOnlyCollection<int> collection = stack;
        Assert.Equal(2, collection.Count);
        Assert.Equal(new[] { 20, 10 }, collection.ToArray());
    }

    [Fact]
    public void Implements_IDisposable()
    {
        IDisposable stack = new HeapPooledStack<int>();
        stack.Dispose();
    }

    [Fact]
    public void Push_AfterClear_Works()
    {
        using var stack = new HeapPooledStack<int>();
        stack.Push(1);
        stack.Clear();
        stack.Push(2);
        Assert.True(stack.TryPop(out var v));
        Assert.Equal(2, v);
    }

    [Fact]
    public void ReferenceType_ClearsOnPop()
    {
        using var stack = new HeapPooledStack<string>();
        stack.Push("hello");
        stack.Push("world");
        Assert.True(stack.TryPop(out var v));
        Assert.Equal("world", v);
        Assert.Equal(1, stack.Count);
    }
}
