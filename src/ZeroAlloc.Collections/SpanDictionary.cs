using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A stack-only dictionary using open addressing with linear probing.
/// Avoids per-node allocations unlike the BCL <see cref="Dictionary{TKey, TValue}"/>.
/// The backing entry array is rented from <see cref="ArrayPool{T}"/> and returned on <see cref="Dispose"/>.
/// </summary>
/// <typeparam name="TKey">The type of keys.</typeparam>
/// <typeparam name="TValue">The type of values.</typeparam>
public ref struct SpanDictionary<TKey, TValue>
{
    internal enum EntryState : byte
    {
        Empty,
        Occupied,
        Deleted
    }

    internal struct Entry
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
    /// Initializes a new <see cref="SpanDictionary{TKey, TValue}"/> with the specified capacity.
    /// The backing entry array is rented from <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    /// <param name="capacity">The initial capacity.</param>
    public SpanDictionary(int capacity)
    {
        if (capacity < 1) capacity = DefaultCapacity;
        _pool = ArrayPool<Entry>.Shared;
        _entries = _pool.Rent(capacity);
        // ArrayPool may return a dirty buffer — always clear so all slots start as Empty
        Array.Clear(_entries, 0, _entries.Length);
        _count = 0;
        _comparer = EqualityComparer<TKey>.Default;
    }

    /// <summary>
    /// Gets the number of key/value pairs in the dictionary.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Adds a key/value pair. Throws <see cref="ArgumentException"/> if the key already exists.
    /// </summary>
    public void Add(TKey key, TValue value)
    {
        if (TryInsert(key, value, insertOnly: true))
            return;

        throw new ArgumentException($"An item with the same key has already been added. Key: {key}");
    }

    /// <summary>
    /// Gets or sets the value associated with the specified key.
    /// </summary>
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

    /// <summary>
    /// Attempts to get the value associated with the specified key.
    /// </summary>
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

    /// <summary>
    /// Determines whether the dictionary contains the specified key.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    /// <summary>
    /// Removes the value with the specified key, using tombstone deletion.
    /// </summary>
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

    /// <summary>
    /// Removes all keys and values from the dictionary.
    /// </summary>
    public void Clear()
    {
        if (_entries is not null)
            Array.Clear(_entries, 0, _entries.Length);
        _count = 0;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the occupied entries.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new Enumerator(_entries!);

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
        // Check load factor before insert
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
                // Use deleted slot if we passed one, otherwise use this empty slot.
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

            // Occupied — check for duplicate key
            if (entry.HashCode == hash && _comparer.Equals(entry.Key, key))
            {
                if (insertOnly)
                    return false;

                entry.Value = value;
                return true;
            }
        }

        // All slots were Deleted or Occupied with no match — use first deleted slot
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

        // Should not reach here if load factor check is correct
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

    /// <summary>
    /// Enumerator for <see cref="SpanDictionary{TKey, TValue}"/>.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly Entry[] _entries;
        private int _index;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(Entry[] entries)
        {
            _entries = entries;
            _index = -1;
        }

        /// <summary>
        /// Gets the current key/value pair.
        /// </summary>
        public KeyValuePair<TKey, TValue> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ref readonly Entry e = ref _entries[_index];
                return new KeyValuePair<TKey, TValue>(e.Key, e.Value);
            }
        }

        /// <summary>
        /// Advances the enumerator to the next occupied entry.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            var entries = _entries;
            int i = _index + 1;
            while (i < entries.Length)
            {
                if (entries[i].State == EntryState.Occupied)
                {
                    _index = i;
                    return true;
                }
                i++;
            }
            _index = i;
            return false;
        }
    }
}
