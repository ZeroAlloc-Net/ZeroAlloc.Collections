using BenchmarkDotNet.Attributes;
using ZeroAlloc.Collections;

namespace ZeroAlloc.Collections.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class StackBenchmarks
{
    [Params(100, 1000, 10000)]
    public int N;

    [Benchmark(Baseline = true)]
    public int Stack_PushPop()
    {
        var stack = new Stack<int>();
        for (int i = 0; i < N; i++) stack.Push(i);
        int sum = 0;
        while (stack.Count > 0) sum += stack.Pop();
        return sum;
    }

    [Benchmark]
    public int PooledStack_PushPop()
    {
        using var stack = new PooledStack<int>();
        for (int i = 0; i < N; i++) stack.Push(i);
        int sum = 0;
        while (stack.TryPop(out var v)) sum += v;
        return sum;
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class QueueBenchmarks
{
    [Params(100, 1000, 10000)]
    public int N;

    [Benchmark(Baseline = true)]
    public int Queue_EnqueueDequeue()
    {
        var queue = new Queue<int>();
        for (int i = 0; i < N; i++) queue.Enqueue(i);
        int sum = 0;
        while (queue.Count > 0) sum += queue.Dequeue();
        return sum;
    }

    [Benchmark]
    public int PooledQueue_EnqueueDequeue()
    {
        using var queue = new PooledQueue<int>();
        for (int i = 0; i < N; i++) queue.Enqueue(i);
        int sum = 0;
        while (queue.TryDequeue(out var v)) sum += v;
        return sum;
    }
}
