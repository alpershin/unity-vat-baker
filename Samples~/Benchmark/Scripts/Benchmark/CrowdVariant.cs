using System;
using UnityEngine;

namespace Alpershin.Vat.Samples.Benchmark
{
    /// <summary>
    /// One crowd implementation to compare: a label and the prefab the grid is filled with.
    /// </summary>
    [Serializable]
    public sealed class CrowdVariant
    {
        [SerializeField] private string _name = "Variant";
        [SerializeField] private GameObject _prefab;

        public string Name => _name;
        public GameObject Prefab => _prefab;
    }
}
