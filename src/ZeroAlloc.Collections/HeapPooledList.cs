using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A growable list backed by a buffer rented from <see cref="ArrayPool{T}"/>.
/// Unlike <see cref="PooledList{T}"/> (a ref struct), this is a sealed class that can be
/// stored on the heap, passed across async boundaries, and used with interfaces.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public sealed class HeapPooledList<T> : IList<T>, IReadOnlyList<T>, IDisposable
{
    private T[]? _array;
    private int _count;
    private readonly ArrayPool<T> _pool;

    /// <summary>
    /// Initializes a new <see cref="HeapPooledList{T}"/> using <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    public HeapPooledList() : this(0, ArrayPool<T>.Shared) { }

    /// <summary>
    /// Initializes a new <see cref="HeapPooledList{T}"/> with the specified initial capacity,
    /// using <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    /// <param name="capacity">The initial capacity hint.</param>
    public HeapPooledList(int capacity) : this(capacity, ArrayPool<T>.Shared) { }

    /// <summary>
    /// Initializes a new <see cref="HeapPooledList{T}"/> with the specified initial capacity and pool.
    /// </summary>
    /// <param name="capacity">The initial capacity hint.</param>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> to rent from.</param>
    public HeapPooledList(int capacity, ArrayPool<T> pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _array = capacity > 0 ? pool.Rent(capacity) : null;
        _count = 0;
    }

    /// <inheritdoc/>
    public int Count => _count;

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public T this[int index]
    {
        get { if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index)); return _array![index]; }
        set { if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index)); _array![index] = value; }
    }

    /// <inheritdoc/>
    public void Add(T item) { if (_array is null || _count == _array.Length) Grow(); _array![_count++] = item; }

    /// <inheritdoc/>
    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        if (_array is null || _count == _array.Length) Grow();
        if (index < _count) Array.Copy(_array!, index, _array!, index + 1, _count - index);
        _array![index] = item;
        _count++;
    }

    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        _count--;
        if (index < _count) Array.Copy(_array!, index + 1, _array!, index, _count - index);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) _array![_count] = default!;
    }

    /// <inheritdoc/>
    public bool Remove(T item) { int i = IndexOf(item); if (i < 0) return false; RemoveAt(i); return true; }

    /// <inheritdoc/>
    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <inheritdoc/>
    public int IndexOf(T item) => _array is null ? -1 : Array.IndexOf(_array, item, 0, _count);

    /// <inheritdoc/>
    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && _array is not null)
            Array.Clear(_array, 0, _count);
        _count = 0;
    }

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0 || arrayIndex + _count > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (_count > 0)
            Array.Copy(_array!, 0, array, arrayIndex, _count);
    }

    /// <summary>
    /// Copies the active elements to a new managed array.
    /// </summary>
    /// <returns>A new array containing the elements.</returns>
    public T[] ToArray()
    {
        if (_count == 0) return Array.Empty<T>();
        var result = new T[_count];
        Array.Copy(_array!, result, _count);
        return result;
    }

    /// <summary>Returns a span over the active elements.</summary>
    public Span<T> AsSpan() => _array is null ? Span<T>.Empty : _array.AsSpan(0, _count);

    /// <summary>Returns a read-only span over the active elements.</summary>
    public ReadOnlySpan<T> AsReadOnlySpan() => AsSpan();

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return _array![i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Grow()
    {
        int newCapacity = _array is null ? 4 : _array.Length * 2;
        var newArray = _pool.Rent(newCapacity);
        if (_array is not null)
        {
            Array.Copy(_array, newArray, _count);
            _pool.Return(_array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
        _array = newArray;
    }

    /// <summary>
    /// Returns the rented buffer to the pool.
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
