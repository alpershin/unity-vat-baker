using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using Alpershin.Vat.Samples.Spawning;

namespace Alpershin.Vat.Samples.Benchmark
{
    /// <summary>
    /// The test stand's screen: frame rate, render counters, touch buttons for the crowd variant and
    /// the unit count, and the results of finished samples — readable on a phone, where there is no
    /// console to log into.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BenchmarkHud : MonoBehaviour
    {
        [SerializeField] private CrowdVariantSwitcher _switcher;
        [SerializeField] private GridPrefabSpawner _spawner;
        [SerializeField] private int[] _unitCounts = { 50, 100, 150, 300, 500 };
        [SerializeField] private int _startCountIndex = 2;

        [Header("Sampling")]
        [SerializeField, Min(0.5f)] private float _sampleDuration = 5f;
        [SerializeField, Min(0f)] private float _settleDelay = 0.5f;
        [SerializeField, Min(1)] private int _keptResults = 4;

        [Header("Measurement")]
        [SerializeField] private bool _uncapFrameRate = true;
        [SerializeField, Min(30)] private int _targetFrameRate = 300;

        [Header("Display")]
        [SerializeField] private bool _drawHud = true;
        [SerializeField, Min(0.05f)] private float _refreshInterval = 0.25f;
        [SerializeField] private float _uiScale;

        private readonly FrameStats _live = new FrameStats(120);
        private readonly FrameStats _sample = new FrameStats(4096);

        // Render counters are published by the render thread, not this one. With VSync off and the
        // frame rate uncapped the main thread outruns it, so LastValue reads zero on the frames no
        // sample has landed on yet and the accumulated figure on the frames one has — the readout
        // flips between 0 and a doubled count. Averaging a frame window puts it back on its feet.
        private readonly FrameStats _liveDrawCalls = new FrameStats(60);
        private readonly FrameStats _liveBatches = new FrameStats(60);
        private readonly FrameStats _liveSetPassCalls = new FrameStats(60);
        private readonly FrameStats _liveTriangles = new FrameStats(60);
        private readonly List<string> _results = new List<string>();
        private readonly StringBuilder _builder = new StringBuilder(256);
        private readonly HudLayout _layout = new HudLayout();

        private ProfilerRecorder _mainThread;
        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _batches;
        private ProfilerRecorder _setPassCalls;
        private ProfilerRecorder _triangles;

        private GUIStyle _labelStyle;
        private GUIStyle _headlineStyle;
        private GUIStyle _buttonStyle;

        private string _headline = string.Empty;
        private string _readout = string.Empty;
        private float _nextRefresh;
        private float _sampleLeft;
        private float _settleLeft;
        private long _drawCallSum;
        private long _batchSum;
        private long _triangleSum;
        private int _counterSamples;
        private int _activeCount = -1;

        public bool IsSampling => _sampleLeft > 0f;

        [ContextMenu("Start Sample")]
        public void StartSample()
        {
            _sample.Reset();
            _drawCallSum = 0;
            _batchSum = 0;
            _triangleSum = 0;
            _counterSamples = 0;
            _settleLeft = _settleDelay;
            _sampleLeft = _sampleDuration;
        }

        public void SetUnitCount(int index)
        {
            if (_spawner == null || index < 0 || index >= _unitCounts.Length)
            {
                return;
            }

            _activeCount = index;
            _spawner.SetMaxInstances(_unitCounts[index]);

            if (_switcher != null)
            {
                _switcher.Refresh();
                return;
            }

            _spawner.Spawn();
        }

        // Awake, not Start: the cap has to be in place before the switcher lays the crowd out,
        // otherwise the first spawn ignores it and has to be thrown away and redone.
        private void Awake()
        {
            if (_spawner == null)
            {
                return;
            }

            if (_startCountIndex >= 0 && _startCountIndex < _unitCounts.Length)
            {
                _activeCount = _startCountIndex;
                _spawner.SetMaxInstances(_unitCounts[_startCountIndex]);
                return;
            }

            for (var i = 0; i < _unitCounts.Length; i++)
            {
                if (_unitCounts[i] == _spawner.MaxInstances)
                {
                    _activeCount = i;
                    return;
                }
            }
        }

        private void OnEnable()
        {
            // With VSync on, both branches sit at the refresh rate and the comparison shows nothing.
            // On mobile targetFrameRate = -1 does NOT mean "uncapped": it means the platform
            // default, which is 30 fps. An explicit high number is the only way off that cap.
            if (_uncapFrameRate)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = _targetFrameRate;
                OnDemandRendering.renderFrameInterval = 1;
            }

            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPassCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        private void OnDisable()
        {
            _mainThread.Dispose();
            _drawCalls.Dispose();
            _batches.Dispose();
            _setPassCalls.Dispose();
            _triangles.Dispose();
        }

        private void Update()
        {
            EnsureLayout();

            var frameMs = Time.unscaledDeltaTime * 1000f;
            _live.Add(frameMs);
            ReadCounters();
            AdvanceSample(frameMs);
            ReadPointer();

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + _refreshInterval;
                RebuildText();
            }
        }

