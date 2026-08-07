using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PenguinConverters.Syntra.Core.Types;

/// <summary>
/// A compact, insertion-ordered dictionary optimised for a large number of instances that each
/// hold only a few entries (rule of thumb: up to ~32).
///
/// Storage is a single flat array of (hash, key, value) triples. Lookup computes the key hash once
/// and then performs a linear scan comparing 32-bit hashes, falling back to a string comparison only
/// on a hash hit. For small counts this beats <see cref="Dictionary{TKey,TValue}"/> (no bucket array,
/// no modulo, better cache locality), <see cref="System.Collections.Specialized.ListDictionary"/>
/// (no per-entry node allocation, no pointer chasing) and <see cref="SortedList{TKey,TValue}"/>
/// (string equality is far cheaper than string ordering).
///
/// Default key semantics are <see cref="StringComparer.Ordinal"/>. Enumeration order is insertion order.
/// Not thread-safe.
/// </summary>
[DebuggerDisplay("Count = {Count}")]
public sealed class QuickDictionary :
    IDictionary<string, object?>,
    IReadOnlyDictionary<string, object?>,
    IDictionary
{
    #region Nested types

    private struct Entry
    {
        public int HashCode;
        public string Key;
        public object? Value;
    }

    /// <summary>
    /// Selects the key comparison strategy. The two ordinal modes resolve to direct
    /// <see cref="string"/> calls, avoiding an interface dispatch per hash and per comparison.
    /// </summary>
    private enum KeyMode : byte
    {
        Ordinal = 0,
        OrdinalIgnoreCase = 1,
        Custom = 2
    }

    #endregion

    #region Fields

    private const int DefaultCapacity = 4;

    private Entry[] _entries;
    private int _count;
    private int _version;

    /// <summary>Non-null only when <see cref="_mode"/> is <see cref="KeyMode.Custom"/>.</summary>
    private readonly IEqualityComparer<string>? _comparer;

    private readonly KeyMode _mode;

    #endregion

    #region Construction

    /// <summary>
    /// Initializes an empty dictionary using <see cref="StringComparer.Ordinal"/> key semantics.
    /// No backing array is allocated until the first entry is added.
    /// </summary>
    public QuickDictionary() : this(0, null) { }

    /// <summary>
    /// Initializes an empty dictionary with the given capacity and
    /// <see cref="StringComparer.Ordinal"/> key semantics.
    /// </summary>
    /// <param name="capacity">The number of entries the dictionary can hold without growing.</param>
    public QuickDictionary(int capacity) : this(capacity, null) { }

    /// <summary>
    /// Initializes an empty dictionary using the given key comparer.
    /// </summary>
    /// <param name="comparer">
    /// The key comparer, or <c>null</c> for <see cref="StringComparer.Ordinal"/>.
    /// </param>
    public QuickDictionary(IEqualityComparer<string>? comparer) : this(0, comparer) { }

    /// <summary>
    /// Initializes an empty dictionary with the given capacity and key comparer.
    /// </summary>
    /// <param name="capacity">The number of entries the dictionary can hold without growing.</param>
    /// <param name="comparer">
    /// The key comparer, or <c>null</c> for <see cref="StringComparer.Ordinal"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="capacity"/> is negative.
    /// </exception>
    public QuickDictionary(int capacity, IEqualityComparer<string>? comparer)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _entries = capacity == 0 ? Array.Empty<Entry>() : new Entry[capacity];

        // Ordinal and OrdinalIgnoreCase resolve to direct string calls, so the comparer
        // reference is dropped entirely for them.
        if (comparer is null || ReferenceEquals(comparer, StringComparer.Ordinal))
        {
            _mode = KeyMode.Ordinal;
        }
        else if (ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase))
        {
            _mode = KeyMode.OrdinalIgnoreCase;
        }
        else
        {
            _mode = KeyMode.Custom;
            _comparer = comparer;
        }
    }

    /// <summary>
    /// Initializes a dictionary populated from <paramref name="collection"/>, using
    /// <see cref="StringComparer.Ordinal"/> key semantics.
    /// </summary>
    /// <param name="collection">The entries to copy.</param>
    public QuickDictionary(IEnumerable<KeyValuePair<string, object?>> collection)
        : this(collection, null) { }

    /// <summary>
    /// Initializes a dictionary populated from <paramref name="collection"/> using the given comparer.
    /// </summary>
    /// <param name="collection">The entries to copy.</param>
    /// <param name="comparer">
    /// The key comparer, or <c>null</c> for <see cref="StringComparer.Ordinal"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="collection"/> is <c>null</c>.
    /// </exception>
    public QuickDictionary(IEnumerable<KeyValuePair<string, object?>> collection, IEqualityComparer<string>? comparer)
        : this(collection is ICollection<KeyValuePair<string, object?>> c ? c.Count : DefaultCapacity, comparer)
    {
        if (collection is null)
            throw new ArgumentNullException(nameof(collection));

        foreach (KeyValuePair<string, object?> pair in collection)
            Add(pair.Key, pair.Value);
    }

    #endregion

    #region Properties

    /// <summary>Number of entries currently stored.</summary>
    public int Count => _count;

    /// <summary>Size of the backing array. See <see cref="TrimExcess"/> to shrink it.</summary>
    public int Capacity => _entries.Length;

    /// <summary>The comparer used for keys.</summary>
    public IEqualityComparer<string> Comparer => _mode switch
    {
        KeyMode.Ordinal => StringComparer.Ordinal,
        KeyMode.OrdinalIgnoreCase => StringComparer.OrdinalIgnoreCase,
        _ => _comparer!
    };

    /// <summary>Gets or sets the value associated with <paramref name="key"/>.</summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The value associated with the key.</returns>
    /// <exception cref="KeyNotFoundException">The key does not exist (getter only).</exception>
    public object? this[string key]
    {
        get
        {
            int index = IndexOf(key);
            if (index < 0)
                throw new KeyNotFoundException($"The key '{key}' was not present in the dictionary.");

            return _entries[index].Value;
        }
        set => TryInsert(key, value, add: false);
    }

    /// <summary>
    /// Gets a view over the keys in insertion order.
    /// </summary>
    /// <remarks>
    /// The view is created on each call rather than cached. Caching would cost two reference
    /// fields on every instance, which is the wrong trade for a type that exists in the millions
    /// and whose key collection is rarely requested. Prefer enumerating the dictionary directly.
    /// </remarks>
    public KeyCollection Keys => new KeyCollection(this);

    /// <summary>
    /// Gets a view over the values in insertion order.
    /// </summary>
    /// <remarks>See the remarks on <see cref="Keys"/> for why this is not cached.</remarks>
    public ValueCollection Values => new ValueCollection(this);

    ICollection<string> IDictionary<string, object?>.Keys => Keys;
    ICollection<object?> IDictionary<string, object?>.Values => Values;
    IEnumerable<string> IReadOnlyDictionary<string, object?>.Keys => Keys;
    IEnumerable<object?> IReadOnlyDictionary<string, object?>.Values => Values;

    bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;

    #endregion

    #region Core lookup / mutation

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetKeyHashCode(string key)
    {
        return _mode switch
        {
            KeyMode.Ordinal => key.GetHashCode(),
            KeyMode.OrdinalIgnoreCase => key.GetHashCode(StringComparison.OrdinalIgnoreCase),
            _ => _comparer!.GetHashCode(key)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool KeysEqual(string a, string b)
    {
        return _mode switch
        {
            KeyMode.Ordinal => string.Equals(a, b, StringComparison.Ordinal),
            KeyMode.OrdinalIgnoreCase => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
            _ => _comparer!.Equals(a, b)
        };
    }

    /// <summary>Returns the index of <paramref name="key"/>, or -1.</summary>
    /// <param name="key">The key to locate.</param>
    /// <returns>The zero-based index of the key, or -1 when absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is <c>null</c>.</exception>
    public int IndexOf(string key)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        int hash = GetKeyHashCode(key);

        // Slicing to _count gives the JIT the i < length invariant, so the bounds check
        // is elided from this loop - it is the hottest path in the type.
        ReadOnlySpan<Entry> entries = _entries.AsSpan(0, _count);

        for (int i = 0; i < entries.Length; i++)
        {
            // The int comparison rejects almost every non-match without touching the string.
            if (entries[i].HashCode == hash && KeysEqual(entries[i].Key, key))
                return i;
        }

        return -1;
    }

    private bool TryInsert(string key, object? value, bool add)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        int hash = GetKeyHashCode(key);
        int count = _count;
        Span<Entry> entries = _entries.AsSpan(0, count);

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].HashCode == hash && KeysEqual(entries[i].Key, key))
            {
                if (add)
                    return false;

                entries[i].Value = value;
                return true;
            }
        }

        if (count == _entries.Length)
            Grow(count + 1);

        ref Entry entry = ref _entries[count];
        entry.HashCode = hash;
        entry.Key = key;
        entry.Value = value;

        _count = count + 1;
        _version++;
        return true;
    }

    private void Grow(int required)
    {
        int capacity = _entries.Length == 0 ? DefaultCapacity : _entries.Length * 2;
        if (capacity < required)
            capacity = required;

        Array.Resize(ref _entries, capacity);
    }

    private void RemoveAt(int index)
    {
        _count--;

        if (index < _count)
            Array.Copy(_entries, index + 1, _entries, index, _count - index);

        _entries[_count] = default; // drop references so the GC can collect key/value
        _version++;
    }

    #endregion

    #region Public API

    /// <summary>Adds an entry.</summary>
    /// <param name="key">The key to add.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <exception cref="ArgumentException">The key already exists.</exception>
    public void Add(string key, object? value)
    {
        if (!TryInsert(key, value, add: true))
            throw new ArgumentException($"An item with the key '{key}' has already been added.", nameof(key));
    }

    /// <summary>Adds the entry only if the key is not already present.</summary>
    /// <param name="key">The key to add.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <returns><c>true</c> if the entry was added; <c>false</c> if the key already existed.</returns>
    public bool TryAdd(string key, object? value) => TryInsert(key, value, add: true);

    /// <summary>Adds an entry.</summary>
    /// <param name="item">The key/value pair to add.</param>
    public void Add(KeyValuePair<string, object?> item) => Add(item.Key, item.Value);

    /// <summary>Determines whether the given key is present.</summary>
    /// <param name="key">The key to locate.</param>
    /// <returns><c>true</c> if the key exists; otherwise <c>false</c>.</returns>
    public bool ContainsKey(string key) => IndexOf(key) >= 0;

    /// <summary>Determines whether the given value is present.</summary>
    /// <param name="value">The value to locate.</param>
    /// <returns><c>true</c> if the value exists; otherwise <c>false</c>.</returns>
    public bool ContainsValue(object? value)
    {
        ReadOnlySpan<Entry> entries = _entries.AsSpan(0, _count);

        if (value is null)
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].Value is null)
                    return true;

            return false;
        }

        EqualityComparer<object?> comparer = EqualityComparer<object?>.Default;
        for (int i = 0; i < entries.Length; i++)
            if (comparer.Equals(entries[i].Value, value))
                return true;

        return false;
    }

    /// <summary>Determines whether the given key/value pair is present.</summary>
    /// <param name="item">The pair to locate.</param>
    /// <returns><c>true</c> if the pair exists; otherwise <c>false</c>.</returns>
    public bool Contains(KeyValuePair<string, object?> item)
    {
        int index = IndexOf(item.Key);
        return index >= 0 && EqualityComparer<object?>.Default.Equals(_entries[index].Value, item.Value);
    }

    /// <summary>Looks up a value by key.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">When this method returns, the value if found; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if the key exists; otherwise <c>false</c>.</returns>
    public bool TryGetValue(string key, out object? value)
    {
        int index = IndexOf(key);
        if (index < 0)
        {
            value = null;
            return false;
        }

        value = _entries[index].Value;
        return true;
    }

    /// <summary>Returns the value for <paramref name="key"/>, or <paramref name="defaultValue"/> if absent.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="defaultValue">The value to return when the key is absent.</param>
    /// <returns>The stored value, or <paramref name="defaultValue"/>.</returns>
    public object? GetValueOrDefault(string key, object? defaultValue = null)
    {
        int index = IndexOf(key);
        return index < 0 ? defaultValue : _entries[index].Value;
    }

    /// <summary>Removes the entry with the given key.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns><c>true</c> if an entry was removed; otherwise <c>false</c>.</returns>
    public bool Remove(string key)
    {
        int index = IndexOf(key);
        if (index < 0)
            return false;

        RemoveAt(index);
        return true;
    }

    /// <summary>Removes the entry matching both key and value.</summary>
    /// <param name="item">The pair to remove.</param>
    /// <returns><c>true</c> if an entry was removed; otherwise <c>false</c>.</returns>
    public bool Remove(KeyValuePair<string, object?> item)
    {
        int index = IndexOf(item.Key);
        if (index < 0 || !EqualityComparer<object?>.Default.Equals(_entries[index].Value, item.Value))
            return false;

        RemoveAt(index);
        return true;
    }

    /// <summary>Removes all entries, retaining the backing array.</summary>
    public void Clear()
    {
        if (_count == 0)
            return;

        Array.Clear(_entries, 0, _count);
        _count = 0;
        _version++;
    }

    /// <summary>Grows the backing array to hold at least <paramref name="capacity"/> entries.</summary>
    /// <param name="capacity">The minimum capacity required.</param>
    /// <returns>The capacity of the dictionary after the call.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="capacity"/> is negative.
    /// </exception>
    public int EnsureCapacity(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        if (_entries.Length < capacity)
            Array.Resize(ref _entries, capacity);

        return _entries.Length;
    }

    /// <summary>
    /// Shrinks the backing array to <see cref="Count"/>. Worth calling on instances that are built once
    /// and then kept alive in large numbers.
    /// </summary>
    public void TrimExcess()
    {
        if (_entries.Length != _count)
            Array.Resize(ref _entries, _count);
    }

    /// <summary>Copies all entries into <paramref name="array"/> starting at <paramref name="arrayIndex"/>.</summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index at which copying begins.</param>
    public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
    {
        if (array is null)
            throw new ArgumentNullException(nameof(array));
        if ((uint)arrayIndex > (uint)array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < _count)
            throw new ArgumentException("Destination array is not long enough.", nameof(array));

        ReadOnlySpan<Entry> entries = _entries.AsSpan(0, _count);
        for (int i = 0; i < entries.Length; i++)
            array[arrayIndex + i] = new KeyValuePair<string, object?>(entries[i].Key, entries[i].Value);
    }

    #endregion

    #region Enumeration

    /// <summary>Allocation-free enumerator (struct) used by <c>foreach</c>.</summary>
    /// <returns>A struct enumerator over the entries in insertion order.</returns>
    public Enumerator GetEnumerator() => new Enumerator(this, dictionaryEntryMode: false);

    IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator()
        => new Enumerator(this, dictionaryEntryMode: false);

    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this, dictionaryEntryMode: false);

    IDictionaryEnumerator IDictionary.GetEnumerator() => new Enumerator(this, dictionaryEntryMode: true);

    /// <summary>Enumerates the entries of a <see cref="QuickDictionary"/> in insertion order.</summary>
    public struct Enumerator : IEnumerator<KeyValuePair<string, object?>>, IDictionaryEnumerator
    {
        private readonly QuickDictionary _dictionary;
        private readonly int _version;
        private readonly bool _dictionaryEntryMode;
        private int _index;
        private KeyValuePair<string, object?> _current;

        internal Enumerator(QuickDictionary dictionary, bool dictionaryEntryMode)
        {
            _dictionary = dictionary;
            _version = dictionary._version;
            _dictionaryEntryMode = dictionaryEntryMode;
            _index = 0;
            _current = default;
        }

        /// <summary>Gets the entry at the current position.</summary>
        public KeyValuePair<string, object?> Current => _current;

        /// <summary>Advances to the next entry.</summary>
        /// <returns><c>true</c> if there was a next entry; otherwise <c>false</c>.</returns>
        public bool MoveNext()
        {
            if (_version != _dictionary._version)
                ThrowModified();

            if (_index < _dictionary._count)
            {
                ref Entry entry = ref _dictionary._entries[_index];
                _current = new KeyValuePair<string, object?>(entry.Key, entry.Value);
                _index++;
                return true;
            }

            _index = _dictionary._count + 1;
            _current = default;
            return false;
        }

        object IEnumerator.Current
        {
            get
            {
                ValidatePosition();
                return _dictionaryEntryMode
                    ? new DictionaryEntry(_current.Key, _current.Value)
                    : _current;
            }
        }

        DictionaryEntry IDictionaryEnumerator.Entry
        {
            get
            {
                ValidatePosition();
                return new DictionaryEntry(_current.Key, _current.Value);
            }
        }

        object IDictionaryEnumerator.Key
        {
            get
            {
                ValidatePosition();
                return _current.Key;
            }
        }

        object? IDictionaryEnumerator.Value
        {
            get
            {
                ValidatePosition();
                return _current.Value;
            }
        }

        void IEnumerator.Reset()
        {
            if (_version != _dictionary._version)
                ThrowModified();

            _index = 0;
            _current = default;
        }

        /// <summary>Releases resources held by the enumerator. This implementation does nothing.</summary>
        public void Dispose() { }

        private void ValidatePosition()
        {
            if (_index == 0 || _index == _dictionary._count + 1)
                throw new InvalidOperationException("Enumeration has either not started or has already finished.");
        }

        private static void ThrowModified()
            => throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
    }

    #endregion

    #region Key / value collections

    /// <summary>A read-only view over the keys of a <see cref="QuickDictionary"/>.</summary>
    [DebuggerDisplay("Count = {Count}")]
    public sealed class KeyCollection : ICollection<string>, IReadOnlyCollection<string>, ICollection
    {
        private readonly QuickDictionary _dictionary;

        internal KeyCollection(QuickDictionary dictionary) => _dictionary = dictionary;

        /// <summary>Number of keys in the underlying dictionary.</summary>
        public int Count => _dictionary._count;

        bool ICollection<string>.IsReadOnly => true;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => _dictionary;

        /// <summary>Determines whether the given key is present.</summary>
        /// <param name="item">The key to locate.</param>
        /// <returns><c>true</c> if the key exists; otherwise <c>false</c>.</returns>
        public bool Contains(string item) => _dictionary.ContainsKey(item);

        /// <summary>Copies the keys into <paramref name="array"/> starting at <paramref name="arrayIndex"/>.</summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The zero-based index at which copying begins.</param>
        public void CopyTo(string[] array, int arrayIndex)
        {
            if (array is null)
                throw new ArgumentNullException(nameof(array));
            if ((uint)arrayIndex > (uint)array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (array.Length - arrayIndex < _dictionary._count)
                throw new ArgumentException("Destination array is not long enough.", nameof(array));

            for (int i = 0; i < _dictionary._count; i++)
                array[arrayIndex + i] = _dictionary._entries[i].Key;
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array is string[] typed)
            {
                CopyTo(typed, index);
                return;
            }

            if (array is not object[] objects)
                throw new ArgumentException("Invalid array type.", nameof(array));

            for (int i = 0; i < _dictionary._count; i++)
                objects[index + i] = _dictionary._entries[i].Key;
        }

        /// <summary>Returns an allocation-free struct enumerator over the keys.</summary>
        /// <returns>A struct enumerator.</returns>
        public Enumerator GetEnumerator() => new Enumerator(_dictionary);

        IEnumerator<string> IEnumerable<string>.GetEnumerator() => new Enumerator(_dictionary);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_dictionary);

        void ICollection<string>.Add(string item) => throw new NotSupportedException("The key collection is read-only.");
        void ICollection<string>.Clear() => throw new NotSupportedException("The key collection is read-only.");
        bool ICollection<string>.Remove(string item) => throw new NotSupportedException("The key collection is read-only.");

        /// <summary>Enumerates the keys of a <see cref="QuickDictionary"/> in insertion order.</summary>
        public struct Enumerator : IEnumerator<string>
        {
            private readonly QuickDictionary _dictionary;
            private readonly int _version;
            private int _index;
            private string? _current;

            internal Enumerator(QuickDictionary dictionary)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _index = 0;
                _current = null;
            }

            /// <summary>Gets the key at the current position.</summary>
            public string Current => _current!;

            object IEnumerator.Current => _current!;

            /// <summary>Advances to the next key.</summary>
            /// <returns><c>true</c> if there was a next key; otherwise <c>false</c>.</returns>
            public bool MoveNext()
            {
                if (_version != _dictionary._version)
                    throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");

                if (_index < _dictionary._count)
                {
                    _current = _dictionary._entries[_index].Key;
                    _index++;
                    return true;
                }

                _current = null;
                return false;
            }

            void IEnumerator.Reset()
            {
                _index = 0;
                _current = null;
            }

            /// <summary>Releases resources held by the enumerator. This implementation does nothing.</summary>
            public void Dispose() { }
        }
    }

    /// <summary>A read-only view over the values of a <see cref="QuickDictionary"/>.</summary>
    [DebuggerDisplay("Count = {Count}")]
    public sealed class ValueCollection : ICollection<object?>, IReadOnlyCollection<object?>, ICollection
    {
        private readonly QuickDictionary _dictionary;

        internal ValueCollection(QuickDictionary dictionary) => _dictionary = dictionary;

        /// <summary>Number of values in the underlying dictionary.</summary>
        public int Count => _dictionary._count;

        bool ICollection<object?>.IsReadOnly => true;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => _dictionary;

        /// <summary>Determines whether the given value is present.</summary>
        /// <param name="item">The value to locate.</param>
        /// <returns><c>true</c> if the value exists; otherwise <c>false</c>.</returns>
        public bool Contains(object? item) => _dictionary.ContainsValue(item);

        /// <summary>Copies the values into <paramref name="array"/> starting at <paramref name="arrayIndex"/>.</summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The zero-based index at which copying begins.</param>
        public void CopyTo(object?[] array, int arrayIndex)
        {
            if (array is null)
                throw new ArgumentNullException(nameof(array));
            if ((uint)arrayIndex > (uint)array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (array.Length - arrayIndex < _dictionary._count)
                throw new ArgumentException("Destination array is not long enough.", nameof(array));

            for (int i = 0; i < _dictionary._count; i++)
                array[arrayIndex + i] = _dictionary._entries[i].Value;
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array is object?[] objects)
            {
                CopyTo(objects, index);
                return;
            }

            throw new ArgumentException("Invalid array type.", nameof(array));
        }

        /// <summary>Returns an allocation-free struct enumerator over the values.</summary>
        /// <returns>A struct enumerator.</returns>
        public Enumerator GetEnumerator() => new Enumerator(_dictionary);

        IEnumerator<object?> IEnumerable<object?>.GetEnumerator() => new Enumerator(_dictionary);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_dictionary);

        void ICollection<object?>.Add(object? item) => throw new NotSupportedException("The value collection is read-only.");
        void ICollection<object?>.Clear() => throw new NotSupportedException("The value collection is read-only.");
        bool ICollection<object?>.Remove(object? item) => throw new NotSupportedException("The value collection is read-only.");

        /// <summary>Enumerates the values of a <see cref="QuickDictionary"/> in insertion order.</summary>
        public struct Enumerator : IEnumerator<object?>
        {
            private readonly QuickDictionary _dictionary;
            private readonly int _version;
            private int _index;
            private object? _current;

            internal Enumerator(QuickDictionary dictionary)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _index = 0;
                _current = null;
            }

            /// <summary>Gets the value at the current position.</summary>
            public object? Current => _current;

            /// <summary>Advances to the next value.</summary>
            /// <returns><c>true</c> if there was a next value; otherwise <c>false</c>.</returns>
            public bool MoveNext()
            {
                if (_version != _dictionary._version)
                    throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");

                if (_index < _dictionary._count)
                {
                    _current = _dictionary._entries[_index].Value;
                    _index++;
                    return true;
                }

                _current = null;
                return false;
            }

            void IEnumerator.Reset()
            {
                _index = 0;
                _current = null;
            }

            /// <summary>Releases resources held by the enumerator. This implementation does nothing.</summary>
            public void Dispose() { }
        }
    }

    #endregion

    #region Non-generic IDictionary

    bool IDictionary.IsFixedSize => false;
    bool IDictionary.IsReadOnly => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    ICollection IDictionary.Keys => Keys;
    ICollection IDictionary.Values => Values;

    object? IDictionary.this[object key]
    {
        get => key is string s ? GetValueOrDefault(s) : null; // matches ListDictionary: unknown key -> null
        set
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));

            this[AsKey(key)] = value;
        }
    }

    void IDictionary.Add(object key, object? value) => Add(AsKey(key), value);

    bool IDictionary.Contains(object key) => key is string s && ContainsKey(s);

    void IDictionary.Remove(object key)
    {
        if (key is string s)
            Remove(s);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        if (array is null)
            throw new ArgumentNullException(nameof(array));

        switch (array)
        {
            case KeyValuePair<string, object?>[] pairs:
                CopyTo(pairs, index);
                return;

            case DictionaryEntry[] entries:
                for (int i = 0; i < _count; i++)
                    entries[index + i] = new DictionaryEntry(_entries[i].Key, _entries[i].Value);
                return;

            case object[] objects:
                for (int i = 0; i < _count; i++)
                    objects[index + i] = new KeyValuePair<string, object?>(_entries[i].Key, _entries[i].Value);
                return;

            default:
                throw new ArgumentException("Invalid array type.", nameof(array));
        }
    }

    private static string AsKey(object key)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        return key as string
            ?? throw new ArgumentException($"Keys must be of type {nameof(String)}.", nameof(key));
    }

    #endregion
}
