using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A fixed-capacity list backed by a caller-provided <see cref="Span{T}"/>.
/// Because the buffer is supplied by the caller (typically via <c>stackalloc</c>),
/// no heap allocation or pool interaction occurs — this type has no <c>Dispose</c>.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public ref struct FixedSizeList<T>
{
    private readonly Span<T> _buffer;
    private int _count;

    /// <summary>
    /// Initializes a new <see cref="FixedSizeList{T}"/> over the given buffer.
    /// </summary>
    /// <param name="buffer">
    /// The span that backs this list. Typically obtained via <c>stackalloc T[N]</c>.
    /// The capacity equals <paramref name="buffer"/>.Length.
    /// </param>
    public FixedSizeList(Span<T> buffer)
    {
        _buffer = buffer;
        _count = 0;
    }

    /// <summary>Gets the number of elements currently in the list.</summary>
    public readonly int Count => _count;

    /// <summary>Gets the maximum number of elements the list can hold.</summary>
    public readonly int Capacity => _buffer.Length;

    /// <summary>Gets a value indicating whether the list has reached capacity.</summary>
    public readonly bool IsFull => _count == _buffer.Length;

    /// <summary>
    /// Gets a reference to the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get.</param>
    /// <returns>A reference to the element at <paramref name="index"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is less than zero or greater than or equal to <see cref="Count"/>.
    /// </exception>
    public readonly ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return ref _buffer[index];
        }
    }

    /// <summary>
    /// Appends an item to the end of the list.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <exception cref="InvalidOperationException">The list is already at capacity.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        if (_count == _buffer.Length)
            throw new InvalidOperationException("FixedSizeList is full.");
        _buffer[_count++] = item;
    }

    /// <summary>
    /// Attempts to append an item to the end of the list.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <returns><c>true</c> if the item was added; <c>false</c> if the list is full.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(T item)
    {
        if (_count == _buffer.Length) return false;
        _buffer[_count++] = item;
        return true;
    }

    /// <summary>Returns a span over the active elements.</summary>
    public readonly Span<T> AsSpan() => _buffer[.._count];

    /// <summary>Returns a read-only span over the active elements.</summary>
    public readonly ReadOnlySpan<T> AsReadOnlySpan() => _buffer[.._count];

    /// <summary>Resets the count to zero. Does not clear buffer contents.</summary>
    public void Clear() => _count = 0;

    /// <summary>Returns an enumerator that iterates through the active elements.</summary>
    public readonly Enumerator GetEnumerator() => new(_buffer[.._count]);

    /// <summary>
    /// Enumerates the elements of a <see cref="FixedSizeList{T}"/>.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly Span<T> _span;
        private int _index;

        internal Enumerator(Span<T> span) { _span = span; _index = -1; }

        /// <summary>Advances the enumerator to the next element.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => ++_index < _span.Length;

        /// <summary>Gets the element at the current position of the enumerator.</summary>
        public readonly ref readonly T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _span[_index];
        }
    }
}
