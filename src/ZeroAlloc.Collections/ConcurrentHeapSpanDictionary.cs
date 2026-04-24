using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Collections;

/// <summary>
/// A thread-safe, <see cref="ArrayPool{T}"/>-backed hash map. Drop-in replacement for
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/> when pooled
/// bucket storage is wanted. Coarse-locked: every public method acquires a single
/// <c>lock(_syncRoot)</c> — no lock striping, no lock-free CAS.
/// </summary>
/// <typeparam name="TKey">The type of keys.</typeparam>
/// <typeparam name="TValue">The type of values.</typeparam>
/// <remarks>
/// Disposal contract: the caller must ensure no concurrent operations are in flight
/// before calling <see cref="Dispose"/>. Concurrent <c>Dispose</c> vs. operations is
/// undefined behaviour.
///
/// Factory atomicity: <c>GetOrAdd(key, valueFactory)</c> and
/// <c>AddOrUpdate(key, addValue, updateFactory)</c> invoke their factory delegate
/// <i>exactly once</i> per successful add/update. This is a stronger guarantee than
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>,
/// whose factories may be invoked more than once under contention.
/// </remarks>
public sealed class ConcurrentHeapSpanDictionary<TKey, TValue>
    : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable
    where TKey : notnull
{
    private enum EntryState : byte
    {
        Empty,
        Occupied,
        Deleted,
    }

    private struct Entry
    {
        public TKey Key;
        public TValue Value;
        public int HashCode;
        public EntryState State;
    }

    private readonly object _syncRoot = new();
    private Entry[]? _entries;
    private int _count;
    private readonly IEqualityComparer<TKey> _comparer;
    private readonly ArrayPool<Entry> _pool;

    private const int DefaultCapacity = 4;

    /// <summary>
    /// Initializes a new <see cref="ConcurrentHeapSpanDictionary{TKey, TValue}"/> with default capacity.
    /// The backing entry array is rented from <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    public ConcurrentHeapSpanDictionary() : this(DefaultCapacity, comparer: null) { }

    /// <summary>
    /// Initializes a new <see cref="ConcurrentHeapSpanDictionary{TKey, TValue}"/> with the specified capacity.
    /// The backing entry array is rented from <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    /// <param name="capacity">The initial capacity.</param>
    public ConcurrentHeapSpanDictionary(int capacity) : this(capacity, comparer: null) { }

    /// <summary>
    /// Initializes a new <see cref="ConcurrentHeapSpanDictionary{TKey, TValue}"/> with the specified capacity
    /// and key comparer. The backing entry array is rented from <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    /// <param name="capacity">The initial capacity.</param>
    /// <param name="comparer">The key comparer, or <c>null</c> to use <see cref="EqualityComparer{T}.Default"/>.</param>
    public ConcurrentHeapSpanDictionary(int capacity, IEqualityComparer<TKey>? comparer)
    {
        if (capacity < 1) capacity = DefaultCapacity;
        _pool = ArrayPool<Entry>.Shared;
        _entries = _pool.Rent(capacity);
        // ArrayPool may return a dirty buffer — always clear so all slots start as Empty.
        Array.Clear(_entries, 0, _entries.Length);
        _count = 0;
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
    }

    /// <summary>Gets the number of key/value pairs currently in the dictionary.</summary>
    public int Count { get { lock (_syncRoot) return _count; } }

    /// <summary>Returns <c>true</c> when the dictionary contains no entries.</summary>
    public bool IsEmpty { get { lock (_syncRoot) return _count == 0; } }

    /// <summary>
    /// Gets or sets the value associated with the specified key. Getter throws
    /// <see cref="KeyNotFoundException"/> when the key is not present; setter performs an upsert.
    /// </summary>
    public TValue this[TKey key]
    {
        get
        {
            lock (_syncRoot)
            {
                if (TryGetValueLocked(key, out var value)) return value;
                throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
            }
        }
        set { lock (_syncRoot) TryInsertLocked(key, value, insertOnly: false); }
    }

    /// <summary>Attempts to add the specified key and value. Returns <c>false</c> if the key already exists.</summary>
    public bool TryAdd(TKey key, TValue value)
    {
        lock (_syncRoot) return TryInsertLocked(key, value, insertOnly: true);
    }

    /// <summary>
    /// Updates the value for the specified key if its current value equals <paramref name="comparisonValue"/>.
    /// </summary>
    public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
    {
        lock (_syncRoot)
        {
            if (TryGetValueLocked(key, out var current) &&
                EqualityComparer<TValue>.Default.Equals(current, comparisonValue))
            {
                TryInsertLocked(key, newValue, insertOnly: false);
                return true;
            }
            return false;
        }
    }

    /// <summary>Attempts to get the value associated with the specified key.</summary>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_syncRoot) return TryGetValueLocked(key, out value);
    }

    /// <summary>Returns the existing value for the key, or adds and returns <paramref name="value"/>.</summary>
    public TValue GetOrAdd(TKey key, TValue value)
    {
        lock (_syncRoot)
        {
            if (TryGetValueLocked(key, out var existing)) return existing;
            TryInsertLocked(key, value, insertOnly: true);
            return value;
        }
    }

    /// <summary>
    /// Returns the existing value for the key, or invokes <paramref name="valueFactory"/> to produce one and adds it.
    /// The factory is invoked at most once per call; under contention, the coarse lock guarantees
    /// <i>exactly once</i> per successful add.
    /// </summary>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        if (valueFactory is null) throw new ArgumentNullException(nameof(valueFactory));
        lock (_syncRoot)
        {
            if (TryGetValueLocked(key, out var existing)) return existing;
            var value = valueFactory(key);
            TryInsertLocked(key, value, insertOnly: true);
            return value;
        }
    }

    /// <summary>
    /// Adds <paramref name="addValue"/> for a new key, or updates an existing key using
    /// <paramref name="updateFactory"/>. The update factory is invoked exactly once per successful update.
    /// </summary>
    public TValue AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateFactory)
    {
        if (updateFactory is null) throw new ArgumentNullException(nameof(updateFactory));
        lock (_syncRoot)
        {
            if (TryGetValueLocked(key, out var existing))
            {
                var updated = updateFactory(key, existing);
                TryInsertLocked(key, updated, insertOnly: false);
                return updated;
            }
            TryInsertLocked(key, addValue, insertOnly: true);
            return addValue;
        }
    }

    /// <summary>Attempts to remove the specified key and return its value.</summary>
    public bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_syncRoot)
        {
            if (TryGetValueLocked(key, out value))
            {
                RemoveLocked(key);
                return true;
            }
            return false;
        }
    }

    /// <summary>Returns <c>true</c> if the dictionary contains the specified key.</summary>
    public bool ContainsKey(TKey key)
    {
        lock (_syncRoot) return TryGetValueLocked(key, out _);
    }

    /// <summary>Returns a snapshot array of the current keys.</summary>
    public TKey[] ToKeysArray()
    {
        lock (_syncRoot)
        {
            if (_entries is null) return Array.Empty<TKey>();
            var result = new TKey[_count];
            int i = 0;
            for (int j = 0; j < _entries.Length; j++)
            {
                if (_entries[j].State == EntryState.Occupied)
                    result[i++] = _entries[j].Key;
            }
            return result;
        }
    }

    /// <summary>Returns a snapshot array of the current values.</summary>
    public TValue[] ToValuesArray()
    {
        lock (_syncRoot)
        {
            if (_entries is null) return Array.Empty<TValue>();
            var result = new TValue[_count];
            int i = 0;
            for (int j = 0; j < _entries.Length; j++)
            {
                if (_entries[j].State == EntryState.Occupied)
                    result[i++] = _entries[j].Value;
            }
            return result;
        }
    }

    /// <summary>Returns a snapshot array of the current key/value pairs.</summary>
    public KeyValuePair<TKey, TValue>[] ToArray()
    {
        lock (_syncRoot)
        {
            if (_entries is null) return Array.Empty<KeyValuePair<TKey, TValue>>();
            var result = new KeyValuePair<TKey, TValue>[_count];
            int i = 0;
            for (int j = 0; j < _entries.Length; j++)
            {
                if (_entries[j].State == EntryState.Occupied)
                    result[i++] = new KeyValuePair<TKey, TValue>(_entries[j].Key, _entries[j].Value);
            }
            return result;
        }
    }

    /// <summary>
    /// Returns an enumerator over a snapshot of the current entries. The snapshot is taken under
    /// the lock; iteration does not hold the lock and is safe during concurrent writes.
    /// </summary>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        var snapshot = ToArray();
        foreach (var kv in snapshot) yield return kv;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Removes all entries. Does not return the bucket array to the pool.</summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            if (_entries is not null) Array.Clear(_entries, 0, _entries.Length);
            _count = 0;
        }
    }

    /// <summary>Returns the rented entry array to the pool. Idempotent.</summary>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            var entries = _entries;
            if (entries is null) return;
            _entries = null;
            _count = 0;
            _pool.Return(entries, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<Entry>());
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetHash(TKey key) => _comparer.GetHashCode(key) & 0x7FFFFFFF;

    private bool TryGetValueLocked(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var entries = _entries;
        if (entries is null)
        {
            value = default!;
            return false;
        }
        int hash = GetHash(key);
        int capacity = entries.Length;

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

    /// <returns>true if inserted or updated; false if key existed and insertOnly was true.</returns>
    private bool TryInsertLocked(TKey key, TValue value, bool insertOnly)
    {
        if ((_count + 1) * 4 >= _entries!.Length * 3)
        {
            GrowLocked();
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

        GrowLocked();
        return TryInsertLocked(key, value, insertOnly);
    }

    private bool RemoveLocked(TKey key)
    {
        var entries = _entries;
        if (entries is null) return false;
        int hash = GetHash(key);
        int capacity = entries.Length;

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

    private void GrowLocked()
    {
        int newCapacity = _entries!.Length * 2;
        if (newCapacity < DefaultCapacity) newCapacity = DefaultCapacity;

        var newEntries = _pool.Rent(newCapacity);
        var oldEntries = _entries;

        // Clear the rented array — ArrayPool may return a dirty buffer.
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
