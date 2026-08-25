using UnityEngine;

namespace Alpershin.Vat.EditorTools
{
    /// <summary>
    /// Raw result of sampling: vertex positions (and normals) for every baked frame, laid out
    /// frame by frame, plus the clip table describing where each clip starts.
    /// </summary>
    internal sealed class VatSampleBuffer
    {
        public VatSampleBuffer(Vector3[] positions, Vector3[] normals, Bounds bounds, int vertexCount, int frameCount, VatClip[] clips)
        {
            Positions = positions;
            Normals = normals;
            Bounds = bounds;
            VertexCount = vertexCount;
            FrameCount = frameCount;
            Clips = clips;
        }

        public Vector3[] Positions { get; }
        public Vector3[] Normals { get; }
        public Bounds Bounds { get; }
        public int VertexCount { get; }
        public int FrameCount { get; }
        public VatClip[] Clips { get; }

        public bool HasNormals => Normals != null;
    }
}
