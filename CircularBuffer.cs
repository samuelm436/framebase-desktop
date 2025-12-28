using System;
using System.Collections.Generic;
using System.Linq;

namespace FramebaseApp
{
    /// <summary>
    /// Hocheffizienter Ringpuffer für FPS-Daten
    /// O(1) Add/Remove statt O(n) bei List.RemoveAt(0)
    /// </summary>
    public class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private int _head = 0;
        private int _count = 0;
        private readonly int _capacity;

        public int Count => _count;
        public int Capacity => _capacity;
        public bool IsFull => _count == _capacity;

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("Capacity must be positive", nameof(capacity));
            
            _capacity = capacity;
            _buffer = new T[capacity];
        }

        /// <summary>
        /// Fügt Element hinzu - O(1) Operation!
        /// </summary>
        public void Add(T item)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _capacity;
            
            if (_count < _capacity)
                _count++;
        }

        /// <summary>
        /// Gibt alle Elemente in chronologischer Reihenfolge zurück
        /// </summary>
        public IEnumerable<T> GetAll()
        {
            if (_count == 0)
                yield break;

            int start = IsFull ? _head : 0;
            
            for (int i = 0; i < _count; i++)
            {
                int index = (start + i) % _capacity;
                yield return _buffer[index];
            }
        }

        /// <summary>
        /// Konvertiert zu Array (für LINQ-Operationen)
        /// </summary>
        public T[] ToArray()
        {
            return GetAll().ToArray();
        }

        /// <summary>
        /// Löscht alle Elemente
        /// </summary>
        public void Clear()
        {
            _head = 0;
            _count = 0;
            Array.Clear(_buffer, 0, _capacity);
        }
    }
}
