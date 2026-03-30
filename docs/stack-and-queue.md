---
id: stack-and-queue
title: PooledStack & PooledQueue
slug: /docs/stack-and-queue
description: LIFO stacks and FIFO queues backed by ArrayPool — available as ref structs and as classes.
sidebar_position: 5
---

# PooledStack & PooledQueue

These collections provide the classic stack (LIFO) and queue (FIFO) abstractions, backed by arrays rented from `ArrayPool<T>`. Both grow automatically when capacity is exceeded and return their buffers on disposal.

| Type | Kind | Pattern |
|---|---|---|
| `PooledStack<T>` | `ref struct` | LIFO -- last in, first out |
| `HeapPooledStack<T>` | `sealed class` | LIFO, heap-storable |
| `PooledQueue<T>` | `ref struct` | FIFO -- first in, first out |
| `HeapPooledQueue<T>` | `sealed class` | FIFO, heap-storable |

Use the ref struct variants for synchronous hot paths where zero GC pressure matters. Use the class variants when you need to store the collection in a field, pass it across `await` boundaries, or consume `IReadOnlyCollection<T>`.

## LIFO vs FIFO

```
Stack (LIFO):                   Queue (FIFO):

  Push(A), Push(B), Push(C)       Enqueue(A), Enqueue(B), Enqueue(C)

  top -> [ C ]                    head -> [ A ] [ B ] [ C ] <- tail
         [ B ]
         [ A ]

  TryPop -> C                     TryDequeue -> A
  TryPop -> B                     TryDequeue -> B
  TryPop -> A                     TryDequeue -> C
```

---

## PooledStack

### Construction

```csharp
// Default — empty, uses ArrayPool<T>.Shared
var stack = new PooledStack<int>();

// Pre-sized
var stack = new PooledStack<int>(32);

// Custom pool
var stack = new PooledStack<int>(32, customPool);
```

`HeapPooledStack<T>` offers the same three constructors.

### Push / TryPop / TryPeek

All removal methods use the `Try` pattern -- they return `false` when the stack is empty rather than throwing.

```csharp
using var stack = new PooledStack<string>();
stack.Push("first");
stack.Push("second");
stack.Push("third");

stack.TryPeek(out string top);   // true, top == "third" (not removed)
stack.TryPop(out string item);   // true, item == "third"
stack.TryPop(out item);          // true, item == "second"
stack.TryPop(out item);          // true, item == "first"
stack.TryPop(out item);          // false — stack is empty
```

### Auto-Grow

`Push` doubles the buffer capacity when it runs out of space. The default initial capacity is 4. The old buffer is returned to the pool after copying.

### Enumeration and ToArray

The ref struct variant provides a `ref struct Enumerator` that iterates **top-to-bottom** and returns `ref readonly T Current`. The class variant returns `IEnumerator<T>` in the same order.

`ToArray()` returns a new array in **top-first** order (the same order as enumeration).

```csharp
using var stack = new PooledStack<int>();
stack.Push(1);
stack.Push(2);
stack.Push(3);

foreach (ref readonly int item in stack)
    Console.Write($"{item} "); // 3 2 1

int[] arr = stack.ToArray(); // [3, 2, 1]
```

### AsSpan

`AsSpan()` returns a span over the internal buffer in **bottom-to-top** order (the raw storage order). This is useful for bulk processing but note that the order differs from enumeration order.

```csharp
Span<int> raw = stack.AsSpan(); // [1, 2, 3] — bottom to top
```

---

## PooledQueue

### Construction

```csharp
// Default — empty, uses ArrayPool<T>.Shared
var queue = new PooledQueue<int>();

// Pre-sized
var queue = new PooledQueue<int>(64);

// Custom pool
var queue = new PooledQueue<int>(64, customPool);
```

`HeapPooledQueue<T>` offers the same three constructors.

### Enqueue / TryDequeue / TryPeek

