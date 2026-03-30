using BenchmarkDotNet.Attributes;
using ZeroAlloc.Collections;

namespace ZeroAlloc.Collections.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class PooledListBenchmarks
{
    [Params(100, 1000, 10000)]
    public int N;

    [Benchmark(Baseline = true)]
    public int List_Add()
    {
        var list = new List<int>();
        for (int i = 0; i < N; i++) list.Add(i);
        return list.Count;
    }

    [Benchmark]
    public int PooledList_Add()
    {
        using var list = new PooledList<int>();
        for (int i = 0; i < N; i++) list.Add(i);
        return list.Count;
    }

    [Benchmark]
    public int List_Enumerate()
    {
        var list = new List<int>(N);
        for (int i = 0; i < N; i++) list.Add(i);
        int sum = 0;
        foreach (var item in list) sum += item;
        return sum;
    }

    [Benchmark]
    public int PooledList_Enumerate()
    {
        using var list = new PooledList<int>(N);
        for (int i = 0; i < N; i++) list.Add(i);
        int sum = 0;
        foreach (ref readonly var item in list) sum += item;
        return sum;
    }
}
