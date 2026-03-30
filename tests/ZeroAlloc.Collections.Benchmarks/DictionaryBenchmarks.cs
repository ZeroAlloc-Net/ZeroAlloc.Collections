using BenchmarkDotNet.Attributes;
using ZeroAlloc.Collections;

namespace ZeroAlloc.Collections.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class DictionaryBenchmarks
{
    [Params(100, 1000)]
    public int N;

    [Benchmark(Baseline = true)]
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
}
