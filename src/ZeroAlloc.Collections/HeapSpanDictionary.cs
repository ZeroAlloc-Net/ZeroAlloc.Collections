using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A heap-storable dictionary using open addressing with linear probing.
/// Same hash map logic as <see cref="SpanDictionary{TKey, TValue}"/> but usable as a class field.
/// The backing entry array is rented from <see cref="ArrayPool{T}"/> and returned on <see cref="Dispose"/>.
/// </summary>
/// <typeparam name="TKey">The type of keys.</typeparam>
/// <typeparam name="TValue">The type of values.</typeparam>
public sealed class HeapSpanDictionary<TKey, TValue>
    : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IDisposable
{
    private enum EntryState : byte
    {
        Empty,
        Occupied,
        Deleted
    }

    private struct Entry
    {
        public TKey Key;
        public TValue Value;
        public int HashCode;
        public EntryState State;
    }

    private Entry[]? _entries;
    private int _count;
    // Bumped when a key is added, the table grows, or the dictionary is cleared or disposed.
    // Removing a key or overwriting a value keeps it, matching Dictionary<TKey, TValue>.
    private int _version;
    private KeyCollection? _keys;
    private ValueCollection? _values;
    private readonly EqualityComparer<TKey> _comparer;
    private readonly ArrayPool<Entry> _pool;

    private const int DefaultCapacity = 4;

    /// <summary>
    /// Initializes a new <see cref="HeapSpanDictionary{TKey, TValue}"/> with default capacity.
    /// The backing entry array is rented from <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    public HeapSpanDictionary() : this(DefaultCapacity) { }

    /// <summary>
    /// Initializes a new <see cref="HeapSpanDictionary{TKey, TValue}"/> with the specified capacity.
    /// The backing entry array is rented from <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    /// <param name="capacity">The initial capacity.</param>
    public HeapSpanDictionary(int capacity)
    {
        if (capacity < 1) capacity = DefaultCapacity;
        _pool = ArrayPool<Entry>.Shared;
        _entries = _pool.Rent(capacity);
        // ArrayPool may return a dirty buffer — always clear so all slots start as Empty
        Array.Clear(_entries, 0, _entries.Length);
        _count = 0;
        _comparer = EqualityComparer<TKey>.Default;
    }

    /// <inheritdoc/>
    public int Count => _count;

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public TValue this[TKey key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (TryGetValue(key, out var value))
                return value;
            ThrowKeyNotFound(key);
            return default!;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            TryInsert(key, value, insertOnly: false);
        }
    }

    /// <summary>
    /// Gets a read-only, live view of the keys. The view is created once and cached, so reading
    /// this property does not allocate. Adding a key or clearing the dictionary while the view is
    /// being enumerated throws <see cref="InvalidOperationException"/>; removing keys or
    /// overwriting values does not.
    /// </summary>
    public ICollection<TKey> Keys => _keys ??= new KeyCollection(this);

    /// <summary>
    /// Gets a read-only, live view of the values. The view is created once and cached, so reading
    /// this property does not allocate. Adding a key or clearing the dictionary while the view is
    /// being enumerated throws <see cref="InvalidOperationException"/>; removing keys or
    /// overwriting values does not.
    /// </summary>
    public ICollection<TValue> Values => _values ??= new ValueCollection(this);

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    /// <inheritdoc/>
    public void Add(TKey key, TValue value)
    {
        if (!TryInsert(key, value, insertOnly: true))
            throw new ArgumentException($"An item with the same key has already been added. Key: {key}", nameof(key));
    }

    /// <inheritdoc/>
    public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TKey key, out TValue value)
    {
        var entries = _entries;
        int hash = GetHash(key);
        int capacity = entries!.Length;

        for (int i = 0; i < capacity; i++)
        {
            int index = (int)(((uint)hash + (uint)i) % (uint)capacity);
            ref Entry entry = ref entries[index];

            if (entry.State == EntryState.Empty)
            {
                value = default!;
                return false;
            }

            if (entry.State == EntryState.Occupied &&
                entry.HashCode == hash &&
                _comparer.Equals(entry.Key, key))
            {
                value = entry.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    /// <inheritdoc/>
    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        return TryGetValue(item.Key, out var value) &&
               EqualityComparer<TValue>.Default.Equals(value, item.Value);
    }

    /// <inheritdoc/>
    public bool Remove(TKey key)
    {
        var entries = _entries;
        int hash = GetHash(key);
        int capacity = entries!.Length;

        for (int i = 0; i < capacity; i++)
        {
            int index = (int)(((uint)hash + (uint)i) % (uint)capacity);
            ref Entry entry = ref entries[index];

            if (entry.State == EntryState.Empty)
                return false;

            if (entry.State == EntryState.Occupied &&
                entry.HashCode == hash &&
                _comparer.Equals(entry.Key, key))
            {
                entry.State = EntryState.Deleted;
                entry.Key = default!;
                entry.Value = default!;
                _count--;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        if (TryGetValue(item.Key, out var value) &&
            EqualityComparer<TValue>.Default.Equals(value, item.Value))
        {
            return Remove(item.Key);
        }
        return false;
    }

    /// <inheritdoc/>
    public void Clear()
    {
        if (_entries is not null)
            Array.Clear(_entries, 0, _entries.Length);
        _count = 0;
        _version++;
    }

    /// <inheritdoc/>
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0 || arrayIndex + _count > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));

        for (int i = 0; i < _entries!.Length; i++)
        {
            if (_entries[i].State == EntryState.Occupied)
            {
                array[arrayIndex++] = new KeyValuePair<TKey, TValue>(_entries[i].Key, _entries[i].Value);
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        for (int i = 0; i < _entries!.Length; i++)
        {
            if (_entries[i].State == EntryState.Occupied)
                yield return new KeyValuePair<TKey, TValue>(_entries[i].Key, _entries[i].Value);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns the rented entry array to the pool.
    /// </summary>
    public void Dispose()
    {
        var entries = _entries;
        if (entries is not null)
        {
            _entries = null;
            _count = 0;
            _version++;
            _pool.Return(entries, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<Entry>());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetHash(TKey key)
    {
        return key is null ? 0 : key.GetHashCode() & 0x7FFFFFFF;
    }

    /// <returns>true if inserted or updated; false if key existed and insertOnly was true.</returns>
    /// <remarks>
    /// Overwriting an existing key never grows the table, so it never invalidates an enumeration
    /// of <see cref="Keys"/> or <see cref="Values"/>.
    /// </remarks>
    private bool TryInsert(TKey key, TValue value, bool insertOnly)
    {
        var entries = _entries!;
        int hash = GetHash(key);
        int capacity = entries.Length;
        int targetIndex = -1;

        for (int i = 0; i < capacity; i++)
        {
            int index = (int)(((uint)hash + (uint)i) % (uint)capacity);
            ref Entry entry = ref entries[index];

            if (entry.State == EntryState.Empty)
            {
                if (targetIndex < 0)
                    targetIndex = index;
                break;
            }

            if (entry.State == EntryState.Deleted)
            {
                if (targetIndex < 0)
                    targetIndex = index;
                continue;
            }

            if (entry.HashCode == hash && _comparer.Equals(entry.Key, key))
            {
                if (insertOnly)
                    return false;

                entry.Value = value;
                return true;
            }
        }

        // The key is new.
        if (targetIndex < 0 || (_count + 1) * 4 >= capacity * 3)
        {
            Grow();
            entries = _entries!;
            capacity = entries.Length;
            // A grown table holds no deleted slots and stays under the load factor, so the probe
            // ends at an empty slot.
            targetIndex = hash % capacity;
            while (entries[targetIndex].State != EntryState.Empty)
                targetIndex = (targetIndex + 1) % capacity;
        }

        ref Entry target = ref entries[targetIndex];
        target.Key = key;
        target.Value = value;
        target.HashCode = hash;
        target.State = EntryState.Occupied;
        _count++;
        _version++;
        return true;
    }

    private void Grow()
    {
        int newCapacity = _entries!.Length * 2;
        if (newCapacity < DefaultCapacity) newCapacity = DefaultCapacity;

        var newEntries = _pool.Rent(newCapacity);
        var oldEntries = _entries;

        // Clear the rented array — ArrayPool may return a dirty buffer
        Array.Clear(newEntries, 0, newEntries.Length);

        for (int i = 0; i < oldEntries.Length; i++)
        {
            ref Entry old = ref oldEntries[i];
            if (old.State != EntryState.Occupied)
                continue;

            int hash = old.HashCode;
            for (int j = 0; j < newCapacity; j++)
            {
                int index = (int)(((uint)hash + (uint)j) % (uint)newCapacity);
                if (newEntries[index].State == EntryState.Empty)
                {
                    newEntries[index] = old;
                    break;
                }
            }
        }

        _entries = newEntries;
        _version++;
        _pool.Return(oldEntries, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<Entry>());
    }
    private bool ContainsValue(TValue value)
    {
        var entries = _entries;
        if (entries is null) return false;
        var comparer = EqualityComparer<TValue>.Default;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].State == EntryState.Occupied && comparer.Equals(entries[i].Value, value))
                return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowKeyNotFound(TKey key) =>
        throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");

    private static void ThrowReadOnlyView() =>
        throw new NotSupportedException("The key and value views of a dictionary are read-only.");

    private static void ThrowVersionChanged() =>
        throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");

    private abstract class ViewBase<T> : ICollection<T>, IReadOnlyCollection<T>
    {
        protected readonly HeapSpanDictionary<TKey, TValue> Dictionary;

        protected ViewBase(HeapSpanDictionary<TKey, TValue> dictionary) => Dictionary = dictionary;

        public int Count => Dictionary._count;

        public bool IsReadOnly => true;

        public abstract bool Contains(T item);

        protected abstract T Select(ref Entry entry);

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array is null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0 || arrayIndex > array.Length - Dictionary._count)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));

            var entries = Dictionary._entries;
            if (entries is null) return;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].State == EntryState.Occupied)
                    array[arrayIndex++] = Select(ref entries[i]);
            }
        }

        public IEnumerator<T> GetEnumerator() => new Enumerator(this);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<T>.Add(T item) => ThrowReadOnlyView();

        void ICollection<T>.Clear() => ThrowReadOnlyView();

        bool ICollection<T>.Remove(T item)
        {
            ThrowReadOnlyView();
            return false;
        }

        private sealed class Enumerator : IEnumerator<T>
        {
            private readonly ViewBase<T> _view;
            private readonly int _version;
            private int _index;
            private T _current;

            public Enumerator(ViewBase<T> view)
            {
                _view = view;
                _version = view.Dictionary._version;
                _index = -1;
                _current = default!;
            }

            public T Current => _current;

            object? IEnumerator.Current => _current;

            public bool MoveNext()
            {
                var dictionary = _view.Dictionary;
                if (_version != dictionary._version) ThrowVersionChanged();

                var entries = dictionary._entries;
                if (entries is null) return false;
                while (++_index < entries.Length)
                {
                    if (entries[_index].State == EntryState.Occupied)
                    {
                        _current = _view.Select(ref entries[_index]);
                        return true;
                    }
                }
                _current = default!;
                return false;
            }

            public void Reset()
            {
                if (_version != _view.Dictionary._version) ThrowVersionChanged();
                _index = -1;
                _current = default!;
            }

            public void Dispose() { }
        }
    }

    private sealed class KeyCollection : ViewBase<TKey>
    {
        public KeyCollection(HeapSpanDictionary<TKey, TValue> dictionary) : base(dictionary) { }

        public override bool Contains(TKey item) => Dictionary.ContainsKey(item);

        protected override TKey Select(ref Entry entry) => entry.Key;
    }

    private sealed class ValueCollection : ViewBase<TValue>
    {
        public ValueCollection(HeapSpanDictionary<TKey, TValue> dictionary) : base(dictionary) { }

        public override bool Contains(TValue item) => Dictionary.ContainsValue(item);

        protected override TValue Select(ref Entry entry) => entry.Value;
    }
}
