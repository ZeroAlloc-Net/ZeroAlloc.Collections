using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Collections;

namespace ZeroAlloc.Collections.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class DictionaryBenchmarks
{
    [Params(100, 1000)]
    public int N;

    [Benchmark]
    public int Dictionary_AddLookup()
    {
        var dict = new Dictionary<int, int>();
        for (int i = 0; i < N; i++) dict[i] = i * 10;
        int sum = 0;
        for (int i = 0; i < N; i++) sum += dict[i];
        return sum;
    }

    [Benchmark]
    public int SpanDictionary_AddLookup()
    {
        var dict = new SpanDictionary<int, int>(N);
        for (int i = 0; i < N; i++) dict[i] = i * 10;
        int sum = 0;
        for (int i = 0; i < N; i++)
        {
            dict.TryGetValue(i, out var v);
            sum += v;
        }
        dict.Dispose();
        return sum;
    }

    // --- Concurrent-dictionary 3-way comparison ---

    [Benchmark(Baseline = true)]
    public int ConcurrentDictionary_Fill()
    {
        var dict = new ConcurrentDictionary<int, int>();
        for (int i = 0; i < N; i++) dict[i] = i * 10;
        int sum = 0;
        foreach (var kv in dict) sum += kv.Value;
        return sum;
    }

    [Benchmark]
    public int DictionaryPlusLock_Fill()
    {
        var dict = new Dictionary<int, int>();
        var sync = new object();
        for (int i = 0; i < N; i++) lock (sync) dict[i] = i * 10;
        int sum = 0;
        lock (sync) foreach (var kv in dict) sum += kv.Value;
        return sum;
    }

    [Benchmark]
    public int ConcurrentHeapSpanDictionary_Fill()
    {
        using var dict = new ConcurrentHeapSpanDictionary<int, int>(N);
        for (int i = 0; i < N; i++) dict.TryAdd(i, i * 10);
        int sum = 0;
        foreach (var kv in dict) sum += kv.Value;
        return sum;
    }
}
