using System.Buffers;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A growable FIFO queue backed by a circular array rented from <see cref="ArrayPool{T}"/>.
/// </summary>
/// <typeparam name="T">The type of elements in the queue.</typeparam>
public ref struct PooledQueue<T>
{
    private T[]? _array;
    private int _head;
    private int _tail;
    private int _count;
    private readonly ArrayPool<T> _pool;

    /// <summary>
    /// Initializes a new <see cref="PooledQueue{T}"/> using <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    public PooledQueue() : this(0, ArrayPool<T>.Shared) { }

    /// <summary>
    /// Initializes a new <see cref="PooledQueue{T}"/> with the specified initial capacity,
    /// using <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    /// <param name="capacity">The initial capacity hint.</param>
    public PooledQueue(int capacity) : this(capacity, ArrayPool<T>.Shared) { }

    /// <summary>
    /// Initializes a new <see cref="PooledQueue{T}"/> with the specified initial capacity and pool.
    /// </summary>
    /// <param name="capacity">The initial capacity hint.</param>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> to rent from.</param>
    public PooledQueue(int capacity, ArrayPool<T> pool)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _pool = pool;
        _array = capacity > 0 ? pool.Rent(capacity) : null;
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    /// <summary>Gets the number of elements currently in the queue.</summary>
    public readonly int Count => _count;

    /// <summary>Gets a value indicating whether the queue contains no elements.</summary>
    public readonly bool IsEmpty => _count == 0;

    /// <summary>
    /// Adds an item to the tail of the queue, growing the internal buffer if necessary.
    /// </summary>
    /// <param name="item">The item to enqueue.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enqueue(T item)
    {
        if (_array is null || _count == _array.Length)
            Grow();

        _array![_tail] = item;
        _tail = (_tail + 1) % _array.Length;
        _count++;
    }

    /// <summary>
    /// Attempts to remove and return the item at the head of the queue.
    /// </summary>
    /// <param name="item">When this method returns <c>true</c>, contains the dequeued item.</param>
    /// <returns><c>true</c> if an item was dequeued; <c>false</c> if the queue is empty.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDequeue(out T item)
    {
        if (_count == 0) { item = default!; return false; }
        item = _array![_head];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            _array[_head] = default!;
        _head = (_head + 1) % _array.Length;
        _count--;
        return true;
    }

    /// <summary>
    /// Attempts to read the item at the head of the queue without removing it.
    /// </summary>
    /// <param name="item">When this method returns <c>true</c>, contains the item at the head.</param>
    /// <returns><c>true</c> if the queue is non-empty; <c>false</c> otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryPeek(out T item)
    {
        if (_count == 0) { item = default!; return false; }
        item = _array![_head];
        return true;
    }

    /// <summary>
    /// Removes all elements from the queue.
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
    /// Copies the queue elements to a new array in FIFO order.
    /// </summary>
    /// <returns>An array containing copies of the elements in FIFO order.</returns>
    public readonly T[] ToArray()
    {
        if (_count == 0) return Array.Empty<T>();
        var result = new T[_count];
        if (_head < _tail)
        {
            Array.Copy(_array!, _head, result, 0, _count);
        }
        else
        {
            int headToEnd = _array!.Length - _head;
            Array.Copy(_array, _head, result, 0, headToEnd);
            Array.Copy(_array, 0, result, headToEnd, _tail);
        }
        return result;
    }

    /// <summary>Returns an enumerator that iterates through the queue in FIFO order.</summary>
    public readonly Enumerator GetEnumerator() => new(_array!, _head, _count, _array?.Length ?? 0);

    /// <summary>
    /// Enumerates the elements of a <see cref="PooledQueue{T}"/> in FIFO order.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly T[] _array;
        private readonly int _head;
        private readonly int _count;
        private readonly int _capacity;
        private int _index;

        internal Enumerator(T[] array, int head, int count, int capacity)
        {
            _array = array;
            _head = head;
            _count = count;
            _capacity = capacity;
            _index = -1;
        }

        /// <summary>Advances the enumerator to the next element.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => ++_index < _count;

        /// <summary>Gets the element at the current position of the enumerator.</summary>
        public readonly T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _array[(_head + _index) % _capacity];
        }
    }

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

    private void Grow()
    {
        int newCapacity = _array is null ? 4 : _array.Length * 2;
        var newArray = _pool.Rent(newCapacity);
        if (_array is not null && _count > 0)
        {
            if (_head < _tail)
            {
                Array.Copy(_array, _head, newArray, 0, _count);
            }
            else
            {
                int headToEnd = _array.Length - _head;
                Array.Copy(_array, _head, newArray, 0, headToEnd);
                Array.Copy(_array, 0, newArray, headToEnd, _tail);
            }
            _pool.Return(_array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
        _array = newArray;
        _head = 0;
        _tail = _count;
    }
}
