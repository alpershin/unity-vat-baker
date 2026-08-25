#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Alpershin.Vat
{
    /// <summary>
    /// One clip on the authoring side: the animation, the name it will answer to, and the timings
    /// that come with it. Editor-only — none of this is needed once the set is baked.
    /// </summary>
    [Serializable]
    public sealed class VatSourceClip
    {
        [SerializeField] private AnimationClip _clip;
        [SerializeField] private string _name;
        [SerializeField, Min(0.01f)] private float _speed = 1f;
        [SerializeField, Min(0f)] private float _transitionDuration;

        public VatSourceClip(AnimationClip clip)
            : this(clip, clip != null ? clip.name : string.Empty, 1f, 0f)
        {
        }

        public VatSourceClip(AnimationClip clip, string name, float speed, float transitionDuration)
        {
            _clip = clip;
            _name = name;
            _speed = speed;
            _transitionDuration = transitionDuration;
        }

        public AnimationClip Clip => _clip;
        public float Speed => Mathf.Max(_speed, 0.01f);

        /// <summary>Default crossfade into this clip. Zero means "use the player's own duration".</summary>
        public float TransitionDuration => Mathf.Max(_transitionDuration, 0f);

        /// <summary>What <c>Play("...")</c> will look for. Falls back to the clip's own name.</summary>
        public string Name => !string.IsNullOrEmpty(_name) ? _name : (_clip != null ? _clip.name : string.Empty);
    }
}
#endif
