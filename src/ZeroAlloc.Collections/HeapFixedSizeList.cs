using System.Collections;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A heap-storable fixed-capacity list backed by a plain managed array.
/// Unlike <see cref="FixedSizeList{T}"/> (a ref struct), this is a sealed class that can be
/// stored on the heap, passed across async boundaries, and used with interfaces.
/// The capacity is fixed at construction time and cannot grow.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public sealed class HeapFixedSizeList<T> : IList<T>, IReadOnlyList<T>
{
    private readonly T[] _items;
    private int _count;

    /// <summary>
    /// Initializes a new <see cref="HeapFixedSizeList{T}"/> with the specified capacity.
    /// </summary>
    /// <param name="capacity">The maximum number of elements the list can hold.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public HeapFixedSizeList(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new T[capacity];
        _count = 0;
    }

    /// <inheritdoc/>
    public int Count => _count;

    /// <summary>Gets the maximum number of elements the list can hold.</summary>
    public int Capacity => _items.Length;

    /// <summary>Gets a value indicating whether the list has reached capacity.</summary>
    public bool IsFull => _count == _items.Length;

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _items[index] = value;
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The list is already at capacity.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        if (_count == _items.Length)
            throw new InvalidOperationException("HeapFixedSizeList is full.");
        _items[_count++] = item;
    }

    /// <summary>
    /// Attempts to append an item to the end of the list.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <returns><c>true</c> if the item was added; <c>false</c> if the list is full.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(T item)
    {
        if (_count == _items.Length) return false;
        _items[_count++] = item;
        return true;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The list is already at capacity.</exception>
    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (_count == _items.Length)
            throw new InvalidOperationException("HeapFixedSizeList is full.");

        if (index < _count)
            Array.Copy(_items, index, _items, index + 1, _count - index);

        _items[index] = item;
        _count++;
    }

    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _count--;
        if (index < _count)
            Array.Copy(_items, index + 1, _items, index, _count - index);

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            _items[_count] = default!;
    }

    /// <inheritdoc/>
    public bool Remove(T item)
    {
        int index = IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    /// <inheritdoc/>
    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <inheritdoc/>
    public int IndexOf(T item) => Array.IndexOf(_items, item, 0, _count);

    /// <inheritdoc/>
    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            Array.Clear(_items, 0, _count);
        _count = 0;
    }

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0 || arrayIndex + _count > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (_count > 0)
            Array.Copy(_items, 0, array, arrayIndex, _count);
    }

    /// <summary>Copies the active elements to a new managed array.</summary>
    /// <returns>A new array containing the elements.</returns>
    public T[] ToArray()
    {
        if (_count == 0) return Array.Empty<T>();
        var result = new T[_count];
        Array.Copy(_items, result, _count);
        return result;
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return _items[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
