# Cookbook: Game Loop Object Pooling

This recipe shows how to process game entities in an update loop with zero GC pauses, using `FixedSizeList<T>` for stack-allocated buffers and `PooledList<T>` for dynamic batches.

## The Problem

In a game engine running at 60 FPS, each frame has a 16ms budget. A single Gen0 GC collection takes 1-5ms, and Gen1 can take 10ms+. When your update loop allocates `List<Entity>` every frame to collect nearby entities, sort them, or filter by state, those micro-allocations accumulate and trigger GC pauses that cause visible stuttering.

```
Frame 1:  new List<Entity>()  → 0.8ms
Frame 2:  new List<Entity>()  → 0.6ms
...
Frame 60: GC Gen0 triggered   → 4.2ms  ← STUTTER
```

## The Solution

| Need                          | Collection              | Allocation |
|-------------------------------|-------------------------|------------|
| Fixed entity buffer per frame | `FixedSizeList<T>`      | Stack (stackalloc) |
| Dynamic batch (unknown size)  | `PooledList<T>`         | ArrayPool (returned on Dispose) |
| Iterating results             | `ref struct Enumerator` | None |

`FixedSizeList<T>` wraps a `Span<T>`, so when you back it with `stackalloc`, the entire collection lives on the stack. For cases where the count is unknown at compile time, `PooledList<T>` rents from `ArrayPool` and returns the buffer when disposed.

## Complete Example

```csharp
using ZeroAlloc.Collections;

public struct Entity
{
    public int Id;
    public float X;
    public float Y;
    public float Health;
    public EntityState State;
}

public enum EntityState { Active, Damaged, Dead }

public sealed class GameWorld
{
    private readonly Entity[] _entities;
    private int _entityCount;

    public GameWorld(int maxEntities)
    {
        _entities = new Entity[maxEntities];
        _entityCount = 0;
    }

    public ReadOnlySpan<Entity> Entities => _entities.AsSpan(0, _entityCount);

    /// <summary>
    /// Main update loop — called once per frame.
    /// Zero heap allocations inside this method.
    /// </summary>
    public void Update(float deltaTime)
    {
        ProcessPhysics(deltaTime);
        ProcessCombat();
        ProcessCleanup();
    }

    /// <summary>
    /// Uses FixedSizeList backed by stackalloc to collect nearby entities.
    /// Entirely stack-allocated — nothing touches the heap.
    /// </summary>
    private void ProcessPhysics(float deltaTime)
    {
        for (int i = 0; i < _entityCount; i++)
        {
            ref Entity entity = ref _entities[i];
            if (entity.State == EntityState.Dead) continue;

            // Stack-allocate a buffer for up to 16 nearby entities
            Span<Entity> buffer = stackalloc Entity[16];
            var nearby = new FixedSizeList<Entity>(buffer);

            FindNearby(entity, ref nearby);

            // Iterate with ref readonly — no copies
            foreach (ref readonly Entity neighbor in nearby)
            {
                float dx = neighbor.X - entity.X;
                float dy = neighbor.Y - entity.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                if (dist < 1.0f && dist > 0.001f)
                {
                    // Simple repulsion
                    entity.X -= (dx / dist) * deltaTime;
                    entity.Y -= (dy / dist) * deltaTime;
                }
            }
            // nearby goes out of scope — nothing to dispose, it's on the stack
        }
    }

    /// <summary>
    /// Uses PooledList when the result count is unknown at compile time.
    /// The pooled buffer is returned automatically via using.
    /// </summary>
    private void ProcessCombat()
    {
        // Collect all damaged entities — count is unknown
        using var damaged = new PooledList<int>(capacity: 32);

        for (int i = 0; i < _entityCount; i++)
        {
            if (_entities[i].State == EntityState.Damaged)
            {
                damaged.Add(i);
            }
        }

        // Process each damaged entity
        foreach (ref readonly int index in damaged)
        {
            ref Entity entity = ref _entities[index];
            entity.Health -= 10f;

            if (entity.Health <= 0)
            {
                entity.State = EntityState.Dead;
            }
        }
        // damaged.Dispose() returns buffer to ArrayPool here
    }

    /// <summary>
    /// Removes dead entities using FixedSizeList as a stack-allocated removal list.
    /// </summary>
    private void ProcessCleanup()
    {
        Span<int> removalBuffer = stackalloc int[64];
        var toRemove = new FixedSizeList<int>(removalBuffer);

        for (int i = 0; i < _entityCount; i++)
        {
            if (_entities[i].State == EntityState.Dead)
            {
                // TryAdd returns false if the buffer is full — we'll catch
                // the rest next frame rather than allocating
                if (!toRemove.TryAdd(i)) break;
            }
        }

        // Remove in reverse order to preserve indices
        ReadOnlySpan<int> indices = toRemove.AsReadOnlySpan();
        for (int i = indices.Length - 1; i >= 0; i--)
        {
            RemoveEntity(indices[i]);
        }
    }

    private void FindNearby(in Entity entity, ref FixedSizeList<Entity> result)
    {
        float range = 5.0f;
        for (int i = 0; i < _entityCount; i++)
        {
            ref Entity other = ref _entities[i];
            if (other.Id == entity.Id || other.State == EntityState.Dead) continue;

            float dx = other.X - entity.X;
            float dy = other.Y - entity.Y;
            if (dx * dx + dy * dy <= range * range)
            {
                if (!result.TryAdd(other)) return; // buffer full — stop searching
            }
        }
    }

    private void RemoveEntity(int index)
    {
        _entityCount--;
        if (index < _entityCount)
        {
            _entities[index] = _entities[_entityCount];
        }
        _entities[_entityCount] = default;
    }
}
```

