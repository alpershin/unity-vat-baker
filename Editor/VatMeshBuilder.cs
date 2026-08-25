using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Alpershin.Vat.EditorTools
{
    /// <summary>
    /// Merges the skinned renderers of a prefab into one static mesh whose vertex order matches
    /// the VAT texture: TEXCOORD1.x of every vertex points at its own column in the map.
    /// </summary>
    internal static class VatMeshBuilder
    {
        public static VatMeshResult Build(SkinnedMeshRenderer[] renderers, VatSampleBuffer buffer, string meshName, Mesh target = null)
        {
            var vertexCount = buffer.VertexCount;
            var uv0 = new List<Vector2>(vertexCount);
            var vertexIndices = new List<Vector2>(vertexCount);
            var materials = new List<Material>();
            var triangleSets = new List<List<int>>();
            var offset = 0;

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                var shared = renderer.sharedMesh;
                if (shared == null)
                {
                    continue;
                }

                AppendUv(shared, uv0);
                AppendVertexIndices(vertexIndices, offset, shared.vertexCount, vertexCount);
                AppendTriangles(renderer, shared, offset, materials, triangleSets);
                offset += shared.vertexCount;
            }

            var mesh = CreateMesh(meshName, buffer, uv0, vertexIndices, triangleSets, target);
            return new VatMeshResult(mesh, materials.ToArray());
        }

        private static Mesh CreateMesh(string meshName, VatSampleBuffer buffer, List<Vector2> uv0, List<Vector2> vertexIndices, List<List<int>> triangleSets, Mesh target)
        {
            var vertexCount = buffer.VertexCount;

            // Refilling the mesh that is already a sub-asset keeps its file id, and with it every
            // reference a prefab or scene holds to it.
            var mesh = target != null ? target : new Mesh();
            mesh.Clear();
            mesh.name = meshName;
            mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.vertices = Slice(buffer.Positions, vertexCount);
            if (buffer.HasNormals)
            {
                mesh.normals = Slice(buffer.Normals, vertexCount);
            }

            mesh.uv = uv0.ToArray();
            mesh.uv2 = vertexIndices.ToArray();
            mesh.subMeshCount = triangleSets.Count;

            for (var i = 0; i < triangleSets.Count; i++)
            {
                mesh.SetTriangles(triangleSets[i], i, false);
            }

            mesh.bounds = buffer.Bounds;
            return mesh;
        }

        private static Vector3[] Slice(Vector3[] source, int length)
        {
            var slice = new Vector3[length];
            Array.Copy(source, slice, length);
            return slice;
        }

        private static void AppendUv(Mesh shared, List<Vector2> uv0)
        {
            var uv = shared.uv;
            for (var i = 0; i < shared.vertexCount; i++)
            {
                uv0.Add(i < uv.Length ? uv[i] : Vector2.zero);
            }
        }

        private static void AppendVertexIndices(List<Vector2> vertexIndices, int offset, int count, int totalVertices)
        {
            for (var i = 0; i < count; i++)
            {
                vertexIndices.Add(new Vector2((offset + i + 0.5f) / totalVertices, 0f));
            }
        }

        private static void AppendTriangles(SkinnedMeshRenderer renderer, Mesh shared, int offset, List<Material> materials, List<List<int>> triangleSets)
        {
            var sharedMaterials = renderer.sharedMaterials;
            for (var subMesh = 0; subMesh < shared.subMeshCount; subMesh++)
            {
                var material = subMesh < sharedMaterials.Length ? sharedMaterials[subMesh] : null;
                var target = GetOrCreateSet(materials, triangleSets, material);
                var triangles = shared.GetTriangles(subMesh);

                for (var i = 0; i < triangles.Length; i++)
                {
                    target.Add(triangles[i] + offset);
                }
            }
        }

        private static List<int> GetOrCreateSet(List<Material> materials, List<List<int>> triangleSets, Material material)
        {
            var index = materials.IndexOf(material);
            if (index >= 0)
            {
                return triangleSets[index];
            }

            materials.Add(material);
            var set = new List<int>();
            triangleSets.Add(set);
            return set;
        }
    }
}
