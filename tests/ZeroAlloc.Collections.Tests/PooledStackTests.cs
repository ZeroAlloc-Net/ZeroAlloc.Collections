using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class PooledStackTests
{
    [Fact]
    public void NewStack_IsEmpty()
    {
        using var stack = new PooledStack<int>();
        Assert.True(stack.IsEmpty);
        Assert.Equal(0, stack.Count);
    }

    [Fact]
    public void Push_Pop_Lifo()
    {
        using var stack = new PooledStack<int>();
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
        using var stack = new PooledStack<int>();
        stack.Push(42);
        Assert.True(stack.TryPeek(out var v));
        Assert.Equal(42, v);
        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void TryPop_WhenEmpty_ReturnsFalse()
    {
        using var stack = new PooledStack<int>();
        Assert.False(stack.TryPop(out _));
    }

    [Fact]
    public void TryPeek_WhenEmpty_ReturnsFalse()
    {
        using var stack = new PooledStack<int>();
        Assert.False(stack.TryPeek(out _));
    }

    [Fact]
    public void Grows_Automatically()
    {
        using var stack = new PooledStack<int>(2);
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
        using var stack = new PooledStack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Clear();
        Assert.Equal(0, stack.Count);
        Assert.True(stack.IsEmpty);
    }

    [Fact]
    public void Foreach_EnumeratesTopToBottom()
    {
        using var stack = new PooledStack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);
        var results = new List<int>();
        foreach (var item in stack)
            results.Add(item);
        Assert.Equal(new[] { 3, 2, 1 }, results);
    }

    [Fact]
    public void ToArray_CopiesInStackOrder()
    {
        using var stack = new PooledStack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);
        Assert.Equal(new[] { 3, 2, 1 }, stack.ToArray());
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var stack = new PooledStack<int>();
        stack.Push(1);
        stack.Dispose();
        stack.Dispose();
    }
}
