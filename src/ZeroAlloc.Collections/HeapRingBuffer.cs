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
        if (++_tail == _capacity) _tail = 0;
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
        if (++_head == _capacity) _head = 0;
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
                // Use _capacity (not _array.Length) — the rented buffer may be larger than capacity
                Array.Clear(_array, _head, _capacity - _head);
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
        for (int i = 0, index = _head; i < _count; i++)
        {
            result[i] = _array![index];
            if (++index == _capacity) index = 0;
        }
        return result;
    }

    /// <summary>
    /// Returns a struct enumerator that iterates through the buffer in FIFO order, so a
    /// <c>foreach</c> does not allocate. Enumerating through <see cref="IEnumerable{T}"/> boxes it.
    /// </summary>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns the rented array to the pool and empties the buffer. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        var array = _array;
        if (array is not null)
        {
            _array = null;
            _head = 0;
            _tail = 0;
            _count = 0;
            _pool.Return(array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    /// <summary>
    /// Enumerates the elements of a <see cref="HeapRingBuffer{T}"/> in FIFO order. The enumerator
    /// visits the elements the buffer held when it was created, or when it was last reset, and ends
    /// early when the buffer is disposed.
    /// </summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly HeapRingBuffer<T> _buffer;
        private T[]? _array;
        private int _count;
        private int _position;
        private int _slot;
        private T _current;

        internal Enumerator(HeapRingBuffer<T> buffer)
        {
            _buffer = buffer;
            _array = buffer._array;
            _count = buffer._count;
            _position = -1;
            _slot = buffer._head;
            _current = default!;
        }

        /// <summary>Gets the element at the current position of the enumerator.</summary>
        public readonly T Current => _current;

        readonly object? IEnumerator.Current => _current;

        /// <summary>Advances the enumerator to the next element.</summary>
        /// <returns><see langword="true"/> if there is a next element; otherwise, <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            int position = _position + 1;
            // Dispose returns the captured array to the pool, where another renter may overwrite
            // it, so a disposed buffer ends the enumeration instead of reading that array.
            if (position < _count && _buffer._array is not null)
            {
                int slot = _slot;
                if (position > 0 && ++slot == _buffer._capacity) slot = 0;
                _slot = slot;
                _position = position;
                _current = _array![slot];
                return true;
            }
            _position = _count;
            _current = default!;
            return false;
        }

        /// <summary>Sets the enumerator back to its initial position, over the buffer's current contents.</summary>
        public void Reset()
        {
            _array = _buffer._array;
            _count = _buffer._count;
            _position = -1;
            _slot = _buffer._head;
            _current = default!;
        }

        /// <summary>Does nothing: the enumerator holds no resources.</summary>
        public readonly void Dispose() { }
    }
}
