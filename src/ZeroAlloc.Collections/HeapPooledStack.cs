using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A heap-storable LIFO stack backed by <see cref="ArrayPool{T}"/> that returns its buffer on disposal.
/// </summary>
/// <typeparam name="T">The type of elements in the stack.</typeparam>
public sealed class HeapPooledStack<T> : IReadOnlyCollection<T>, IDisposable
{
    private T[]? _array;
    private readonly ArrayPool<T> _pool;
    private int _count;

    private const int DefaultCapacity = 4;

    /// <summary>
    /// Initializes a new empty <see cref="HeapPooledStack{T}"/>.
    /// </summary>
    public HeapPooledStack() : this(0, ArrayPool<T>.Shared) { }

    /// <summary>
    /// Initializes a new <see cref="HeapPooledStack{T}"/> with the specified initial capacity.
    /// </summary>
    /// <param name="capacity">The minimum initial capacity.</param>
    public HeapPooledStack(int capacity) : this(capacity, ArrayPool<T>.Shared) { }

    /// <summary>
    /// Initializes a new <see cref="HeapPooledStack{T}"/> with the specified initial capacity and pool.
    /// </summary>
    /// <param name="capacity">The minimum initial capacity.</param>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> to rent from.</param>
    public HeapPooledStack(int capacity, ArrayPool<T> pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _array = capacity > 0 ? pool.Rent(capacity) : null;
        _count = 0;
    }

    /// <summary>
    /// Gets the number of elements in the stack.
    /// </summary>
    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count;
    }

    /// <summary>
    /// Gets a value indicating whether the stack is empty.
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count == 0;
    }

    /// <summary>
    /// Pushes an item onto the top of the stack.
    /// </summary>
    /// <param name="item">The item to push.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(T item)
    {
        T[]? array = _array;
        int count = _count;

        if (array is null || (uint)count >= (uint)array.Length)
        {
            Grow();
            array = _array;
        }

        array![count] = item;
        _count = count + 1;
    }

    /// <summary>
    /// Attempts to pop an item from the top of the stack.
    /// </summary>
    /// <param name="item">When this method returns <see langword="true"/>, the popped item.</param>
    /// <returns><see langword="true"/> if an item was popped; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPop(out T item)
    {
        int count = _count;
        if (count == 0)
        {
            item = default!;
            return false;
        }

        int index = count - 1;
        item = _array![index];

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _array![index] = default!;
        }

        _count = index;
        return true;
    }

    /// <summary>
    /// Attempts to peek at the item on the top of the stack without removing it.
    /// </summary>
    /// <param name="item">When this method returns <see langword="true"/>, the top item.</param>
    /// <returns><see langword="true"/> if the stack is non-empty; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPeek(out T item)
    {
        int count = _count;
        if (count == 0)
        {
            item = default!;
            return false;
        }

        item = _array![count - 1];
        return true;
    }

    /// <summary>
    /// Resets the count to zero. Clears references if <typeparamref name="T"/> is a reference type
    /// or contains references. The underlying buffer is retained.
    /// </summary>
    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && _array is not null)
        {
            Array.Clear(_array, 0, _count);
        }

        _count = 0;
    }

    /// <summary>
    /// Copies the active elements to a new array in stack order (top first).
    /// </summary>
    /// <returns>A new array containing copies of the active elements in top-to-bottom order.</returns>
    public T[] ToArray()
    {
        if (_count == 0)
        {
            return Array.Empty<T>();
        }

        T[] result = new T[_count];
        for (int i = 0; i < _count; i++)
        {
            result[i] = _array![_count - 1 - i];
        }
        return result;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the stack from top to bottom.
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = _count - 1; i >= 0; i--)
            yield return _array![i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns the rented buffer to the pool. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        T[]? array = _array;
        if (array is not null)
        {
            _array = null;
            _count = 0;
            _pool.Return(array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    private void Grow()
    {
        int newCapacity = _array is null ? DefaultCapacity : _array.Length * 2;
        T[] newArray = _pool.Rent(newCapacity);

        if (_array is not null)
        {
            Array.Copy(_array, 0, newArray, 0, _count);
            _pool.Return(_array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        _array = newArray;
    }
}
