using System.Collections;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

public sealed class HeapFixedSizeList<T> : IList<T>, IReadOnlyList<T>
{
    private readonly T[] _items;
    private int _count;

    public HeapFixedSizeList(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new T[capacity];
        _count = 0;
    }

    public int Count => _count;
    public int Capacity => _items.Length;
    public bool IsFull => _count == _items.Length;
    public bool IsReadOnly => false;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        if (_count == _items.Length)
            throw new InvalidOperationException("HeapFixedSizeList is full.");
        _items[_count++] = item;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(T item)
    {
        if (_count == _items.Length) return false;
        _items[_count++] = item;
        return true;
    }

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

    public bool Remove(T item)
    {
        int index = IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    public bool Contains(T item) => IndexOf(item) >= 0;

    public int IndexOf(T item)
    {
        return Array.IndexOf(_items, item, 0, _count);
    }

    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            Array.Clear(_items, 0, _count);
        _count = 0;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        Array.Copy(_items, 0, array, arrayIndex, _count);
    }

    public T[] ToArray()
    {
        if (_count == 0) return Array.Empty<T>();
        var result = new T[_count];
        Array.Copy(_items, result, _count);
        return result;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return _items[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
