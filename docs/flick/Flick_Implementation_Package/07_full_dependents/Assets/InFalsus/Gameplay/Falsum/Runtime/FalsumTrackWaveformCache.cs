using System.Collections.Generic;
using UnityEngine;

namespace InFalsus.Gameplay.Falsum.Runtime
{
    /// <summary>
    /// Main-thread cache of decoded track waveform segments.
    /// Waveform bytes never touch the disk: the rasteriser
    /// (<c>FalsumTrackSpectrumCache</c>) produces them in memory and only the
    /// most recently used segments are kept.
    /// </summary>
    public sealed class FalsumTrackWaveformCache : System.IDisposable
    {
        private const int DefaultCapacity = 2;

        private readonly int _capacity;
        private readonly List<int> _keys = new List<int>();
        private readonly Dictionary<int, Texture2D> _textures =
            new Dictionary<int, Texture2D>();
        private int _pinnedKey = int.MinValue;
        private bool _disposed;

        public FalsumTrackWaveformCache()
            : this(DefaultCapacity)
        {
        }

        public FalsumTrackWaveformCache(int capacity)
        {
            _capacity = Mathf.Max(1, capacity);
        }

        public int Capacity
        {
            get { return _capacity; }
        }

        public int Count
        {
            get { return _keys.Count; }
        }

        public int ResidentBytes
        {
            get
            {
                var total = 0;
                for (var i = 0; i < _keys.Count; i++)
                {
                    Texture2D texture = null;
                    _textures.TryGetValue(_keys[i], out texture);
                    if (texture != null)
                    {
                        total += texture.width * texture.height;
                    }
                }
                return total;
            }
        }

        public Texture2D Get(int key)
        {
            Texture2D texture;
            if (!_textures.TryGetValue(key, out texture))
            {
                return null;
            }
            if (texture == null)
            {
                Remove(key);
                return null;
            }
            _keys.Remove(key);
            _keys.Add(key);
            return texture;
        }

        /// <summary>
        /// Marks the segment currently shown on the track. A pinned segment
        /// is never evicted, so the prefetch that runs right after a display
        /// switch cannot destroy the texture the renderer is drawing.
        /// </summary>
        public void Pin(int key)
        {
            _pinnedKey = key;
        }

        public void Store(int key, Texture2D texture)
        {
            if (texture == null)
            {
                Remove(key);
                return;
            }
            Texture2D existing;
            if (_textures.TryGetValue(key, out existing) &&
                existing != null &&
                !ReferenceEquals(existing, texture))
            {
                DestroyTexture(existing);
            }
            _textures[key] = texture;
            _keys.Remove(key);
            _keys.Add(key);
            while (_keys.Count > _capacity)
            {
                var victim = -1;
                for (var i = 0; i < _keys.Count; i++)
                {
                    if (_keys[i] == _pinnedKey)
                    {
                        continue;
                    }
                    victim = _keys[i];
                    break;
                }
                if (victim == -1)
                {
                    // Everything resident is pinned; drop the pinned one
                    // rather than grow without bound.
                    victim = _keys[0];
                    _pinnedKey = int.MinValue;
                }
                Remove(victim);
            }
        }

        public void Clear()
        {
            for (var i = 0; i < _keys.Count; i++)
            {
                Texture2D texture = null;
                _textures.TryGetValue(_keys[i], out texture);
                DestroyTexture(texture);
            }
            _keys.Clear();
            _textures.Clear();
            _pinnedKey = int.MinValue;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Clear();
        }

        private void Remove(int key)
        {
            Texture2D texture = null;
            _textures.TryGetValue(key, out texture);
            _textures.Remove(key);
            _keys.Remove(key);
            DestroyTexture(texture);
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(texture);
            }
            else
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
