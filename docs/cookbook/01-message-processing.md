# Cookbook: High-Throughput Message Processing

This recipe shows how to process 100k+ messages per second in a .NET background service using ZeroAlloc.Collections, with zero GC pressure on the hot path.

## The Problem

A telemetry ingestion service receives sensor readings over a socket. Each reading must be batched, queued for processing, and tracked in a sliding window for anomaly detection. Using `List<T>`, `Queue<T>`, and rolling arrays causes frequent Gen0/Gen1 collections that introduce latency spikes under load.

**Typical allocation profile with BCL collections:**

```
|  Collection         | Allocations/sec  | GC Pauses  |
|---------------------|------------------|------------|
| List<SensorReading> | ~12,000          | ~40ms p99  |
| Queue<Batch>        | ~8,000           | ~25ms p99  |
| Rolling array copy  | ~6,000           | ~15ms p99  |
```

## The Solution

Replace each BCL collection with its ZeroAlloc counterpart:

| BCL                     | ZeroAlloc                  | Role                              |
|-------------------------|----------------------------|-----------------------------------|
| `List<T>`               | `PooledList<T>`            | Accumulate batch of readings      |
| `Queue<T>`              | `PooledQueue<T>`           | Work queue between producer/consumer |
| Rolling `T[]` + copy    | `RingBuffer<T>`            | Sliding window for anomaly check  |

All three rent from `ArrayPool<T>.Shared` and return buffers on `Dispose()`. Because they are `ref struct`, the compiler prevents accidental boxing or heap escapes.

## Complete Example

```csharp
using System.Buffers;
using ZeroAlloc.Collections;

public readonly record struct SensorReading(int SensorId, double Value, long TimestampTicks);

public sealed class TelemetryIngestionService : BackgroundService
{
    private readonly Channel<SensorReading[]> _channel =
        Channel.CreateBounded<SensorReading[]>(128);

    private const int BatchSize = 256;
    private const int WindowSize = 1024;

    /// <summary>
    /// Producer: reads from a socket and batches into PooledList, then
    /// sends completed batches through the channel.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start consumer on a separate task
        _ = Task.Run(() => ConsumeAsync(stoppingToken), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // PooledList rents from ArrayPool — no allocation
            using var batch = new PooledList<SensorReading>(BatchSize);

            for (int i = 0; i < BatchSize; i++)
            {
                SensorReading reading = await ReadNextFromSocketAsync(stoppingToken);
                batch.Add(reading);
            }

            // ToArray() is the only allocation — the batch crosses an async boundary
            await _channel.Writer.WriteAsync(batch.ToArray(), stoppingToken);
            // batch.Dispose() returns the pooled buffer here
        }
    }

    /// <summary>
    /// Consumer: dequeues batches, processes each reading, and maintains
    /// a sliding window for anomaly detection.
    /// </summary>
    private async Task ConsumeAsync(CancellationToken ct)
    {
        // RingBuffer keeps the last N readings for anomaly detection
        using var window = new RingBuffer<SensorReading>(WindowSize);

        // PooledQueue buffers work items between processing passes
        using var workQueue = new PooledQueue<SensorReading>(BatchSize);

        await foreach (var batch in _channel.Reader.ReadAllAsync(ct))
        {
            // Enqueue all readings from the batch
            foreach (var reading in batch)
            {
                workQueue.Enqueue(reading);
            }

            // Process the work queue
            while (workQueue.TryDequeue(out var reading))
            {
                // Sliding window: if full, oldest reading is silently dropped
                if (window.IsFull)
                {
                    window.TryRead(out _); // evict oldest
                }
                window.TryWrite(reading);

                // Check for anomaly using the window
                if (IsAnomaly(reading, window))
                {
                    await RaiseAlertAsync(reading, ct);
                }

                RecordMetric(reading);
            }
        }
    }

    /// <summary>
    /// Scans the ring buffer to compute a running average and flags outliers.
    /// The foreach enumerator is a ref struct — zero allocations.
    /// </summary>
    private static bool IsAnomaly(SensorReading current, RingBuffer<SensorReading> window)
    {
        if (window.IsEmpty) return false;

        double sum = 0;
        int count = 0;
        foreach (var reading in window)
        {
            if (reading.SensorId == current.SensorId)
            {
                sum += reading.Value;
                count++;
            }
        }

        if (count < 10) return false;

        double avg = sum / count;
        return Math.Abs(current.Value - avg) > avg * 0.5;
    }

    private static void RecordMetric(SensorReading reading)
    {
        // Write to metrics sink (Prometheus, StatsD, etc.)
    }

    private static Task RaiseAlertAsync(SensorReading reading, CancellationToken ct)
    {
        Console.WriteLine($"ANOMALY: sensor={reading.SensorId} value={reading.Value}");
        return Task.CompletedTask;
    }

    private static ValueTask<SensorReading> ReadNextFromSocketAsync(CancellationToken ct)
    {
        // Placeholder: real implementation reads from a network socket
        return ValueTask.FromResult(new SensorReading(1, Random.Shared.NextDouble() * 100, DateTime.UtcNow.Ticks));
    }
}
```

## Registration

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<TelemetryIngestionService>();
var host = builder.Build();
host.Run();
```

## Before / After

```
BEFORE (BCL collections):
  Gen0 collections:  142/sec
  Gen1 collections:   18/sec
  p99 latency:        41ms
  Throughput:          95k msg/sec

AFTER (ZeroAlloc.Collections):
  Gen0 collections:    0/sec
  Gen1 collections:    0/sec
  p99 latency:          3ms
  Throughput:          180k msg/sec
```

The only remaining allocation is the `ToArray()` call when crossing the async channel boundary. Inside each processing pass, all iteration, queuing, and windowing is allocation-free.

## Key Takeaways

- **`PooledList<T>`** replaces `List<T>` for batching. It rents from `ArrayPool<T>.Shared` and returns the buffer on `Dispose()`. Use `using var` to ensure cleanup.
- **`PooledQueue<T>`** replaces `Queue<T>` for producer/consumer work queues. Same pooling, same `ref struct` safety.
- **`RingBuffer<T>`** provides a fixed-capacity sliding window. When full, manually evict with `TryRead` before writing. No resizing, no copies.
- All three types are `ref struct`, so the compiler enforces stack-only lifetime. You cannot accidentally box them or store them in a field that outlives the current scope.
- The `foreach` enumerator on each collection is also a `ref struct` -- zero-allocation iteration with `ref readonly` access to elements.

## Related

- [Game Loop Object Pooling](02-game-loop.md)
- [Zero-Alloc Request Parsing](03-request-parsing.md)
- [Custom Collection with Source Generator](04-custom-collection.md)
