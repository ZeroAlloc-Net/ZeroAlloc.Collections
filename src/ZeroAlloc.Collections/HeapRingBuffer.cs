using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A heap-storable fixed-capacity circular buffer backed by <see cref="ArrayPool{T}"/>.
/// </summary>
/// <typeparam name="T">The type of elements in the buffer.</typeparam>
public sealed class HeapRingBuffer<T> : IReadOnlyCollection<T>, IDisposable
{
    private T[]? _array;
    private readonly int _capacity;
    private int _head;
    private int _tail;
    private int _count;
    private readonly ArrayPool<T> _pool;

    /// <summary>
    /// Initializes a new <see cref="HeapRingBuffer{T}"/> with the specified capacity,
    /// using <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    /// <param name="capacity">The maximum number of elements the buffer can hold.</param>
    public HeapRingBuffer(int capacity) : this(capacity, ArrayPool<T>.Shared) { }

    /// <summary>
    /// Initializes a new <see cref="HeapRingBuffer{T}"/> with the specified capacity and pool.
    /// </summary>
    /// <param name="capacity">The maximum number of elements the buffer can hold.</param>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> to rent from.</param>
    public HeapRingBuffer(int capacity, ArrayPool<T> pool)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _array = pool.Rent(capacity);
        _capacity = capacity;
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    /// <summary>Gets the number of elements currently in the buffer.</summary>
    public int Count => _count;

    /// <summary>Gets a value indicating whether the buffer contains no elements.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>Gets a value indicating whether the buffer is at capacity.</summary>
    public bool IsFull => _count == _capacity;

    /// <summary>
    /// Attempts to write an item to the tail of the buffer.
    /// </summary>
    /// <param name="item">The item to write.</param>
    /// <returns><c>true</c> if the item was written; <c>false</c> if the buffer is full.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWrite(T item)
    {
        if (_count == _capacity) return false;
        _array![_tail] = item;
        _tail = (_tail + 1) % _capacity;
        _count++;
        return true;
    }

    /// <summary>
    /// Attempts to read and remove the item at the head of the buffer.
    /// </summary>
    /// <param name="item">When this method returns <c>true</c>, contains the item read.</param>
    /// <returns><c>true</c> if an item was read; <c>false</c> if the buffer is empty.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead(out T item)
    {
        if (_count == 0) { item = default!; return false; }
        item = _array![_head];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            _array[_head] = default!;
        _head = (_head + 1) % _capacity;
        _count--;
        return true;
    }

    /// <summary>
    /// Attempts to peek at the item at the head of the buffer without removing it.
    /// </summary>
    /// <param name="item">When this method returns <c>true</c>, contains the item at the head.</param>
    /// <returns><c>true</c> if the buffer is non-empty; <c>false</c> otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPeek(out T item)
    {
        if (_count == 0) { item = default!; return false; }
        item = _array![_head];
        return true;
    }

    /// <summary>
    /// Removes all elements from the buffer.
    /// </summary>
    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && _array is not null && _count > 0)
        {
            // Only clear the live elements, not the entire rented buffer
            if (_head < _tail)
            {
                Array.Clear(_array, _head, _count);
            }
            else
            {
                Array.Clear(_array, _head, _array.Length - _head);
                if (_tail > 0) Array.Clear(_array, 0, _tail);
            }
        }
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    /// <summary>
    /// Copies elements to a new array in FIFO order.
    /// </summary>
    public T[] ToArray()
    {
        if (_count == 0) return Array.Empty<T>();

        var result = new T[_count];
        for (int i = 0; i < _count; i++)
            result[i] = _array![(_head + i) % _capacity];
        return result;
    }

    /// <summary>Returns an enumerator that iterates through the buffer in FIFO order.</summary>
    public IEnumerator<T> GetEnumerator()
    {
        int count = _count;
        int head = _head;
        var array = _array;
        for (int i = 0; i < count; i++)
            yield return array![((head + i) % _capacity)];
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns the rented array to the pool.
    /// </summary>
    public void Dispose()
    {
        if (_array is not null)
        {
            _pool.Return(_array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            _array = null;
        }
    }
}
