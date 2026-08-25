using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Alpershin.Vat.Samples.Spawning;

namespace Alpershin.Vat.Samples.Benchmark
{
    /// <summary>
    /// Refills the grid with another crowd implementation at runtime, so the same camera and the
    /// same unit count can be measured against Mecanim, VAT, or anything else in the list.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrowdVariantSwitcher : MonoBehaviour
    {
        [SerializeField] private GridPrefabSpawner _spawner;
        [SerializeField] private CrowdVariant[] _variants = Array.Empty<CrowdVariant>();
        [SerializeField] private int _startIndex;
        [SerializeField] private bool _spawnOnStart = true;
        [SerializeField] private Key _switchKey = Key.Tab;
        [SerializeField] private UnityEvent _switched;

        private readonly GameObject[] _pickedPrefab = new GameObject[1];
        private int _current = -1;

        public int Current => _current;
        public int VariantCount => _variants.Length;
        public string CurrentName => IsValidVariant(_current) ? _variants[_current].Name : string.Empty;

        public string VariantName(int index) => IsValidVariant(index) ? _variants[index].Name : string.Empty;

        [ContextMenu("Switch Next")]
        public void SwitchNext()
        {
            if (_variants.Length > 0)
            {
                Switch((_current + 1) % _variants.Length);
            }
        }

        /// <summary>
        /// Rebuilds the crowd with the variant already selected — used when the unit count changes.
        /// </summary>
        public void Refresh()
        {
            Switch(_current);
        }

        public void Switch(int index)
        {
            if (_spawner == null || !IsValidVariant(index))
            {
                return;
            }

            var prefab = _variants[index].Prefab;
            if (prefab == null)
            {
                Debug.LogWarning($"Crowd variant '{_variants[index].Name}' has no prefab.", this);
                return;
            }

            _current = index;
            _pickedPrefab[0] = prefab;
            _spawner.SetPrefabs(_pickedPrefab);
            _spawner.Spawn();

            _switched?.Invoke();
            Debug.Log($"Crowd variant: {_variants[index].Name} ({_spawner.SpawnedCount} units)", this);
        }

        private void Start()
        {
            if (_spawnOnStart)
            {
                Switch(_startIndex);
                return;
            }

            // Remember the selection anyway, so the HUD highlights it and Refresh() has a target.
            _current = IsValidVariant(_startIndex) ? _startIndex : -1;
        }

        private void Update()
        {
            if (_switchKey == Key.None)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_switchKey].wasPressedThisFrame)
            {
                SwitchNext();
            }
        }

        private bool IsValidVariant(int index)
        {
            return index >= 0 && index < _variants.Length;
        }
    }
}
