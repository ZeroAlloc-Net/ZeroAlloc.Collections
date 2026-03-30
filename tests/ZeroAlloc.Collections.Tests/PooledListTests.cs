using Xunit;

namespace ZeroAlloc.Collections.Tests;

public class PooledListTests
{
    [Fact]
    public void DefaultConstructor_CreatesEmptyList()
    {
        var list = new PooledList<int>();
        try
        {
            Assert.Equal(0, list.Count);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Constructor_WithCapacity_CreatesEmptyListWithCapacity()
    {
        var list = new PooledList<int>(16);
        try
        {
            Assert.Equal(0, list.Count);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var list = new PooledList<int>(4);
        list.Add(1);
        list.Dispose();
        list.Dispose(); // Should not throw.
    }

    [Fact]
    public void Add_SingleItem_IncrementsCount()
    {
        var list = new PooledList<int>();
        try
        {
            list.Add(42);
            Assert.Equal(1, list.Count);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Add_MultipleItems_AllAccessibleByIndex()
    {
        var list = new PooledList<int>(4);
        try
        {
            list.Add(10);
            list.Add(20);
            list.Add(30);

            Assert.Equal(3, list.Count);
            Assert.Equal(10, list[0]);
            Assert.Equal(20, list[1]);
            Assert.Equal(30, list[2]);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var list = new PooledList<int>(4);
        try
        {
            list.Add(1);

            bool threwForAbove = false;
            try { _ = list[1]; }
            catch (ArgumentOutOfRangeException) { threwForAbove = true; }
            Assert.True(threwForAbove);

            bool threwForNegative = false;
            try { _ = list[-1]; }
            catch (ArgumentOutOfRangeException) { threwForNegative = true; }
            Assert.True(threwForNegative);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Add_BeyondInitialCapacity_GrowsAutomatically()
    {
        var list = new PooledList<int>(2);
        try
        {
            for (int i = 0; i < 100; i++)
            {
                list.Add(i);
            }

            Assert.Equal(100, list.Count);
            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(i, list[i]);
            }
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Indexer_Set_UpdatesValue()
    {
        var list = new PooledList<int>(4);
        try
        {
            list.Add(1);
            list[0] = 99;

            Assert.Equal(99, list[0]);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void AsSpan_ReturnsCorrectSlice()
    {
        var list = new PooledList<int>(8);
        try
        {
            list.Add(1);
            list.Add(2);
            list.Add(3);

            Span<int> span = list.AsSpan();

            Assert.Equal(3, span.Length);
            Assert.Equal(1, span[0]);
            Assert.Equal(2, span[1]);
            Assert.Equal(3, span[2]);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void AsReadOnlySpan_ReturnsCorrectSlice()
    {
        var list = new PooledList<int>(8);
        try
        {
            list.Add(10);
            list.Add(20);

            ReadOnlySpan<int> span = list.AsReadOnlySpan();

            Assert.Equal(2, span.Length);
            Assert.Equal(10, span[0]);
            Assert.Equal(20, span[1]);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Clear_ResetsCountButKeepsBuffer()
    {
        var list = new PooledList<int>(8);
        try
        {
            list.Add(1);
            list.Add(2);
            list.Add(3);

            list.Clear();

            Assert.Equal(0, list.Count);

            // Buffer is still usable — we can add again without issues.
            list.Add(99);
            Assert.Equal(1, list.Count);
            Assert.Equal(99, list[0]);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void ToArray_CopiesElements()
    {
        var list = new PooledList<int>(4);
        try
        {
            list.Add(5);
            list.Add(10);
            list.Add(15);

            int[] array = list.ToArray();

            Assert.Equal(3, array.Length);
            Assert.Equal(5, array[0]);
            Assert.Equal(10, array[1]);
            Assert.Equal(15, array[2]);

            // Mutating the array should not affect the list.
            array[0] = 999;
            Assert.Equal(5, list[0]);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Foreach_EnumeratesAllItems()
    {
        var list = new PooledList<int>(4);
        try
        {
            list.Add(1);
            list.Add(2);
            list.Add(3);

            var collected = new List<int>();
            foreach (ref readonly int item in list)
            {
                collected.Add(item);
            }

            Assert.Equal(new[] { 1, 2, 3 }, collected);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Enumerator_EmptyList_NoIterations()
    {
        var list = new PooledList<int>();
        try
        {
            int iterations = 0;
            foreach (ref readonly int item in list)
            {
                iterations++;
            }

            Assert.Equal(0, iterations);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void RemoveAt_RemovesAndShifts()
    {
        var list = new PooledList<int>(8);
        try
        {
            list.Add(10);
            list.Add(20);
            list.Add(30);
            list.Add(40);

            list.RemoveAt(1); // Remove 20

            Assert.Equal(3, list.Count);
            Assert.Equal(10, list[0]);
            Assert.Equal(30, list[1]);
            Assert.Equal(40, list[2]);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Insert_ShiftsElementsRight()
    {
        var list = new PooledList<int>(8);
        try
        {
            list.Add(1);
            list.Add(3);
            list.Add(4);

            list.Insert(1, 2); // Insert 2 at index 1

            Assert.Equal(4, list.Count);
            Assert.Equal(1, list[0]);
            Assert.Equal(2, list[1]);
            Assert.Equal(3, list[2]);
            Assert.Equal(4, list[3]);
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void Contains_ReturnsTrueForExistingItem()
    {
        var list = new PooledList<int>(4);
        try
        {
            list.Add(10);
            list.Add(20);
            list.Add(30);

            Assert.True(list.Contains(20));
            Assert.False(list.Contains(99));
        }
        finally
        {
            list.Dispose();
        }
    }

    [Fact]
    public void IndexOf_ReturnsCorrectIndex()
    {
        var list = new PooledList<int>(4);
        try
        {
            list.Add(100);
            list.Add(200);
            list.Add(300);

            Assert.Equal(0, list.IndexOf(100));
            Assert.Equal(1, list.IndexOf(200));
            Assert.Equal(2, list.IndexOf(300));
            Assert.Equal(-1, list.IndexOf(999));
        }
        finally
        {
            list.Dispose();
        }
    }
}
