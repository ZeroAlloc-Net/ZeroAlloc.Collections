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
            throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            TryInsert(key, value, insertOnly: false);
        }
    }

    /// <inheritdoc/>
    public ICollection<TKey> Keys
    {
        get
        {
            var keys = new List<TKey>(_count);
            for (int i = 0; i < _entries!.Length; i++)
            {
                if (_entries[i].State == EntryState.Occupied)
                    keys.Add(_entries[i].Key);
            }
            return keys;
        }
    }

    /// <inheritdoc/>
    public ICollection<TValue> Values
    {
        get
        {
            var values = new List<TValue>(_count);
            for (int i = 0; i < _entries!.Length; i++)
            {
                if (_entries[i].State == EntryState.Occupied)
                    values.Add(_entries[i].Value);
            }
            return values;
        }
    }

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
            int index = (hash + i) % capacity;
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
            int index = (hash + i) % capacity;
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
            _pool.Return(entries, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<Entry>());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetHash(TKey key)
    {
        return key is null ? 0 : key.GetHashCode() & 0x7FFFFFFF;
    }

    /// <returns>true if inserted or updated; false if key existed and insertOnly was true.</returns>
    private bool TryInsert(TKey key, TValue value, bool insertOnly)
    {
        if ((_count + 1) * 4 >= _entries!.Length * 3)
        {
            Grow();
        }

        var entries = _entries;
        int hash = GetHash(key);
        int capacity = entries!.Length;
        int firstDeletedIndex = -1;

        for (int i = 0; i < capacity; i++)
        {
            int index = (hash + i) % capacity;
            ref Entry entry = ref entries[index];

            if (entry.State == EntryState.Empty)
            {
                int targetIndex = firstDeletedIndex >= 0 ? firstDeletedIndex : index;
                ref Entry target = ref entries[targetIndex];
                target.Key = key;
                target.Value = value;
                target.HashCode = hash;
                target.State = EntryState.Occupied;
                _count++;
                return true;
            }

            if (entry.State == EntryState.Deleted)
            {
                if (firstDeletedIndex < 0)
                    firstDeletedIndex = index;
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

        if (firstDeletedIndex >= 0)
        {
            ref Entry target = ref entries[firstDeletedIndex];
            target.Key = key;
            target.Value = value;
            target.HashCode = hash;
            target.State = EntryState.Occupied;
            _count++;
            return true;
        }

        Grow();
        return TryInsert(key, value, insertOnly);
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
                int index = (hash + j) % newCapacity;
                if (newEntries[index].State == EntryState.Empty)
                {
                    newEntries[index] = old;
                    break;
                }
            }
        }

        _entries = newEntries;
        _pool.Return(oldEntries, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<Entry>());
    }
}
