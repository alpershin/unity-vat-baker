using UnityEngine;

namespace Alpershin.Vat
{
    /// <summary>
    /// Per-unit VAT playback: this unit's own clip, its own start time and its own crossfade,
    /// pushed once per switch through a MaterialPropertyBlock. Requires the material to have
    /// "Per Unit Animation" enabled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VatUnitAnimator : MonoBehaviour
    {
        [SerializeField] private VatAnimationSet _set;
        [SerializeField] private int _clipIndex;
        [SerializeField, Min(0f)] private float _blendDuration = 0.2f;
        [SerializeField, Min(0f)] private float _startScatter = 10f;

        private static readonly int ClipAId = Shader.PropertyToID("_VatUnitClipA");
        private static readonly int ClipBId = Shader.PropertyToID("_VatUnitClipB");
        private static readonly int TimesId = Shader.PropertyToID("_VatUnitTimes");

        private MaterialPropertyBlock _block;
        private Renderer[] _renderers;
        private Vector4 _clipA;
        private Vector4 _clipB;
        private Vector4 _times;
        private int _currentClip = -1;

        public int CurrentClip => _currentClip;
        public int ClipCount => _set != null ? _set.ClipCount : 0;

        /// <summary>
        /// Crossfades into another clip. One property block upload per call — the fade itself is
        /// carried out by the shader, so no per-frame CPU work follows.
        /// </summary>
        public void Play(int clipIndex)
        {
            if (!IsValidClip(clipIndex) || clipIndex == _currentClip)
            {
                return;
            }

            var duration = ResolveDuration(clipIndex);
            if (duration <= 0f)
            {
                PlayImmediate(clipIndex);
                return;
            }

            _clipA = _clipB;
            _times.x = _times.y;
            _clipB = _set.GetShaderParams(clipIndex);
            _times.y = Time.time;
            _times.z = Time.time;
            _times.w = duration;
            _currentClip = clipIndex;

            Upload();
        }

        /// <summary>Plays a clip chosen in the inspector rather than by index.</summary>
        public bool Play(VatClipReference reference)
        {
            if (!reference.TryResolve(out var clipIndex))
            {
                return false;
            }

            Play(clipIndex);
            return true;
        }

        public void PlayImmediate(int clipIndex)
        {
            if (!IsValidClip(clipIndex))
            {
                return;
            }

            var parameters = _set.GetShaderParams(clipIndex);
            var start = Time.time - Random.Range(0f, _startScatter);

            _clipA = parameters;
            _clipB = parameters;
            _times = new Vector4(start, start, 0f, 0f);
            _currentClip = clipIndex;

            Upload();
        }

#if UNITY_EDITOR
        public void Configure(VatAnimationSet set, int clipIndex)
        {
            _set = set;
            _clipIndex = clipIndex;
        }
#endif

        private void Awake()
        {
            // Children, not just this object: a LOD group keeps one renderer per level and every
            // one of them has to carry this unit's clip, or it snaps back on the level switch.
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
            PlayImmediate(_clipIndex);
        }

        private void Upload()
        {
            _block.Clear();
            _block.SetVector(ClipAId, _clipA);
            _block.SetVector(ClipBId, _clipB);
            _block.SetVector(TimesId, _times);

            for (var i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].SetPropertyBlock(_block);
            }
        }

        /// <summary>The clip's own crossfade wins when it was authored; otherwise this component's.</summary>
        private float ResolveDuration(int clipIndex)
        {
            var authored = _set.GetClip(clipIndex).TransitionDuration;
            return authored > 0f ? authored : _blendDuration;
        }

        private bool IsValidClip(int clipIndex)
        {
            return _set != null && clipIndex >= 0 && clipIndex < _set.ClipCount;
        }
    }
}
