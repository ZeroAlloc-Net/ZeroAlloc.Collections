using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

public sealed class HeapPooledList<T> : IList<T>, IReadOnlyList<T>, IDisposable
{
    private T[]? _array;
    private int _count;
    private readonly ArrayPool<T> _pool;

    public HeapPooledList() : this(0, ArrayPool<T>.Shared) { }
    public HeapPooledList(int capacity) : this(capacity, ArrayPool<T>.Shared) { }
    public HeapPooledList(int capacity, ArrayPool<T> pool)
    {
        _pool = pool;
        _array = capacity > 0 ? pool.Rent(capacity) : null;
        _count = 0;
    }

    public int Count => _count;
    public bool IsReadOnly => false;

    public T this[int index]
    {
        get { if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index)); return _array![index]; }
        set { if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index)); _array![index] = value; }
    }

    public void Add(T item) { if (_array is null || _count == _array.Length) Grow(); _array![_count++] = item; }

    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        if (_array is null || _count == _array.Length) Grow();
        if (index < _count) Array.Copy(_array!, index, _array!, index + 1, _count - index);
        _array![index] = item;
        _count++;
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        _count--;
        if (index < _count) Array.Copy(_array!, index + 1, _array!, index, _count - index);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) _array![_count] = default!;
    }

    public bool Remove(T item) { int i = IndexOf(item); if (i < 0) return false; RemoveAt(i); return true; }
    public bool Contains(T item) => IndexOf(item) >= 0;
    public int IndexOf(T item) => _array is null ? -1 : Array.IndexOf(_array, item, 0, _count);

    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && _array is not null)
            Array.Clear(_array, 0, _count);
        _count = 0;
    }

    public void CopyTo(T[] array, int arrayIndex) => Array.Copy(_array!, 0, array, arrayIndex, _count);

    public T[] ToArray()
    {
        if (_count == 0) return Array.Empty<T>();
        var result = new T[_count];
        Array.Copy(_array!, result, _count);
        return result;
    }

    public Span<T> AsSpan() => _array is null ? Span<T>.Empty : _array.AsSpan(0, _count);
    public ReadOnlySpan<T> AsReadOnlySpan() => AsSpan();

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
            _pool.Return(_array);
        }
        _array = newArray;
    }

    public void Dispose()
    {
        if (_array is not null)
        {
            _pool.Return(_array);
            _array = null;
        }
    }
}
