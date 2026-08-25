using UnityEngine;
using UnityEngine.InputSystem;

namespace Alpershin.Vat
{
    /// <summary>
    /// Publishes the clip a VAT crowd plays and crossfades between clips. The clip lives in global
    /// shader properties, so every unit stays on one shared material and one batch.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VatCrowdPlayer : MonoBehaviour
    {
        [SerializeField] private VatAnimationSet _set;
        [SerializeField] private int _clipIndex;
        [SerializeField, Min(0f)] private float _blendDuration = 0.2f;
        [SerializeField] private Key _nextClipKey = Key.Space;

        private static readonly int ClipAId = Shader.PropertyToID("_VatClipA");
        private static readonly int ClipBId = Shader.PropertyToID("_VatClipB");
        private static readonly int BlendId = Shader.PropertyToID("_VatBlend");

        private int _currentClip = -1;
        private float _activeBlendDuration;
        private int _targetClip = -1;
        private float _blendLeft;

        public int CurrentClip => _currentClip;
        public int ClipCount => _set != null ? _set.ClipCount : 0;

        public void Play(int clipIndex)
        {
            if (!IsValidClip(clipIndex) || clipIndex == _targetClip)
            {
                return;
            }

            var duration = ResolveDuration(clipIndex);

            // Outside play mode nothing ticks the blend, so a transition would hang half way.
            if (duration <= 0f || !Application.isPlaying)
            {
                PlayImmediate(clipIndex);
                return;
            }

            _targetClip = clipIndex;
            _activeBlendDuration = duration;
            _blendLeft = duration;
            Shader.SetGlobalVector(ClipBId, _set.GetShaderParams(clipIndex));
            Shader.SetGlobalFloat(BlendId, 0f);
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

        public bool Play(string clipName)
        {
            for (var i = 0; i < ClipCount; i++)
            {
                if (_set.GetClip(i).Name != clipName)
                {
                    continue;
                }

                Play(i);
                return true;
            }

            return false;
        }

        [ContextMenu("Play Next Clip")]
        public void PlayNext()
        {
            if (ClipCount > 0)
            {
                Play((_targetClip + 1) % ClipCount);
            }
        }

        [ContextMenu("Apply Clip Index")]
        public void ApplyClipIndex()
        {
            PlayImmediate(_clipIndex);
        }

        private void OnEnable()
        {
            PlayImmediate(_clipIndex);
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(BlendId, 0f);
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (_nextClipKey != Key.None && keyboard != null && keyboard[_nextClipKey].wasPressedThisFrame)
            {
                PlayNext();
            }

            if (_blendLeft <= 0f)
            {
                return;
            }

            _blendLeft -= Time.deltaTime;
            if (_blendLeft <= 0f)
            {
                PlayImmediate(_targetClip);
                return;
            }

            Shader.SetGlobalFloat(BlendId, 1f - _blendLeft / _activeBlendDuration);
        }

        private void PlayImmediate(int clipIndex)
        {
            if (!IsValidClip(clipIndex))
            {
                return;
            }

            _clipIndex = clipIndex;
            _currentClip = clipIndex;
            _targetClip = clipIndex;
            _blendLeft = 0f;

            Shader.SetGlobalVector(ClipAId, _set.GetShaderParams(clipIndex));
            Shader.SetGlobalFloat(BlendId, 0f);
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
