using BenchmarkDotNet.Attributes;
using ZeroAlloc.Collections;

namespace ZeroAlloc.Collections.Benchmarks;

/// <summary>
/// One <c>foreach</c> over a filled Heap* collection, so the Allocated column shows what the
/// enumeration itself costs. The collections are built once in setup.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class HeapEnumerationBenchmarks
{
    [Params(100)]
    public int N;

    private List<int> _list = null!;
    private HeapPooledList<int> _pooledList = null!;
    private HeapPooledQueue<int> _pooledQueue = null!;
    private HeapPooledStack<int> _pooledStack = null!;
    private HeapRingBuffer<int> _ringBuffer = null!;
    private HeapFixedSizeList<int> _fixedSizeList = null!;
    private HeapSpanDictionary<int, int> _dictionary = null!;
    private ConcurrentHeapSpanDictionary<int, int> _concurrentDictionary = null!;

    [GlobalSetup]
    public void Setup()
    {
        _list = new List<int>(N);
        _pooledList = new HeapPooledList<int>(N);
        _pooledQueue = new HeapPooledQueue<int>(N);
        _pooledStack = new HeapPooledStack<int>(N);
        _ringBuffer = new HeapRingBuffer<int>(N);
        _fixedSizeList = new HeapFixedSizeList<int>(N);
        _dictionary = new HeapSpanDictionary<int, int>(N * 2);
        _concurrentDictionary = new ConcurrentHeapSpanDictionary<int, int>(N * 2);
        for (int i = 0; i < N; i++)
        {
            _list.Add(i);
            _pooledList.Add(i);
            _pooledQueue.Enqueue(i);
            _pooledStack.Push(i);
            _ringBuffer.TryWrite(i);
            _fixedSizeList.Add(i);
            _dictionary.Add(i, i);
            _concurrentDictionary.TryAdd(i, i);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pooledList.Dispose();
        _pooledQueue.Dispose();
        _pooledStack.Dispose();
        _ringBuffer.Dispose();
        _dictionary.Dispose();
        _concurrentDictionary.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int List_Foreach()
    {
        int sum = 0;
        foreach (var item in _list) sum += item;
        return sum;
    }

    [Benchmark]
    public int HeapPooledList_Foreach()
    {
        int sum = 0;
        foreach (var item in _pooledList) sum += item;
        return sum;
    }

    [Benchmark]
    public int HeapPooledQueue_Foreach()
    {
        int sum = 0;
        foreach (var item in _pooledQueue) sum += item;
        return sum;
    }

    [Benchmark]
    public int HeapPooledStack_Foreach()
    {
        int sum = 0;
        foreach (var item in _pooledStack) sum += item;
        return sum;
    }

    [Benchmark]
    public int HeapRingBuffer_Foreach()
    {
        int sum = 0;
        foreach (var item in _ringBuffer) sum += item;
        return sum;
    }

    [Benchmark]
    public int HeapFixedSizeList_Foreach()
    {
        int sum = 0;
        foreach (var item in _fixedSizeList) sum += item;
        return sum;
    }

    [Benchmark]
    public int HeapSpanDictionary_Foreach()
    {
        int sum = 0;
        foreach (var kv in _dictionary) sum += kv.Value;
        return sum;
    }

    [Benchmark]
    public int HeapSpanDictionary_KeysCount() => _dictionary.Keys.Count + _dictionary.Values.Count;

    [Benchmark]
    public int HeapSpanDictionary_ForeachKeys()
    {
        int sum = 0;
        foreach (var key in _dictionary.Keys) sum += key;
        return sum;
    }

    [Benchmark]
    public int ConcurrentHeapSpanDictionary_Foreach()
    {
        int sum = 0;
        foreach (var kv in _concurrentDictionary) sum += kv.Value;
        return sum;
    }
}
