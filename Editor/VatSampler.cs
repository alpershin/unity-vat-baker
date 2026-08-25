using System.Collections.Generic;
using UnityEngine;

namespace Alpershin.Vat.EditorTools
{
    /// <summary>
    /// Samples animation clips onto a skinned prefab instance and reads the deformed vertices
    /// back frame by frame, in the space of the instance root.
    /// </summary>
    internal sealed class VatSampler
    {
        private readonly GameObject _instance;
        private readonly SkinnedMeshRenderer[] _renderers;
        private readonly float _fps;
        private readonly bool _bakeNormals;
        private readonly int _vertexCount;
        private readonly List<Vector3> _vertexBuffer;
        private readonly List<Vector3> _normalBuffer;

        private Vector3[] _positions;
        private Vector3[] _normals;
        private Bounds _bounds;
        private bool _hasBounds;

        public VatSampler(GameObject instance, SkinnedMeshRenderer[] renderers, float fps, bool bakeNormals)
        {
            _instance = instance;
            _renderers = renderers;
            _fps = fps;
            _bakeNormals = bakeNormals;
            _vertexCount = CountVertices(renderers);
            _vertexBuffer = new List<Vector3>(_vertexCount);
            _normalBuffer = new List<Vector3>(_vertexCount);
        }

        public static int CountVertices(SkinnedMeshRenderer[] renderers)
        {
            var total = 0;
            for (var i = 0; i < renderers.Length; i++)
            {
                var mesh = renderers[i].sharedMesh;
                if (mesh != null)
                {
                    total += mesh.vertexCount;
                }
            }

            return total;
        }

        public VatSampleBuffer Sample(List<VatSourceClip> clips)
        {
            var table = BuildTable(clips);
            var frameCount = CountFrames(table);

            _positions = new Vector3[_vertexCount * frameCount];
            _normals = _bakeNormals ? new Vector3[_vertexCount * frameCount] : null;
            _hasBounds = false;

            var scratch = new Mesh { name = "VatScratch" };
            try
            {
                var row = 0;
                for (var clipIndex = 0; clipIndex < clips.Count; clipIndex++)
                {
                    var clip = clips[clipIndex].Clip;
                    for (var frame = 0; frame < table[clipIndex].FrameCount; frame++)
                    {
                        clip.SampleAnimation(_instance, frame / _fps);
                        WriteFrame(scratch, row * _vertexCount);
                        row++;
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(scratch);
            }

            return new VatSampleBuffer(_positions, _normals, _bounds, _vertexCount, frameCount, table);
        }

        private VatClip[] BuildTable(List<VatSourceClip> clips)
        {
            var table = new VatClip[clips.Count];
            var start = 0;

            for (var i = 0; i < clips.Count; i++)
            {
                var source = clips[i];
                var clip = source.Clip;
                var frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * _fps));

                // Sampling always runs at the bake rate; the state's speed lives in the playback
                // rate the shader reads, so a faster state costs no extra frames.
                table[i] = new VatClip(source.Name, start, frames, _fps * source.Speed, clip.isLooping, source.TransitionDuration);
                start += frames;
            }

            return table;
        }

        private static int CountFrames(VatClip[] table)
        {
            var total = 0;
            for (var i = 0; i < table.Length; i++)
            {
                total += table[i].FrameCount;
            }

            return total;
        }

        private void WriteFrame(Mesh scratch, int offset)
        {
            var cursor = offset;
            for (var i = 0; i < _renderers.Length; i++)
            {
                cursor = WriteRenderer(_renderers[i], scratch, cursor);
            }
        }

        private int WriteRenderer(SkinnedMeshRenderer renderer, Mesh scratch, int cursor)
        {
            if (renderer.sharedMesh == null)
            {
                return cursor;
            }

            renderer.BakeMesh(scratch, false);
            scratch.GetVertices(_vertexBuffer);

            var toRoot = _instance.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (var i = 0; i < _vertexBuffer.Count; i++)
            {
                var position = toRoot.MultiplyPoint3x4(_vertexBuffer[i]);
                _positions[cursor + i] = position;
                Encapsulate(position);
            }

            if (_normals != null)
            {
                scratch.GetNormals(_normalBuffer);
                for (var i = 0; i < _normalBuffer.Count; i++)
                {
                    _normals[cursor + i] = toRoot.MultiplyVector(_normalBuffer[i]).normalized;
                }
            }

            return cursor + _vertexBuffer.Count;
        }

        private void Encapsulate(Vector3 position)
        {
            if (_hasBounds)
            {
                _bounds.Encapsulate(position);
                return;
            }

            _bounds = new Bounds(position, Vector3.zero);
            _hasBounds = true;
        }
    }
}
