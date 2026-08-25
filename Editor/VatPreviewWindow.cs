using UnityEditor;
using UnityEngine;

namespace Alpershin.Vat.EditorTools
{
    /// <summary>
    /// Plays a baked set without entering play mode: scrub a clip, watch it loop, and drag the two
    /// clip slots against each other to see what a crossfade actually looks like.
    /// </summary>
    internal sealed class VatPreviewWindow : EditorWindow
    {
        private static readonly int ClipAId = Shader.PropertyToID("_VatClipA");
        private static readonly int ClipBId = Shader.PropertyToID("_VatClipB");
        private static readonly int BlendId = Shader.PropertyToID("_VatBlend");
        private static readonly int PhaseScatterId = Shader.PropertyToID("_PhaseScatter");
        private static readonly int PerInstanceId = Shader.PropertyToID("_PerInstance");

        private const string PerInstanceKeyword = "_VAT_PER_INSTANCE";

        [SerializeField] private VatAnimationSet _set;
        [SerializeField] private int _clipA;
        [SerializeField] private int _clipB;
        [SerializeField] private float _blend;
        [SerializeField] private float _speed = 1f;
        [SerializeField] private bool _playing = true;

        private PreviewRenderUtility _preview;
        private Material[] _materials;
        private VatAnimationSet _boundSet;
        private Vector2 _orbit = new Vector2(20f, 0f);
        private float _distance = 3f;
        private float _time;
        private double _lastTick;

        public static void Open(VatAnimationSet set)
        {
            var window = GetWindow<VatPreviewWindow>("VAT Preview");
            window._set = set;
            window.minSize = new Vector2(420f, 420f);
            window.Show();
        }

        [MenuItem("Window/VAT/Animation Preview")]
        private static void OpenEmpty()
        {
            Open(null);
        }

        private void OnEnable()
        {
            _lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            ReleaseMaterials();

            if (_preview != null)
            {
                _preview.Cleanup();
                _preview = null;
            }
        }

        private void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            var delta = (float)(now - _lastTick);
            _lastTick = now;

            if (!_playing || _set == null || _set.ClipCount == 0)
            {
                return;
            }

            _time += delta * _speed;
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_set == null || _set.Mesh == null)
            {
                EditorGUILayout.HelpBox("Assign a baked VAT animation set.", MessageType.Info);
                return;
            }

            if (_set.ClipCount == 0)
            {
                EditorGUILayout.HelpBox("This set has no clips baked into it yet.", MessageType.Warning);
                return;
            }

            EnsureBinding();
            DrawViewport(GUILayoutUtility.GetRect(position.width, Mathf.Max(position.height - 150f, 120f)));
            DrawControls();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var picked = (VatAnimationSet)EditorGUILayout.ObjectField(_set, typeof(VatAnimationSet), false);
                if (picked != _set)
                {
                    _set = picked;
                    ReleaseMaterials();
                }

