using System.Buffers;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A stack-only list backed by <see cref="ArrayPool{T}"/> that returns its buffer on disposal.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public ref struct PooledList<T>
{
    private T[]? _items;
    private readonly ArrayPool<T>? _pool;
    private int _count;

    private const int DefaultCapacity = 4;

    /// <summary>
    /// Initializes a new empty <see cref="PooledList{T}"/>.
    /// </summary>
    public PooledList()
    {
        _items = null;
        _pool = ArrayPool<T>.Shared;
        _count = 0;
    }

    /// <summary>
    /// Initializes a new <see cref="PooledList{T}"/> with the specified initial capacity.
    /// </summary>
    /// <param name="capacity">The minimum initial capacity.</param>
    public PooledList(int capacity) : this(capacity, ArrayPool<T>.Shared)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="PooledList{T}"/> with the specified initial capacity and pool.
    /// </summary>
    /// <param name="capacity">The minimum initial capacity.</param>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> to rent from.</param>
    public PooledList(int capacity, ArrayPool<T> pool)
    {
        if (pool is null)
        {
            throw new ArgumentNullException(nameof(pool));
        }
        _pool = pool;
        _items = capacity > 0 ? pool.Rent(capacity) : null;
        _count = 0;
    }

    /// <summary>
    /// Gets the number of elements in the list.
    /// </summary>
    public readonly int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count;
    }

    /// <summary>
    /// Gets a reference to the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element.</param>
    /// <returns>A reference to the element at <paramref name="index"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is less than 0 or greater than or equal to <see cref="Count"/>.
    /// </exception>
    public readonly ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_count)
            {
                ThrowArgumentOutOfRange();
            }

            return ref _items![index];
        }
    }

    /// <summary>
    /// Appends an item to the end of the list.
    /// </summary>
    /// <param name="item">The item to add.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        T[]? items = _items;
        int count = _count;

        if (items is null || (uint)count >= (uint)items.Length)
        {
            Grow();
            items = _items;
        }

        items![count] = item;
        _count = count + 1;
    }

    /// <summary>
    /// Returns a <see cref="Span{T}"/> over the active elements.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Span<T> AsSpan()
    {
        return _items is null ? Span<T>.Empty : _items.AsSpan(0, _count);
    }

    /// <summary>
    /// Returns a <see cref="ReadOnlySpan{T}"/> over the active elements.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<T> AsReadOnlySpan()
    {
        return _items is null ? ReadOnlySpan<T>.Empty : new ReadOnlySpan<T>(_items, 0, _count);
    }

    /// <summary>
    /// Resets the count to zero. Clears references if <typeparamref name="T"/> is a reference type
    /// or contains references. The underlying buffer is retained.
    /// </summary>
    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && _items is not null)
        {
            Array.Clear(_items, 0, _count);
        }

        _count = 0;
    }

    /// <summary>
    /// Copies the active elements to a new array.
    /// </summary>
    /// <returns>A new array containing copies of the active elements.</returns>
    public readonly T[] ToArray()
    {
        if (_count == 0)
        {
            return Array.Empty<T>();
        }

        T[] result = new T[_count];
        Array.Copy(_items!, 0, result, 0, _count);
        return result;
    }

    /// <summary>
    /// Removes the element at the specified index and shifts subsequent elements left.
    /// </summary>
    /// <param name="index">The zero-based index of the element to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is less than 0 or greater than or equal to <see cref="Count"/>.
    /// </exception>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_count)
        {
            ThrowArgumentOutOfRange();
        }

        _count--;
        if (index < _count)
        {
            Array.Copy(_items!, index + 1, _items!, index, _count - index);
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _items![_count] = default!;
        }
    }

    /// <summary>
    /// Inserts an element at the specified index, shifting subsequent elements right.
    /// </summary>
    /// <param name="index">The zero-based index at which <paramref name="item"/> should be inserted.</param>
    /// <param name="item">The item to insert.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is less than 0 or greater than <see cref="Count"/>.
    /// </exception>
    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)_count)
        {
            ThrowArgumentOutOfRange();
        }

        if (_items is null || _count == _items.Length)
        {
            Grow();
        }

        if (index < _count)
        {
            Array.Copy(_items!, index, _items!, index + 1, _count - index);
        }

        _items![index] = item;
        _count++;
    }

    /// <summary>
    /// Determines whether the list contains the specified item.
    /// </summary>
    /// <param name="item">The item to locate.</param>
    /// <returns><see langword="true"/> if found; otherwise, <see langword="false"/>.</returns>
    public readonly bool Contains(T item)
    {
        return IndexOf(item) >= 0;
    }

    /// <summary>
    /// Returns the zero-based index of the first occurrence of the specified item, or -1 if not found.
    /// </summary>
    /// <param name="item">The item to locate.</param>
    /// <returns>The index of <paramref name="item"/>, or -1.</returns>
    public readonly int IndexOf(T item)
    {
        if (_items is null)
        {
            return -1;
        }

        return Array.IndexOf(_items, item, 0, _count);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the list.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Enumerator GetEnumerator() => new Enumerator(this);

    /// <summary>
    /// Returns the rented buffer to the pool. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        T[]? items = _items;
        if (items is not null)
        {
            _items = null;
            _count = 0;
            ArrayPool<T> pool = _pool ?? ArrayPool<T>.Shared;
            pool.Return(items, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    private void Grow()
    {
        int newCapacity = _items is null ? DefaultCapacity : _items.Length * 2;
        ArrayPool<T> pool = _pool ?? ArrayPool<T>.Shared;
        T[] newItems = pool.Rent(newCapacity);

        if (_items is not null)
        {
            Array.Copy(_items, 0, newItems, 0, _count);
            pool.Return(_items, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        _items = newItems;
    }

    private static void ThrowArgumentOutOfRange()
    {
#pragma warning disable MA0015
        throw new ArgumentOutOfRangeException("index");
#pragma warning restore MA0015
    }

    /// <summary>
    /// Enumerates the elements of a <see cref="PooledList{T}"/>.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly T[]? _items;
        private readonly int _count;
        private int _index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(PooledList<T> list)
        {
            _items = list._items;
            _count = list._count;
            _index = -1;
        }

        /// <summary>
        /// Gets a read-only reference to the current element.
        /// </summary>
        public readonly ref readonly T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _items![_index];
        }

        /// <summary>
        /// Advances the enumerator to the next element.
        /// </summary>
        /// <returns><see langword="true"/> if there is a next element; otherwise, <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            int index = _index + 1;
            if (index < _count)
            {
                _index = index;
                return true;
            }

            return false;
        }
    }
}
