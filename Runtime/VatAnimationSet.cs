using System;
using UnityEngine;

namespace Alpershin.Vat
{
    /// <summary>
    /// A baked crowd character: what to bake on the authoring side, and the mesh, maps and clip
    /// table it produced. The heavy artefacts live inside this asset as sub-objects, so a rebake
    /// keeps every reference to them intact.
    /// </summary>
    [CreateAssetMenu(menuName = "VAT/Animation Set", fileName = "VatAnimationSet")]
    public sealed class VatAnimationSet : ScriptableObject
    {
        [SerializeField] private Mesh _mesh;
        [SerializeField] private Texture2D _positionMap;
        [SerializeField] private Texture2D _normalMap;
        [SerializeField] private VatClip[] _clips = Array.Empty<VatClip>();
        [SerializeField] private int _totalFrames;
        [SerializeField] private int _lodLevels = 1;
        [SerializeField] private int _vertexCount;
        [SerializeField] private GameObject _prefab;

        public Mesh Mesh => _mesh;
        public Texture2D PositionMap => _positionMap;
        public Texture2D NormalMap => _normalMap;
        public int TotalFrames => _totalFrames;
        public int LodLevels => _lodLevels;
        public int VertexCount => _vertexCount;

        /// <summary>The prefab the bake produced — what a spawner is meant to instantiate.</summary>
        public GameObject Prefab => _prefab;

        public int ClipCount => _clips.Length;

        public VatClip GetClip(int index)
        {
            return _clips[index];
        }

        public bool TryGetClip(string clipName, out VatClip clip)
        {
            var index = IndexOf(clipName);
            if (index < 0)
            {
                clip = null;
                return false;
            }

            clip = _clips[index];
            return true;
        }

        /// <summary>Index of a clip by name, or -1. The runtime API works in indices; this is how
        /// a name from the inspector turns into one.</summary>
        public int IndexOf(string clipName)
        {
            for (var i = 0; i < _clips.Length; i++)
            {
                if (_clips[i].Name == clipName)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Value for the shader's _VatParams: x start frame, y frame count, z fps, w texture height.
        /// </summary>
        public Vector4 GetShaderParams(int clipIndex)
        {
            var clip = _clips[clipIndex];
            return new Vector4(clip.StartFrame, clip.FrameCount, clip.Fps, _totalFrames);
        }

#if UNITY_EDITOR
        [Header("Source")]
        [SerializeField] private GameObject _sourcePrefab;
        [SerializeField] private VatSourceClip[] _sourceClips = Array.Empty<VatSourceClip>();

        [Header("Bake")]
        [SerializeField, Min(1f)] private float _fps = 30f;
        [SerializeField] private bool _bakeNormals = true;
        [SerializeField] private bool _perUnitAnimation;

        [Header("LOD Group")]
        [SerializeField] private GameObject[] _lodPrefabs = Array.Empty<GameObject>();
        [SerializeField] private float[] _lodTransitions = { 0.4f, 0.15f, 0.05f };

        [Header("Output")]
        [SerializeField] private bool _createPrefab = true;
        [SerializeField] private bool _castShadows = true;
        [SerializeField] private Shader _shader;
        [SerializeField] private Material _materialTemplate;

        public GameObject SourcePrefab => _sourcePrefab;
        public VatSourceClip[] SourceClips => _sourceClips;
        public float Fps => _fps;
        public bool BakeNormals => _bakeNormals;
        public bool PerUnitAnimation => _perUnitAnimation;
        public GameObject[] LodPrefabs => _lodPrefabs;
        public float[] LodTransitions => _lodTransitions;
        public bool CreatePrefab => _createPrefab;
        public bool CastShadows => _castShadows;
        public Shader Shader => _shader;
        public Material MaterialTemplate => _materialTemplate;

        public void SetSourceClips(VatSourceClip[] clips)
        {
            _sourceClips = clips ?? Array.Empty<VatSourceClip>();
        }

        public void Configure(Mesh mesh, Texture2D positionMap, Texture2D normalMap, VatClip[] clips, int totalFrames, int vertexCount, int lodLevels)
        {
            _mesh = mesh;
            _positionMap = positionMap;
            _normalMap = normalMap;
            _clips = clips;
            _totalFrames = totalFrames;
            _vertexCount = vertexCount;
            _lodLevels = lodLevels;
        }

        public void SetPrefab(GameObject prefab)
        {
            _prefab = prefab;
        }
#endif
    }
}
