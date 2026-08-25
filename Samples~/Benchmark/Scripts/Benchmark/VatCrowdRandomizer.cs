using System.Collections.Generic;
using UnityEngine;
using Alpershin.Vat;

namespace Alpershin.Vat.Samples.Benchmark
{
    /// <summary>
    /// Drives per-unit clip switching from a single Update, so the measurement shows the cost of the
    /// property blocks rather than the cost of a hundred MonoBehaviour ticks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VatCrowdRandomizer : MonoBehaviour
    {
        [SerializeField] private Transform _root;
        [SerializeField] private Vector2 _switchInterval = new Vector2(2f, 6f);
        [SerializeField] private int _seed = 12345;

        private readonly List<VatUnitAnimator> _units = new List<VatUnitAnimator>();
        private float[] _nextSwitch = System.Array.Empty<float>();
        private System.Random _random;

        public int UnitCount => _units.Count;

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            var root = _root != null ? _root : transform;

            _units.Clear();
            root.GetComponentsInChildren(true, _units);
            _random = new System.Random(_seed);

            if (_nextSwitch.Length < _units.Count)
            {
                _nextSwitch = new float[_units.Count];
            }

            for (var i = 0; i < _units.Count; i++)
            {
                _nextSwitch[i] = Time.time + NextInterval();
            }
        }

        private void Start()
        {
            Rebuild();
        }

        private void Update()
        {
            var now = Time.time;
            for (var i = 0; i < _units.Count; i++)
            {
                if (now < _nextSwitch[i])
                {
                    continue;
                }

                var unit = _units[i];
                if (unit != null && unit.ClipCount > 1)
                {
                    unit.Play(_random.Next(unit.ClipCount));
                }

                _nextSwitch[i] = now + NextInterval();
            }
        }

        private float NextInterval()
        {
            var span = Mathf.Max(_switchInterval.y - _switchInterval.x, 0f);
            return _switchInterval.x + (float)_random.NextDouble() * span;
        }
    }
}
