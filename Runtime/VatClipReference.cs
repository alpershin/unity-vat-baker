using System;
using UnityEngine;

namespace Alpershin.Vat
{
    /// <summary>
    /// A clip picked in the inspector rather than typed as an index: the set plus the clip name,
    /// resolved to an index once at startup. Renaming a clip in the set surfaces as a failed
    /// resolve instead of silently playing the wrong animation.
    /// </summary>
    [Serializable]
    public struct VatClipReference
    {
        [SerializeField] private VatAnimationSet _set;
        [SerializeField] private string _clipName;

        public VatClipReference(VatAnimationSet set, string clipName)
        {
            _set = set;
            _clipName = clipName;
        }

        public VatAnimationSet Set => _set;
        public string ClipName => _clipName;

        public bool TryResolve(out int clipIndex)
        {
            if (_set == null || string.IsNullOrEmpty(_clipName))
            {
                clipIndex = -1;
                return false;
            }

            clipIndex = _set.IndexOf(_clipName);
            return clipIndex >= 0;
        }
    }
}
