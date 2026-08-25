using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Alpershin.Vat.Samples.Spawning
{
    /// <summary>
    /// Fills a rectangular XZ grid with prefab instances, in play mode and from the editor.
    /// Optionally sizes that grid to what a camera sees.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GridPrefabSpawner : MonoBehaviour
    {
        public enum PickMode
        {
            Sequential,
            Random
        }

        [Header("Prefabs")]
        [SerializeField] private GameObject[] _prefabs = Array.Empty<GameObject>();
        [SerializeField] private PickMode _pickMode = PickMode.Sequential;

        [Header("Grid")]
        [SerializeField, Min(1)] private int _columns = 10;
        [SerializeField, Min(1)] private int _rows = 10;
        [SerializeField] private Vector2 _spacing = new Vector2(2f, 2f);
        [SerializeField] private bool _centerOnPivot = true;
        [SerializeField] private Transform _root;

        [Header("Camera Fit")]
        [SerializeField] private bool _fitToCamera;
        [SerializeField] private Camera _camera;
        [SerializeField] private Vector2 _viewportPadding = new Vector2(0.05f, 0.05f);
        [SerializeField, Min(0)] private int _maxInstances = 500;
        [SerializeField] private bool _autoSpacingToFill = true;
        [SerializeField, Min(1f)] private float _maxFitDistance = 500f;

        [Header("Rotation")]
        [SerializeField] private Vector3 _rotationEuler = Vector3.zero;
        [SerializeField] private Vector2 _randomYawRange = Vector2.zero;

        [Header("Variation")]
        [SerializeField] private Vector2 _positionJitter = Vector2.zero;
        [SerializeField] private Vector2 _uniformScaleRange = Vector2.one;
        [SerializeField] private int _seed = 12345;

        [Header("Lifecycle")]
        [SerializeField] private bool _spawnOnStart = true;
        [SerializeField] private bool _drawGizmos = true;

        [SerializeField, HideInInspector] private List<GameObject> _spawned = new List<GameObject>();

        private const int MaxGizmoCells = 2048;
        private static readonly int[] FootprintOrder = { 0, 1, 3, 2 };

        public int SpawnedCount => _spawned.Count;
        public int MaxInstances => _maxInstances;

        private void Start()
        {
            if (_spawnOnStart && _spawned.Count == 0)
            {
                Spawn();
            }
        }

        [ContextMenu("Spawn")]
        public void Spawn()
        {
            Clear();

            var pool = BuildPrefabPool();
            if (pool.Count == 0)
            {
                Debug.LogWarning($"{nameof(GridPrefabSpawner)}: no usable prefabs assigned.", this);
                return;
            }

            var parent = _root != null ? _root : transform;
            var view = TryCreateCameraView(parent);
            if (_fitToCamera && view == null)
            {
                Debug.LogWarning($"{nameof(GridPrefabSpawner)}: Fit To Camera is on but no camera was found.", this);
            }

            var layout = BuildLayout(parent, view);
            var random = new System.Random(_seed);
            var limit = _maxInstances > 0 ? _maxInstances : int.MaxValue;

            for (var index = 0; index < layout.CellCount && _spawned.Count < limit; index++)
            {
                var cell = layout.GetCell(index);
                if (view != null && !view.Contains(parent.TransformPoint(cell)))
                {
                    continue;
                }

                var instance = InstantiatePrefab(PickPrefab(pool, index, random), parent);
                Place(instance.transform, cell, random);
                _spawned.Add(instance);
            }
        }

        /// <summary>
        /// Caps how many units the next <see cref="Spawn"/> lays out. With auto spacing on, the
        /// grid also gets denser or sparser to keep filling the same camera footprint.
        /// </summary>
        public void SetMaxInstances(int maxInstances)
        {
            _maxInstances = Mathf.Max(0, maxInstances);
        }

        /// <summary>
        /// Replaces the prefab pool at runtime. Call <see cref="Spawn"/> afterwards to refill the grid.
        /// </summary>
        public void SetPrefabs(IReadOnlyList<GameObject> prefabs)
        {
            if (prefabs == null)
            {
                _prefabs = Array.Empty<GameObject>();
                return;
            }

            _prefabs = new GameObject[prefabs.Count];
            for (var i = 0; i < prefabs.Count; i++)
            {
                _prefabs[i] = prefabs[i];
            }
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            for (var i = 0; i < _spawned.Count; i++)
            {
                var instance = _spawned[i];
                if (instance == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(instance);
                    continue;
                }

#if UNITY_EDITOR
                Undo.DestroyObjectImmediate(instance);
#else
                DestroyImmediate(instance);
#endif
            }

            _spawned.Clear();
        }

        private XZGridLayout BuildLayout(Transform parent, CameraViewportGround view)
        {
            if (view == null)
            {
                return new XZGridLayout(_columns, _rows, _spacing, _centerOnPivot);
            }

            return XZGridLayout.FromArea(view.GetLocalArea(parent), ResolveSpacing(view.GetGroundArea(parent)));
        }

        private Vector2 ResolveSpacing(float visibleArea)
        {
            if (!_autoSpacingToFill || _maxInstances <= 0)
            {
                return _spacing;
            }

            var step = Mathf.Max(Mathf.Sqrt(Mathf.Max(visibleArea, Mathf.Epsilon) / _maxInstances), 0.01f);
            return new Vector2(step, step);
        }

        private CameraViewportGround TryCreateCameraView(Transform parent)
        {
            if (!_fitToCamera)
            {
                return null;
            }

            var camera = _camera != null ? _camera : Camera.main;
            if (camera == null)
            {
                return null;
            }

            var plane = new Plane(parent.up, parent.position);
            return new CameraViewportGround(camera, plane, _viewportPadding, _maxFitDistance);
        }

        private void Place(Transform instance, Vector3 cell, System.Random random)
        {
            var offset = new Vector3(
                RandomRange(random, -_positionJitter.x, _positionJitter.x),
                0f,
                RandomRange(random, -_positionJitter.y, _positionJitter.y));

            instance.localPosition = cell + offset;
            instance.localRotation = BuildRotation(random);

            var scale = RandomRange(random, _uniformScaleRange.x, _uniformScaleRange.y);
            instance.localScale = Vector3.one * scale;
        }

        private Quaternion BuildRotation(System.Random random)
        {
            var baseRotation = Quaternion.Euler(_rotationEuler);
            var yaw = RandomRange(random, _randomYawRange.x, _randomYawRange.y);
            return Quaternion.AngleAxis(yaw, Vector3.up) * baseRotation;
        }

        private GameObject PickPrefab(List<GameObject> pool, int index, System.Random random)
        {
            var slot = _pickMode == PickMode.Random
                ? random.Next(pool.Count)
                : index % pool.Count;

            return pool[slot];
        }

        private GameObject InstantiatePrefab(GameObject prefab, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var editorInstance = PrefabUtility.IsPartOfPrefabAsset(prefab)
                    ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)
                    : Instantiate(prefab, parent);
                Undo.RegisterCreatedObjectUndo(editorInstance, "Spawn Grid");
                return editorInstance;
            }
#endif
            return Instantiate(prefab, parent);
        }

        private List<GameObject> BuildPrefabPool()
        {
            var pool = new List<GameObject>(_prefabs.Length);
            for (var i = 0; i < _prefabs.Length; i++)
            {
                var prefab = _prefabs[i];
                if (prefab != null && prefab != gameObject)
                {
                    pool.Add(prefab);
                }
            }

            return pool;
        }

        private static float RandomRange(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private void OnDrawGizmosSelected()
        {
            if (!_drawGizmos)
            {
                return;
            }

            var parent = _root != null ? _root : transform;
            var view = TryCreateCameraView(parent);

            DrawFootprint(view);
            DrawCells(parent, view);
        }

        private void DrawCells(Transform parent, CameraViewportGround view)
        {
            var layout = BuildLayout(parent, view);
            var cellSize = new Vector3(0.4f, 0f, 0.4f);
            var drawn = Mathf.Min(layout.CellCount, MaxGizmoCells);
            var limit = _maxInstances > 0 ? _maxInstances : int.MaxValue;
            var used = 0;

            for (var index = 0; index < drawn; index++)
            {
                var world = parent.TransformPoint(layout.GetCell(index));
                var fits = (view == null || view.Contains(world)) && used < limit;
                if (fits)
                {
                    used++;
                }

                Gizmos.color = fits ? new Color(0.2f, 0.9f, 0.4f, 0.7f) : new Color(1f, 0.4f, 0.3f, 0.2f);
                Gizmos.DrawWireCube(world, cellSize);
            }
        }

        private void DrawFootprint(CameraViewportGround view)
        {
            if (view == null)
            {
                return;
            }

            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
            for (var i = 0; i < FootprintOrder.Length; i++)
            {
                var from = view.GetCornerOnPlane(FootprintOrder[i]);
                var to = view.GetCornerOnPlane(FootprintOrder[(i + 1) % FootprintOrder.Length]);
                Gizmos.DrawLine(from, to);
            }
        }
    }
}
