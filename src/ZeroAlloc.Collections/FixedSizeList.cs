using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

public ref struct FixedSizeList<T>
{
    private readonly Span<T> _buffer;
    private int _count;

    public FixedSizeList(Span<T> buffer)
    {
        _buffer = buffer;
        _count = 0;
    }

    public readonly int Count => _count;
    public readonly int Capacity => _buffer.Length;
    public readonly bool IsFull => _count == _buffer.Length;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        if (_count == _buffer.Length)
            throw new InvalidOperationException("FixedSizeList is full.");
        _buffer[_count++] = item;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(T item)
    {
        if (_count == _buffer.Length) return false;
        _buffer[_count++] = item;
        return true;
    }

    public readonly Span<T> AsSpan() => _buffer[.._count];
    public readonly ReadOnlySpan<T> AsReadOnlySpan() => _buffer[.._count];

    public void Clear() => _count = 0;

    public readonly Enumerator GetEnumerator() => new(_buffer[.._count]);

    public ref struct Enumerator
    {
        private readonly Span<T> _span;
        private int _index;

        internal Enumerator(Span<T> span) { _span = span; _index = -1; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => ++_index < _span.Length;

        public readonly ref readonly T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _span[_index];
        }
    }
}
