using System;
using UnityEngine;

namespace Alpershin.Vat
{
    /// <summary>
    /// One baked animation clip: where its frames live inside the VAT texture and how fast to play them.
    /// </summary>
    [Serializable]
    public sealed class VatClip
    {
        [SerializeField] private string _name;
        [SerializeField] private int _startFrame;
        [SerializeField] private int _frameCount;
        [SerializeField] private float _fps;
        [SerializeField] private bool _looping;
        [SerializeField] private float _transitionDuration;

        public VatClip(string name, int startFrame, int frameCount, float fps, bool looping, float transitionDuration)
        {
            _name = name;
            _startFrame = startFrame;
            _frameCount = frameCount;
            _fps = fps;
            _looping = looping;
            _transitionDuration = transitionDuration;
        }

        public string Name => _name;
        public int StartFrame => _startFrame;
        public int FrameCount => _frameCount;
        public float Fps => _fps;
        public bool Looping => _looping;

        /// <summary>Crossfade this clip was authored with. Zero means the player decides.</summary>
        public float TransitionDuration => _transitionDuration;
    }
}
