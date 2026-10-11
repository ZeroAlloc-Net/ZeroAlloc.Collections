using BenchmarkDotNet.Attributes;
using ZeroAlloc.Collections;

namespace ZeroAlloc.Collections.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class RingBufferBenchmarks
{
    [Params(100, 1000)]
    public int N;

    [Benchmark(Baseline = true)]
    public int Queue_WriteRead()
    {
        var queue = new Queue<int>(N);
        for (int i = 0; i < N; i++) queue.Enqueue(i);
        int sum = 0;
        while (queue.TryDequeue(out var v)) sum += v;
        return sum;
    }

    [Benchmark]
    public int RingBuffer_WriteRead()
    {
        using var ring = new RingBuffer<int>(N);
        for (int i = 0; i < N; i++) ring.TryWrite(i);
        int sum = 0;
        while (ring.TryRead(out var v)) sum += v;
        return sum;
    }

    [Benchmark]
    public int HeapRingBuffer_WriteRead()
    {
        using var ring = new HeapRingBuffer<int>(N);
        for (int i = 0; i < N; i++) ring.TryWrite(i);
        int sum = 0;
        while (ring.TryRead(out var v)) sum += v;
        return sum;
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class FixedSizeListBenchmarks
{
    [Params(100, 1000)]
    public int N;

    [Benchmark(Baseline = true)]
    public int List_AddEnumerate()
    {
        var list = new List<int>(N);
        for (int i = 0; i < N; i++) list.Add(i);
        int sum = 0;
        foreach (var item in list) sum += item;
        return sum;
    }

    [Benchmark]
    public int FixedSizeList_AddEnumerate()
    {
        var list = new FixedSizeList<int>(stackalloc int[N]);
        for (int i = 0; i < N; i++) list.TryAdd(i);
        int sum = 0;
        foreach (var item in list) sum += item;
        return sum;
    }

    [Benchmark]
    public int HeapFixedSizeList_AddEnumerate()
    {
        var list = new HeapFixedSizeList<int>(N);
        for (int i = 0; i < N; i++) list.TryAdd(i);
        int sum = 0;
        foreach (var item in list) sum += item;
        return sum;
    }
}
