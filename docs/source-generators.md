---
id: source-generators
title: Source Generators
sidebar_position: 7
---

# Source Generators

ZeroAlloc.Collections includes three Roslyn incremental source generators that emit specialized, zero-allocation code at compile time. The generators run as part of `dotnet build` — no extra tooling is required. All generated code is emitted with `#nullable enable` and uses `[MethodImpl(AggressiveInlining)]` on hot-path methods.

## ZeroAllocList

Generates a fully specialized list implementation for a specific element type `T`. Because the generated code knows `T` at compile time, it eliminates interface dispatch on `ArrayPool<T>` calls and enables the JIT to inline add/index operations that would otherwise go through virtual dispatch.

### Attribute

```csharp
[ZeroAllocList(typeof(int))]
public partial struct IntList;
```

The attribute accepts a `Type` parameter specifying the element type. The target must be a `partial struct` or `partial class`.

### What It Generates

The generator emits a partial type that implements `IDisposable` with the following members:

| Member | Type | Description |
|--------|------|-------------|
| Default constructor | — | Creates an empty list using `ArrayPool<T>.Shared` |
| `(int capacity)` | Constructor | Pre-rents a buffer of at least the specified capacity |
| `(int capacity, ArrayPool<T> pool)` | Constructor | Uses a custom pool |
| `Count` | `int` | Number of active elements |
| `this[int index]` | `T` | Gets the element at the specified index |
| `Add(T item)` | `void` | Appends an item, growing if necessary |
| `AsSpan()` | `Span<T>` | Span over active elements |
| `AsReadOnlySpan()` | `ReadOnlySpan<T>` | Read-only span over active elements |
| `Clear()` | `void` | Resets count to zero |
| `ToArray()` | `T[]` | Copies active elements to a new array |
| `Dispose()` | `void` | Returns the rented buffer to the pool |
| `GetEnumerator()` | `Enumerator` | Returns a `ref struct` enumerator with `ref readonly T Current` |

### Example

```csharp
using ZeroAlloc.Collections;

[ZeroAllocList(typeof(int))]
public partial struct IntList;

// Usage
var list = new IntList(capacity: 64);
try
{
    for (int i = 0; i < 50; i++)
        list.Add(i);

    ReadOnlySpan<int> span = list.AsReadOnlySpan();
    Console.WriteLine($"Count: {list.Count}, First: {span[0]}, Last: {span[^1]}");
}
finally
{
    list.Dispose();
}
```

### Why Use This Over PooledList&lt;T&gt;?

`PooledList<T>` is generic — every call to `Add`, `AsSpan`, or `Dispose` goes through the generic `ArrayPool<T>` interface. The JIT can often devirtualize these, but not always (especially on older runtimes or when the pool instance is not statically known). The source-generated list emits concrete code for the specific `T`, guaranteeing inlining and eliminating virtual dispatch.

Use `PooledList<T>` when you need a general-purpose pooled list. Use `[ZeroAllocList]` on hot paths where the element type is known and you want maximum throughput.

## PooledCollection

Generates a strongly-typed pooled wrapper with automatic return-to-pool logic. This is similar to `ZeroAllocList` but is intended for domain-specific wrapper types where naming matters.

### Attribute

```csharp
[PooledCollection(typeof(Order))]
public partial struct OrderBuffer;
```

### What It Generates

The generator emits a partial type that implements `IDisposable` with the following members:

| Member | Type | Description |
|--------|------|-------------|
| Default constructor | — | Creates an empty collection using `ArrayPool<T>.Shared` |
| `(int capacity)` | Constructor | Pre-rents a buffer of at least the specified capacity |
| `(int capacity, ArrayPool<T> pool)` | Constructor | Uses a custom pool |
| `Count` | `int` | Number of active elements |
| `this[int index]` | `T` | Gets the element at the specified index |
| `Add(T item)` | `void` | Appends an item, growing if necessary |
| `Dispose()` | `void` | Returns the rented buffer to the pool |
| `GetEnumerator()` | `Enumerator` | Returns a `ref struct` enumerator with `ref readonly T Current` |

### Example

```csharp
using ZeroAlloc.Collections;

public record Order(string Id, decimal Total);

[PooledCollection(typeof(Order))]
public partial struct OrderBuffer;

// Usage
using var buffer = new OrderBuffer(capacity: 128);
foreach (var raw in incomingOrders)
{
    buffer.Add(new Order(raw.Id, raw.Total));
}

foreach (ref readonly var order in buffer)
{
    Process(order);
}
// buffer.Dispose() returns the array to the pool
```

## ZeroAllocEnumerable

Generates a zero-allocation `ref struct` enumerator for any type that has a `T[]` field and an `int` count field. This is useful when you have a custom collection or data structure and want `foreach` support without allocating an `IEnumerator<T>`.

### Attribute

```csharp
[ZeroAllocEnumerable]
public partial struct SensorReadings
{
    private double[] _values;
    private int _count;
}
```

### Requirements

The target type must have:
1. Exactly one non-static field of type `T[]` (any array type)
2. Exactly one non-static field of type `int` (used as the count)

The generator discovers these fields automatically — no configuration needed.

### What It Generates

The generator emits:

| Member | Type | Description |
|--------|------|-------------|
| `GetEnumerator()` | `Enumerator` | Returns a `ref struct` enumerator |
| `Enumerator.Current` | `ref readonly T` | The current element (by reference, no copy) |
| `Enumerator.MoveNext()` | `bool` | Advances to the next element |

### Example

```csharp
using ZeroAlloc.Collections;

[ZeroAllocEnumerable]
public partial struct SensorReadings
{
    private double[] _values;
    private int _count;

    public SensorReadings(int capacity)
    {
        _values = new double[capacity];
        _count = 0;
    }

    public void Record(double value)
    {
        _values[_count++] = value;
    }
}

// Usage — foreach uses the generated ref struct enumerator (zero allocation)
var readings = new SensorReadings(capacity: 256);
readings.Record(23.5);
readings.Record(24.1);
readings.Record(22.8);

foreach (ref readonly var value in readings)
{
    Console.WriteLine(value);
}
```

## How the Generators Work

All three generators use the Roslyn incremental generator pipeline (`IIncrementalGenerator`) for fast IDE responsiveness. The pipeline:

1. **Filter** — `ForAttributeWithMetadataName` identifies types annotated with the relevant attribute. Only matching syntax nodes enter the pipeline.
2. **Transform** — Extract the model (namespace, type name, element type, accessibility) from the semantic model.
3. **Emit** — `RegisterSourceOutput` generates the C# source text and adds it via `SourceProductionContext.AddSource`.

Because the pipeline is incremental, the generator only re-runs when the annotated type or its attribute arguments change. Editing unrelated files does not trigger regeneration.

### TFM Awareness

The generated code targets `netstandard2.1`, `net8.0`, and `net9.0`. On `net9.0`, the generators can emit `allows ref struct` generic constraints where applicable. On lower targets, these constraints are omitted so the code compiles without errors.

### Generated File Naming

Each generator produces a file named `{TypeName}.{GeneratorName}.g.cs`:

- `IntList.ZeroAllocList.g.cs`
- `OrderBuffer.PooledCollection.g.cs`
- `SensorReadings.ZeroAllocEnumerable.g.cs`
