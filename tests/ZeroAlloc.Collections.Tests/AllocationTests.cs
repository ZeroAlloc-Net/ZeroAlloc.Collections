using ZeroAlloc.Collections.Tests.Generators;
using Xunit;

namespace ZeroAlloc.Collections.Tests;

/// <summary>
/// Allocation assertions for the paths the documentation calls allocation-free, and pinned
/// numbers for the paths that still allocate. Each action runs several times to warm up the JIT and
/// any lazily created state, then is measured with <see cref="GC.GetAllocatedBytesForCurrentThread"/>.
/// </summary>
public class AllocationTests
{
    // Results go to a field so the loops are not optimised away; GC.KeepAlive would box.
    private static int _sink;

    private const int WarmUpRuns = 64;
    private const int Runs = 64;

    private static long Measure(Action action)
    {
        for (int i = 0; i < WarmUpRuns; i++) action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Runs; i++) action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / Runs;
    }

    // --- ref struct collections: documented as allocation-free ---

    [Fact]
    public void PooledList_AddIndexForeach_AllocatesNothing()
    {
        long bytes = Measure(static () =>
        {
            using var list = new PooledList<int>(16);
            for (int i = 0; i < 16; i++) list.Add(i);
            int sum = list[3];
            foreach (ref readonly var item in list) sum += item;
            _sink = sum;
        });
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void PooledStackAndQueue_PushPopEnqueueDequeue_AllocateNothing()
    {
        long bytes = Measure(static () =>
        {
            using var stack = new PooledStack<int>(16);
            using var queue = new PooledQueue<int>(16);
            for (int i = 0; i < 16; i++)
            {
                stack.Push(i);
                queue.Enqueue(i);
            }
            int sum = 0;
            foreach (var item in stack) sum += item;
            foreach (var item in queue) sum += item;
            while (stack.TryPop(out var s)) sum += s;
            while (queue.TryDequeue(out var q)) sum += q;
            _sink = sum;
        });
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void RingBuffer_WriteReadForeach_AllocatesNothing()
    {
        long bytes = Measure(static () =>
        {
            using var ring = new RingBuffer<int>(16);
            for (int i = 0; i < 16; i++) ring.TryWrite(i);
            int sum = 0;
            foreach (var item in ring) sum += item;
            while (ring.TryRead(out var item)) sum += item;
            _sink = sum;
        });
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void FixedSizeList_AddForeach_AllocatesNothing()
    {
        long bytes = Measure(static () =>
        {
            var list = new FixedSizeList<int>(stackalloc int[16]);
            for (int i = 0; i < 16; i++) list.TryAdd(i);
            int sum = 0;
            foreach (ref readonly var item in list) sum += item;
            _sink = sum;
        });
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void SpanDictionary_AddLookupForeach_AllocatesNothing()
    {
        long bytes = Measure(static () =>
        {
            var dict = new SpanDictionary<int, int>(32);
            for (int i = 0; i < 16; i++) dict[i] = i;
            int sum = 0;
            for (int i = 0; i < 16; i++)
            {
                dict.TryGetValue(i, out var v);
                sum += v;
            }
            foreach (var kv in dict) sum += kv.Value;
            dict.Dispose();
            _sink = sum;
        });
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void GeneratedList_AddAndIndex_AllocatesNothing()
    {
        long bytes = Measure(static () =>
        {
            var list = new GeneratedIntList(16);
            for (int i = 0; i < 16; i++) list.Add(i);
            int sum = 0;
            for (int i = 0; i < list.Count; i++) sum += list[i];
            list.Dispose();
            _sink = sum;
        });
        Assert.Equal(0, bytes);
    }

    // --- Heap* collections ---

    [Fact]
    public void HeapSpanDictionary_AddLookup_AllocatesNothing()
    {
        using var dict = new HeapSpanDictionary<int, int>(64);
        long bytes = Measure(() =>
        {
            for (int i = 0; i < 16; i++) dict[i] = i;
            int sum = 0;
            for (int i = 0; i < 16; i++) sum += dict[i];
            _sink = sum;
        });
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void HeapSpanDictionary_KeysAndValuesAccess_AllocatesNothing()
    {
        using var dict = new HeapSpanDictionary<int, int>();
        for (int i = 0; i < 8; i++) dict[i] = i;
        long bytes = Measure(() =>
        {
            var keys = dict.Keys;
            var values = dict.Values;
            _sink = keys.Count + values.Count;
            _sink += keys.Contains(3) && values.Contains(4) ? 1 : 0;
            IReadOnlyDictionary<int, int> ro = dict;
            GC.KeepAlive(ro.Keys);
            GC.KeepAlive(ro.Values);
        });
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void HeapSpanDictionary_ForeachOverKeys_AllocatesOneEnumerator()
    {
        using var dict = new HeapSpanDictionary<int, int>();
        for (int i = 0; i < 8; i++) dict[i] = i;
        long bytes = Measure(() =>
        {
            int sum = 0;
            foreach (var key in dict.Keys) sum += key;
            _sink = sum;
        });
        // ICollection<TKey> forces a heap enumerator until the next major, see issue #152.
        Assert.InRange(bytes, 1, 48);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(1000)]
    public void ConcurrentHeapSpanDictionary_Foreach_AllocatesOnlyTheEnumerator(int count)
    {
        using var dict = new ConcurrentHeapSpanDictionary<int, int>();
        for (int i = 0; i < count; i++) dict.TryAdd(i, i);
        long bytes = Measure(() =>
        {
            int sum = 0;
            foreach (var kv in dict) sum += kv.Value;
            _sink = sum;
        });
        // The snapshot lives in a pooled buffer; only the enumerator object is allocated.
        Assert.InRange(bytes, 1, 48);
    }
}
