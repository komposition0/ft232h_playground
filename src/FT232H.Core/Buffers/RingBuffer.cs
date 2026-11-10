using System.Collections;

namespace FT232H.Core.Buffers;

/// <summary>
/// Fixed-capacity ring buffer for plot data (Signal Monitor). Oldest entries are
/// overwritten when capacity is reached. Thread-safe.
/// </summary>
public sealed class RingBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] _items;
    private readonly object _lock = new();
    private int _start;
    private int _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count
    {
        get { lock (_lock) return _count; }
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            int index = (_start + _count) % _items.Length;
            if (_count == _items.Length)
            {
                _items[_start] = item;
                _start = (_start + 1) % _items.Length;
            }
            else
            {
                _items[index] = item;
                _count++;
            }
        }
    }

    public T this[int index]
    {
        get
        {
            lock (_lock)
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return _items[(_start + index) % _items.Length];
            }
        }
    }

    /// <summary>Copies contents in chronological order.</summary>
    public T[] ToArray()
    {
        lock (_lock)
        {
            var result = new T[_count];
            for (int i = 0; i < _count; i++)
                result[i] = _items[(_start + i) % _items.Length];
            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_items);
            _start = 0;
            _count = 0;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)ToArray()).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}