## Game Loop Driver

```csharp
var world = new GameWorld(maxEntities: 10_000);

// Populate with entities...

var stopwatch = System.Diagnostics.Stopwatch.StartNew();
float lastTime = 0;

while (true) // game loop
{
    float currentTime = (float)stopwatch.Elapsed.TotalSeconds;
    float deltaTime = currentTime - lastTime;
    lastTime = currentTime;

    world.Update(deltaTime);

    // Render, input, etc.
    Thread.Sleep(1); // simplified frame pacing
}
```

## Why ref struct Prevents Accidental Heap Escapes

Both `FixedSizeList<T>` and `PooledList<T>` are `ref struct` types. The compiler enforces:

```csharp
// COMPILE ERROR: cannot store a ref struct in a field
class BadCache
{
    FixedSizeList<Entity> _cached; // CS8345
}

// COMPILE ERROR: cannot box a ref struct
object boxed = new FixedSizeList<Entity>(stackalloc Entity[8]); // CS0029

// COMPILE ERROR: cannot use in async method locals that cross await
async Task BadAsync()
{
    var list = new PooledList<int>(); // CS4012
    await Task.Delay(1);
    list.Add(1); // would use-after-return
}
```

This means if it compiles, the lifetime is safe. You cannot accidentally keep a reference to a stack-allocated buffer past the scope where it was created.

## Key Takeaways

- **`FixedSizeList<T>`** backed by `stackalloc` gives you a fully stack-allocated collection. Use it when you know the maximum count at compile time (e.g., "at most 16 neighbors").
- **`PooledList<T>`** is the fallback when the count is unknown. It rents from `ArrayPool` and has the same `ref struct` safety guarantees.
- Use **`TryAdd`** instead of `Add` when the buffer might fill up. It returns `false` instead of throwing, so you can gracefully stop collecting and catch the rest next frame.
- The **`foreach` enumerator** yields `ref readonly T`, so iterating a `FixedSizeList<Entity>` does not copy the `Entity` struct -- it reads directly from the stack buffer.
- **`ref struct` lifetime** is enforced by the compiler. You cannot box, heap-escape, or use these types across `await` boundaries. If it compiles, it is safe.

## Related

- [High-Throughput Message Processing](01-message-processing.md)
- [Zero-Alloc Request Parsing](03-request-parsing.md)
- [Custom Collection with Source Generator](04-custom-collection.md)
