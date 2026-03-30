---
id: diagnostics
title: Compiler Diagnostics
sidebar_position: 8
---

# Compiler Diagnostics

ZeroAlloc.Collections includes a Roslyn analyzer that detects common mistakes at compile time. Diagnostics appear as warnings in your IDE and on `dotnet build` — you never discover a leaked pool buffer at runtime in production.

## Diagnostic Reference Table

| Code | Severity | Category | Title |
|------|----------|----------|-------|
| ZAC001 | Warning | Usage | Pooled collection should be disposed |

## ZAC001 — Pooled Collection Should Be Disposed

**What it means:** A pooled collection was created without a `using` statement or an explicit `Dispose()` call. This leaks the rented `ArrayPool` buffer, defeating the purpose of pool-backed collections and increasing GC pressure.

**Tracked types:** The analyzer checks for undisposed local variables of the following types from `ZeroAlloc.Collections`:

- `PooledList<T>`
- `PooledStack<T>`
- `PooledQueue<T>`
- `RingBuffer<T>`
- `SpanDictionary<TKey, TValue>`

**Message format:** `'{0}' should be disposed. Use a 'using' statement or call Dispose() explicitly.`

### Example That Triggers ZAC001

```csharp
void ProcessItems(int[] data)
{
    var list = new PooledList<int>(capacity: 64); // ZAC001: 'PooledList' should be disposed

    foreach (var item in data)
        list.Add(item);

    var result = list.ToArray();
    // list is never disposed — buffer is leaked back to GC instead of returned to pool
}
```

### How to Fix

Use a `using` declaration (C# 8+) so the collection is disposed at the end of the enclosing scope:

```csharp
void ProcessItems(int[] data)
{
    using var list = new PooledList<int>(capacity: 64); // No warning

    foreach (var item in data)
        list.Add(item);

    var result = list.ToArray();
    // list.Dispose() is called automatically here
}
```

Or use a `using` statement with an explicit block:

```csharp
void ProcessItems(int[] data)
{
    using (var list = new PooledList<int>(capacity: 64))
    {
        foreach (var item in data)
            list.Add(item);

        var result = list.ToArray();
    }
}
```

Or call `Dispose()` explicitly (less preferred — the `using` pattern is safer against exceptions):

```csharp
void ProcessItems(int[] data)
{
    var list = new PooledList<int>(capacity: 64);
    try
    {
        foreach (var item in data)
            list.Add(item);

        var result = list.ToArray();
    }
    finally
    {
        list.Dispose(); // No warning — Dispose() is called
    }
}
```

### What the Analyzer Checks

The analyzer runs on every local variable declaration. It triggers when all of the following are true:

1. The variable's type name matches one of the tracked types (`PooledList`, `PooledStack`, `PooledQueue`, `RingBuffer`, `SpanDictionary`)
2. The type is in the `ZeroAlloc.Collections` namespace
3. The declaration does not use the `using` keyword
4. The declaration is not inside a `using` statement block
5. No `Dispose()` call is found on that variable in the containing block after the declaration

### Types Not Tracked

The analyzer does not track:

- **`FixedSizeList<T>`** — it does not own memory and has no `Dispose()` method
- **`HeapFixedSizeList<T>`** — it allocates a plain array, not a pooled buffer
- **`HeapPooledList<T>`**, **`HeapPooledStack<T>`**, **`HeapPooledQueue<T>`**, **`HeapRingBuffer<T>`**, **`HeapSpanDictionary<TKey, TValue>`** — heap variants implement `IDisposable` and are covered by the standard CA2000 (Call Dispose on objects before losing scope) analyzer

## Suppressing ZAC001

If you intentionally manage the lifetime of a pooled collection outside the local scope (e.g., storing it in a field and disposing it later), suppress the warning with `#pragma`:

```csharp
#pragma warning disable ZAC001
var list = new PooledList<int>(capacity: 64);
#pragma warning restore ZAC001
// list will be disposed elsewhere
```

Or in your `.csproj` for project-wide suppression:

```xml
<PropertyGroup>
    <NoWarn>$(NoWarn);ZAC001</NoWarn>
</PropertyGroup>
```