                if (GUILayout.Button("Bake", EditorStyles.toolbarButton, GUILayout.Width(60f)) && _set != null)
                {
                    var result = VatBaker.Bake(_set);
                    Debug.Log(result.Message, _set);
                    ReleaseMaterials();
                }
            }
        }

        private void DrawControls()
        {
            var clipNames = BuildClipNames();

            using (new EditorGUILayout.HorizontalScope())
            {
                _playing = GUILayout.Toggle(_playing, _playing ? "Pause" : "Play", EditorStyles.miniButton, GUILayout.Width(60f));
                _speed = EditorGUILayout.Slider("Speed", _speed, 0f, 3f);
            }

            _clipA = EditorGUILayout.Popup("Clip", Mathf.Clamp(_clipA, 0, clipNames.Length - 1), clipNames);

            var clip = _set.GetClip(_clipA);
            var frames = Mathf.Max(clip.FrameCount, 1);
            var current = CurrentLocalFrame(clip);
            var scrubbed = EditorGUILayout.IntSlider("Frame", current, 0, frames - 1);
            if (scrubbed != current)
            {
                _playing = false;
                _time = clip.Fps > 0f ? scrubbed / clip.Fps : 0f;
            }

            EditorGUILayout.LabelField("Length", $"{frames} frames @ {clip.Fps:F0} fps");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Crossfade", EditorStyles.boldLabel);
            _clipB = EditorGUILayout.Popup("Target", Mathf.Clamp(_clipB, 0, clipNames.Length - 1), clipNames);
            _blend = EditorGUILayout.Slider("Blend", _blend, 0f, 1f);
        }

        private string[] BuildClipNames()
        {
            var names = new string[_set.ClipCount];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = _set.GetClip(i).Name;
            }

            return names;
        }

        private int CurrentLocalFrame(VatClip clip)
        {
            var frames = Mathf.Max(clip.FrameCount, 1);
            if (clip.Fps <= 0f)
            {
                return 0;
            }

            var frame = Mathf.FloorToInt(_time * clip.Fps) % frames;
            return frame < 0 ? frame + frames : frame;
        }

        private void DrawViewport(Rect rect)
        {
            HandleNavigation(rect);

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            _preview.BeginPreview(rect, GUIStyle.none);
            SetUpCamera();

            // The globals belong to the scene as well, so they are borrowed and put back.
            var previousA = Shader.GetGlobalVector(ClipAId);
            var previousB = Shader.GetGlobalVector(ClipBId);
            var previousBlend = Shader.GetGlobalFloat(BlendId);

            Shader.SetGlobalVector(ClipAId, FrozenClip(_clipA));
            Shader.SetGlobalVector(ClipBId, FrozenClip(_clipB));
            Shader.SetGlobalFloat(BlendId, _blend);

            for (var i = 0; i < _materials.Length; i++)
            {
                _preview.DrawMesh(_set.Mesh, Matrix4x4.identity, _materials[i], i);
            }

            _preview.camera.Render();

            Shader.SetGlobalVector(ClipAId, previousA);
            Shader.SetGlobalVector(ClipBId, previousB);
            Shader.SetGlobalFloat(BlendId, previousBlend);

            GUI.DrawTexture(rect, _preview.EndPreview(), ScaleMode.StretchToFill, false);
        }

        /// <summary>
        /// A clip of a single frame at zero fps: the shader's frame formula collapses to that frame
        /// and stops depending on time, which is what lets the window scrub instead of only play.
        /// </summary>
        private Vector4 FrozenClip(int clipIndex)
        {
            var clip = _set.GetClip(Mathf.Clamp(clipIndex, 0, _set.ClipCount - 1));
            var frame = clip.StartFrame + CurrentLocalFrame(clip);
            return new Vector4(frame, 1f, 0f, _set.TotalFrames);
        }

        private void SetUpCamera()
        {
            var bounds = _set.Mesh.bounds;
            var rotation = Quaternion.Euler(_orbit.x, _orbit.y, 0f);
            var target = bounds.center;

            _preview.camera.transform.rotation = rotation;
            _preview.camera.transform.position = target - rotation * Vector3.forward * (_distance * bounds.extents.magnitude);
            _preview.camera.nearClipPlane = 0.01f;
            _preview.camera.farClipPlane = 100f;

            _preview.lights[0].intensity = 1.1f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(40f, _orbit.y + 40f, 0f);
            _preview.lights[1].intensity = 0.5f;
        }

        private void HandleNavigation(Rect rect)
        {
            var current = Event.current;
            if (!rect.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type == EventType.MouseDrag && current.button == 0)
            {
                _orbit.y += current.delta.x;
                _orbit.x = Mathf.Clamp(_orbit.x + current.delta.y, -89f, 89f);
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.ScrollWheel)
            {
                _distance = Mathf.Clamp(_distance + current.delta.y * 0.1f, 1f, 20f);
                current.Use();
                Repaint();
            }
        }

        private void EnsureBinding()
        {
            if (_preview == null)
            {
                _preview = new PreviewRenderUtility();
                _preview.camera.clearFlags = CameraClearFlags.SolidColor;
                _preview.camera.backgroundColor = new Color(0.16f, 0.16f, 0.18f, 1f);
            }

            if (_materials != null && _boundSet == _set)
            {
                return;
            }

            ReleaseMaterials();
            _materials = BuildPreviewMaterials();
            _boundSet = _set;
        }

        /// <summary>
        /// Copies of the baked materials with the per-unit branch switched off and the position
        /// scatter zeroed: the preview drives one pose through the shared globals, not through a
        /// property block it would have to fake.
        /// </summary>
        private Material[] BuildPreviewMaterials()
        {
            var source = FindSourceMaterials();
            var materials = new Material[Mathf.Max(source.Length, 1)];

            for (var i = 0; i < materials.Length; i++)
            {
                var original = i < source.Length ? source[i] : null;
                var material = original != null ? new Material(original) : new Material(Shader.Find("VAT/Lit"));
                material.hideFlags = HideFlags.HideAndDontSave;

                if (material.HasProperty(PerInstanceId))
                {
                    material.SetFloat(PerInstanceId, 0f);
                }

                material.DisableKeyword(PerInstanceKeyword);

                if (material.HasProperty(PhaseScatterId))
                {
                    material.SetFloat(PhaseScatterId, 0f);
                }

                materials[i] = material;
            }

            return materials;
        }

        private Material[] FindSourceMaterials()
        {
            if (_set.Prefab == null)
            {
                return new Material[0];
            }

            var renderers = _set.Prefab.GetComponentsInChildren<MeshRenderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var filter = renderers[i].GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh == _set.Mesh)
                {
                    return renderers[i].sharedMaterials;
                }
            }

            return renderers.Length > 0 ? renderers[0].sharedMaterials : new Material[0];
        }

        private void ReleaseMaterials()
        {
            if (_materials == null)
            {
                return;
            }

            for (var i = 0; i < _materials.Length; i++)
            {
                if (_materials[i] != null)
                {
                    DestroyImmediate(_materials[i]);
                }
            }

            _materials = null;
            _boundSet = null;
        }
    }
}