        private void ReadCounters()
        {
            _liveDrawCalls.Add(Value(_drawCalls));
            _liveBatches.Add(Value(_batches));
            _liveSetPassCalls.Add(Value(_setPassCalls));
            _liveTriangles.Add(Value(_triangles));
        }

        private void EnsureLayout()
        {
            var variants = _switcher != null ? _switcher.VariantCount : 0;
            if (_layout.NeedsRebuild(variants, _unitCounts.Length, _results.Count))
            {
                _layout.Rebuild(_uiScale, variants, _unitCounts.Length, _results.Count);
            }
        }

        // IMGUI does not receive touch when the project runs on the Input System backend, so the
        // buttons are hit-tested here and only drawn by OnGUI.
        private void ReadPointer()
        {
            if (!_drawHud || !TryGetPress(out var position))
            {
                return;
            }

            if (_switcher != null)
            {
                for (var i = 0; i < _switcher.VariantCount; i++)
                {
                    if (_layout.Variant(i).Contains(position))
                    {
                        _switcher.Switch(i);
                        return;
                    }
                }
            }

            for (var i = 0; i < _unitCounts.Length; i++)
            {
                if (_layout.Count(i).Contains(position))
                {
                    SetUnitCount(i);
                    return;
                }
            }

            if (_layout.Sample.Contains(position) && !IsSampling)
            {
                StartSample();
            }
        }

