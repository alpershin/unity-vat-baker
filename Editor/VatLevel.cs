using UnityEngine;

namespace Alpershin.Vat.EditorTools
{
    /// <summary>
    /// One level of detail on its way through the bake: the instance it is sampled from, the
    /// renderers that belong to it, and everything the bake produces for it.
    /// </summary>
    internal sealed class VatLevel
    {
        public VatLevel(GameObject instance, SkinnedMeshRenderer[] renderers, float transition, string suffix)
        {
            Instance = instance;
            Renderers = renderers;
            Transition = transition;
            Suffix = suffix;
        }

        public GameObject Instance { get; }
        public SkinnedMeshRenderer[] Renderers { get; }

        /// <summary>Screen relative height at which this level gives way to the next one.</summary>
        public float Transition { get; }

        /// <summary>Empty for a single-level bake, "-LOD1" and so on when there are several.</summary>
        public string Suffix { get; }

        public VatSampleBuffer Buffer { get; set; }
        public VatMeshResult MeshResult { get; set; }
        public Material[] Materials { get; set; }
    }
}