```csharp
using var queue = new PooledQueue<string>();
queue.Enqueue("alpha");
queue.Enqueue("beta");
queue.Enqueue("gamma");

queue.TryPeek(out string head);     // true, head == "alpha" (not removed)
queue.TryDequeue(out string item);  // true, item == "alpha"
queue.TryDequeue(out item);         // true, item == "beta"
queue.TryDequeue(out item);         // true, item == "gamma"
queue.TryDequeue(out item);         // false — queue is empty
```

### Auto-Grow and Circular Array Internals

`PooledQueue` uses a circular array internally: `head` points to the front of the queue and `tail` points to the next write position. Both wrap around to the beginning of the array when they reach the end. When the array is full, `Enqueue` rents a new buffer of double the size, linearizes the wrapped elements into the new buffer, and returns the old buffer to the pool.

```
Initial state (capacity 4):

  head  tail
   v     v
 [ A ][ B ][   ][   ]

After Dequeue(A) and Enqueue(C), Enqueue(D):

        head          tail
         v              v
 [   ][ B ][ C ][ D ]

After Enqueue(E) — tail wraps:

  tail  head
   v     v
 [ E ][ B ][ C ][ D ]   <- array is full

Enqueue(F) triggers grow:
  - Rents new array (capacity 8)
  - Linearizes: [B, C, D, E] into [B, C, D, E, _, _, _, _]
  - Writes F:   [B, C, D, E, F, _, _, _]
```

### Enumeration and ToArray

Both variants enumerate in **FIFO order** (head to tail). The ref struct variant uses a zero-allocation `ref struct Enumerator`; the class variant returns `IEnumerator<T>`.

`ToArray()` returns a new array in FIFO order.

```csharp
using var queue = new PooledQueue<int>();
queue.Enqueue(10);
queue.Enqueue(20);
queue.Enqueue(30);

foreach (int item in queue)
    Console.Write($"{item} "); // 10 20 30

int[] arr = queue.ToArray(); // [10, 20, 30]
```

---

## Clear and Dispose

Both stack and queue follow the same conventions:

- `Clear()` resets count (and head/tail for queue) to zero, clears references for reference types, and retains the rented buffer.
- `Dispose()` returns the rented buffer to the pool. Safe to call multiple times.

---

## Usage Example -- Depth-First Traversal with PooledStack

```csharp
public void DepthFirst(TreeNode root, Action<TreeNode> visit)
{
    using var stack = new PooledStack<TreeNode>(32);
    stack.Push(root);

    while (stack.TryPop(out var node))
    {
        visit(node);
        // Push children in reverse so left is processed first
        for (int i = node.Children.Count - 1; i >= 0; i--)
            stack.Push(node.Children[i]);
    }
}
```

## Usage Example -- Breadth-First Traversal with PooledQueue

```csharp
public void BreadthFirst(TreeNode root, Action<TreeNode> visit)
{
    using var queue = new PooledQueue<TreeNode>(32);
    queue.Enqueue(root);

    while (queue.TryDequeue(out var node))
    {
        visit(node);
        foreach (var child in node.Children)
            queue.Enqueue(child);
    }
}
```

---

## API Reference

### PooledStack&lt;T&gt; (ref struct)

| Member | Signature | Description |
|---|---|---|
| Constructor | `PooledStack()` | Empty stack, uses `ArrayPool<T>.Shared`. |
| Constructor | `PooledStack(int capacity)` | Pre-sized, uses `ArrayPool<T>.Shared`. |
| Constructor | `PooledStack(int capacity, ArrayPool<T> pool)` | Pre-sized with a custom pool. |
| Property | `int Count` | Number of elements in the stack. |
| Property | `bool IsEmpty` | `true` when `Count` is zero. |
| Method | `void Push(T item)` | Pushes an item onto the top, growing if necessary. |
| Method | `bool TryPop(out T item)` | Pops the top item; returns `false` if empty. |
| Method | `bool TryPeek(out T item)` | Reads the top item without removing; returns `false` if empty. |
| Method | `void Clear()` | Resets count to zero; retains buffer. |
| Method | `Span<T> AsSpan()` | Span over elements in bottom-to-top storage order. |
| Method | `T[] ToArray()` | New array in top-first order. |
| Method | `Enumerator GetEnumerator()` | Zero-alloc ref struct enumerator (top-to-bottom). |
| Method | `void Dispose()` | Returns the rented buffer to the pool. |