        private static bool TryGetPress(out Vector2 guiPosition)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                guiPosition = ToGuiSpace(touchscreen.primaryTouch.position.ReadValue());
                return true;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                guiPosition = ToGuiSpace(mouse.position.ReadValue());
                return true;
            }

            guiPosition = default;
            return false;
        }

        private static Vector2 ToGuiSpace(Vector2 screenPosition)
        {
            return new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        }

        private void AdvanceSample(float frameMs)
        {
            if (_sampleLeft <= 0f)
            {
                return;
            }

            if (_settleLeft > 0f)
            {
                _settleLeft -= Time.unscaledDeltaTime;
                return;
            }

            _sample.Add(frameMs);
            _drawCallSum += Value(_drawCalls);
            _batchSum += Value(_batches);
            _triangleSum += Value(_triangles);
            _counterSamples++;

            _sampleLeft -= Time.unscaledDeltaTime;
            if (_sampleLeft <= 0f)
            {
                ReportSample();
            }
        }

        private void ReportSample()
        {
            var divisor = Mathf.Max(_counterSamples, 1);
            var average = _sample.Average();
            var line = $"{VariantName()} x{UnitCount()}: {average:F2} ms / {Fps(average):F0} fps, " +
                       $"p95 {_sample.Percentile(0.95f):F2}, draws {_drawCallSum / divisor}, " +
                       $"batches {_batchSum / divisor}, tris {_triangleSum / divisor}";

            _results.Insert(0, line);
            while (_results.Count > _keptResults)
            {
                _results.RemoveAt(_results.Count - 1);
            }

            Debug.Log(line, this);
        }

        private void RebuildText()
        {
            var average = _live.Average();
            _headline = $"{Fps(average):F0} fps   {average:F2} ms";

            _builder.Clear();
            _builder.Append(VariantName()).Append("   units ").Append(UnitCount()).AppendLine();
            _builder.Append("p95     ").Append(_live.Percentile(0.95f).ToString("F2")).Append(" ms").AppendLine();
            _builder.Append("main    ").Append(Milliseconds(_mainThread))
                .Append("   cap ").Append(Application.targetFrameRate)
                .Append(" / screen ").Append(RefreshRate().ToString("F0")).Append(" Hz").AppendLine();
            _builder.Append("draws ").Append(Counter(_drawCalls, _liveDrawCalls))
                .Append("   batches ").Append(Counter(_batches, _liveBatches))
                .Append("   setpass ").Append(Counter(_setPassCalls, _liveSetPassCalls)).AppendLine();
            _builder.Append("tris    ").Append(Counter(_triangles, _liveTriangles));

            _readout = _builder.ToString();
        }

        private static double RefreshRate()
        {
            return Screen.currentResolution.refreshRateRatio.value;
        }

        private static float Fps(float milliseconds)
        {
            return milliseconds > 0f ? 1000f / milliseconds : 0f;
        }

        private string VariantName()
        {
            if (_switcher == null)
            {
                return "crowd";
            }

            var name = _switcher.CurrentName;
            return string.IsNullOrEmpty(name) ? "crowd" : name;
        }

        private int UnitCount()
        {
            return _spawner != null ? _spawner.SpawnedCount : 0;
        }

        private static long Value(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue : 0L;
        }

        // Render counters only exist in the editor and in development builds. The window, not the
        // recorder's last sample, is what gets shown — see the fields for why.
        private static string Counter(ProfilerRecorder recorder, FrameStats window)
        {
            return recorder.Valid ? window.Average().ToString("F0") : "n/a";
        }

        private static string Milliseconds(ProfilerRecorder recorder)
        {
            return recorder.Valid ? $"{recorder.LastValue * 1e-6:F2} ms" : "n/a";
        }

        private void OnGUI()
        {
            if (!_drawHud)
            {
                return;
            }

            EnsureLayout();
            EnsureStyles();

            GUI.Box(_layout.Panel, GUIContent.none);
            GUI.Label(_layout.Headline, _headline, _headlineStyle);
            GUI.Label(_layout.Readout, _readout, _labelStyle);
            DrawResults();
            DrawButtons();
        }

        private void DrawResults()
        {
            if (_results.Count == 0)
            {
                return;
            }

            var line = _layout.Results.height / (_results.Count + 1);
            for (var i = 0; i < _results.Count; i++)
            {
                var rect = new Rect(_layout.Results.x, _layout.Results.y + line * (i + 1), _layout.Results.width, line);
                GUI.Label(rect, _results[i], _labelStyle);
            }
        }

        private void DrawButtons()
        {
            if (_switcher != null)
            {
                for (var i = 0; i < _switcher.VariantCount; i++)
                {
                    DrawButton(_layout.Variant(i), _switcher.VariantName(i), i == _switcher.Current);
                }
            }

            for (var i = 0; i < _unitCounts.Length; i++)
            {
                DrawButton(_layout.Count(i), _unitCounts[i].ToString(), i == _activeCount);
            }

            DrawButton(_layout.Sample, IsSampling ? SampleCaption() : "Sample", IsSampling);
        }

        private string SampleCaption()
        {
            return _settleLeft > 0f ? "settling..." : $"sampling {_sampleLeft:F1} s";
        }

        private void DrawButton(Rect rect, string caption, bool active)
        {
            var previous = GUI.color;
            GUI.color = active ? new Color(0.4f, 0.9f, 0.5f, 1f) : previous;
            GUI.Box(rect, caption, _buttonStyle);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            _labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = false };
            _headlineStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            _buttonStyle ??= new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };

            _labelStyle.fontSize = Mathf.RoundToInt(_layout.FontSize);
            _headlineStyle.fontSize = Mathf.RoundToInt(_layout.HeadlineFontSize);
            _buttonStyle.fontSize = Mathf.RoundToInt(_layout.FontSize);
        }
    }
}
