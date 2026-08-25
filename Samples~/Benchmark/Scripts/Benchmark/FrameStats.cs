using System;

namespace Alpershin.Vat.Samples.Benchmark
{
    /// <summary>
    /// Rolling window of one per-frame value — frame times in milliseconds, or a render counter —
    /// with the summary a comparison actually needs: an average is not enough when one branch
    /// stutters and the other does not.
    /// </summary>
    public sealed class FrameStats
    {
        private readonly float[] _samples;
        private readonly float[] _scratch;
        private int _count;
        private int _cursor;

        public FrameStats(int capacity)
        {
            _samples = new float[Math.Max(1, capacity)];
            _scratch = new float[_samples.Length];
        }

        public int Count => _count;

        public void Add(float milliseconds)
        {
            _samples[_cursor] = milliseconds;
            _cursor = (_cursor + 1) % _samples.Length;
            _count = Math.Min(_count + 1, _samples.Length);
        }

        public void Reset()
        {
            _count = 0;
            _cursor = 0;
        }

        public float Average()
        {
            if (_count == 0)
            {
                return 0f;
            }

            var total = 0f;
            for (var i = 0; i < _count; i++)
            {
                total += _samples[i];
            }

            return total / _count;
        }

        /// <summary>
        /// Frame time that only the worst (1 - t) share of frames exceeds. The 95th percentile is
        /// where hitching shows up, and hitching is what a raw average hides.
        /// </summary>
        public float Percentile(float t)
        {
            if (_count == 0)
            {
                return 0f;
            }

            Array.Copy(_samples, _scratch, _count);
            Array.Sort(_scratch, 0, _count);

            var index = (int)((_count - 1) * Math.Min(Math.Max(t, 0f), 1f));
            return _scratch[index];
        }
    }
}