**Enumerator** (ref struct):

| Member | Signature | Description |
|---|---|---|
| Property | `ref readonly T Current` | Read-only reference to the current element. |
| Method | `bool MoveNext()` | Advances from top to bottom. |

### HeapPooledStack&lt;T&gt; (sealed class : IReadOnlyCollection&lt;T&gt;, IDisposable)

| Member | Signature | Description |
|---|---|---|
| Constructor | `HeapPooledStack()` | Empty stack, uses `ArrayPool<T>.Shared`. |
| Constructor | `HeapPooledStack(int capacity)` | Pre-sized, uses `ArrayPool<T>.Shared`. |
| Constructor | `HeapPooledStack(int capacity, ArrayPool<T> pool)` | Pre-sized with a custom pool. |
| Property | `int Count` | Number of elements in the stack. |
| Property | `bool IsEmpty` | `true` when `Count` is zero. |
| Method | `void Push(T item)` | Pushes an item onto the top, growing if necessary. |
| Method | `bool TryPop(out T item)` | Pops the top item; returns `false` if empty. |
| Method | `bool TryPeek(out T item)` | Reads the top item without removing; returns `false` if empty. |
| Method | `void Clear()` | Resets count to zero; retains buffer. |
| Method | `T[] ToArray()` | New array in top-first order. |
| Method | `IEnumerator<T> GetEnumerator()` | Returns an `IEnumerator<T>` (top-to-bottom). |
| Method | `void Dispose()` | Returns the rented buffer to the pool. |

### PooledQueue&lt;T&gt; (ref struct)

| Member | Signature | Description |
|---|---|---|
| Constructor | `PooledQueue()` | Empty queue, uses `ArrayPool<T>.Shared`. |
| Constructor | `PooledQueue(int capacity)` | Pre-sized, uses `ArrayPool<T>.Shared`. |
| Constructor | `PooledQueue(int capacity, ArrayPool<T> pool)` | Pre-sized with a custom pool. |
| Property | `int Count` | Number of elements in the queue. |
| Property | `bool IsEmpty` | `true` when `Count` is zero. |
| Method | `void Enqueue(T item)` | Adds to the tail, growing if necessary. |
| Method | `bool TryDequeue(out T item)` | Removes from the head; returns `false` if empty. |
| Method | `bool TryPeek(out T item)` | Reads the head without removing; returns `false` if empty. |
| Method | `void Clear()` | Resets head, tail, and count; retains buffer. |
| Method | `T[] ToArray()` | New array in FIFO order. |
| Method | `Enumerator GetEnumerator()` | Zero-alloc ref struct enumerator (FIFO order). |
| Method | `void Dispose()` | Returns the rented buffer to the pool. |

### HeapPooledQueue&lt;T&gt; (sealed class : IReadOnlyCollection&lt;T&gt;, IDisposable)

| Member | Signature | Description |
|---|---|---|
| Constructor | `HeapPooledQueue()` | Empty queue, uses `ArrayPool<T>.Shared`. |
| Constructor | `HeapPooledQueue(int capacity)` | Pre-sized, uses `ArrayPool<T>.Shared`. |
| Constructor | `HeapPooledQueue(int capacity, ArrayPool<T> pool)` | Pre-sized with a custom pool. |
| Property | `int Count` | Number of elements in the queue. |
| Property | `bool IsEmpty` | `true` when `Count` is zero. |
| Method | `void Enqueue(T item)` | Adds to the tail, growing if necessary. |
| Method | `bool TryDequeue(out T item)` | Removes from the head; returns `false` if empty. |
| Method | `bool TryPeek(out T item)` | Reads the head without removing; returns `false` if empty. |
| Method | `void Clear()` | Resets head, tail, and count; retains buffer. |
| Method | `T[] ToArray()` | New array in FIFO order. |
| Method | `IEnumerator<T> GetEnumerator()` | Returns an `IEnumerator<T>` (FIFO order). |
| Method | `void Dispose()` | Returns the rented buffer to the pool. |
